using System.Globalization;
using System.IO.Compression;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>
/// Loads swisstopo's free DHM25/200 m height model of Switzerland (one 45 MB zip) once and
/// keeps it in memory. Used for the overview map and for coarse worlds.
/// Source: https://www.swisstopo.admin.ch/en/height-model-dhm25-200m (©swisstopo).
/// </summary>
public sealed class Dhm200Model
{
    public const string DownloadUrl = "https://data.geo.admin.ch/ch.swisstopo.digitales-hoehenmodell_25/data.zip";
    private const string AscEntryName = "DHM200.asc";

    private readonly Downloader _downloader;
    private readonly string _cacheDir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HeightGrid? _grid;
    private byte[]? _shade;
    private byte[]? _slope;

    public Dhm200Model(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _cacheDir = paths.Dhm200CacheDir;
    }

    /// <summary>Latest known loading state, for status endpoints.</summary>
    public ProgressInfo Status { get; private set; } = new("idle", 0, "Not loaded");

    public bool IsReady => _grid is not null;

    /// <summary>The loaded grid (throws when not loaded yet). Use <see cref="GetAsync"/> to load.</summary>
    public HeightGrid Grid => _grid ?? throw new InvalidOperationException("DHM200 is not loaded yet.");

    /// <summary>Hillshade per cell, 0..255 (128 = flat), same layout as <see cref="Grid"/>.</summary>
    public byte[] Shade => _shade ?? throw new InvalidOperationException("DHM200 is not loaded yet.");

    /// <summary>Slope per cell in whole degrees, same layout as <see cref="Grid"/>.</summary>
    public byte[] Slope => _slope ?? throw new InvalidOperationException("DHM200 is not loaded yet.");

    public async Task<HeightGrid> GetAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (_grid is not null) return _grid;
        await _gate.WaitAsync(ct);
        try
        {
            if (_grid is not null) return _grid;

            string zipPath = Path.Combine(_cacheDir, "data.zip");
            Report(new ProgressInfo("downloading", 0, "Downloading DHM200 (45 MB) from swisstopo"), progress);
            var byteProgress = new Progress<(long Received, long? Total)>(p =>
            {
                double pct = p.Total is { } t && t > 0 ? 100.0 * p.Received / t : 0;
                Report(new ProgressInfo("downloading", pct, $"Downloading DHM200: {p.Received / 1_048_576.0:0.0} MB"), progress);
            });
            await _downloader.GetFileAsync(DownloadUrl, zipPath, byteProgress, ct);

            Report(new ProgressInfo("parsing", 0, "Parsing DHM200 grid"), progress);
            var grid = await Task.Run(() => ParseAscFromZip(zipPath, ct), ct);

            Report(new ProgressInfo("parsing", 90, "Computing hillshade"), progress);
            (_slope, _shade) = await Task.Run(() => ComputeShading(grid), ct);
            _grid = grid;
            Report(new ProgressInfo("ready", 100, "DHM200 ready"), progress);
            return grid;
        }
        catch (Exception ex)
        {
            Report(new ProgressInfo("error", 0, ex.Message), progress);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Report(ProgressInfo info, IProgress<ProgressInfo>? progress)
    {
        Status = info;
        progress?.Report(info);
    }

    /// <summary>Parses the ESRI ASCII grid inside the zip. LV03 coordinates are shifted to LV95.</summary>
    internal static HeightGrid ParseAscFromZip(string zipPath, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(AscEntryName)
                    ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".asc", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("DHM200 zip does not contain an .asc grid.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1 << 16);
        return ParseEsriAscii(reader, lv03ToLv95: true, ct);
    }

    internal static HeightGrid ParseEsriAscii(TextReader reader, bool lv03ToLv95, CancellationToken ct)
    {
        int ncols = 0, nrows = 0;
        double xll = 0, yll = 0, cell = 0, nodata = -9999;
        bool xIsCenter = false, yIsCenter = false;

        // Header: up to 6 key/value lines.
        for (int i = 0; i < 6; i++)
        {
            string? line = reader.ReadLine() ?? throw new InvalidDataException("Unexpected end of ASCII grid header.");
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new InvalidDataException($"Bad ASCII grid header line: '{line}'.");
            double val = double.Parse(parts[1], CultureInfo.InvariantCulture);
            switch (parts[0].ToUpperInvariant())
            {
                case "NCOLS": ncols = (int)val; break;
                case "NROWS": nrows = (int)val; break;
                case "XLLCORNER": xll = val; break;
                case "XLLCENTER": xll = val; xIsCenter = true; break;
                case "YLLCORNER": yll = val; break;
                case "YLLCENTER": yll = val; yIsCenter = true; break;
                case "CELLSIZE": cell = val; break;
                case "NODATA_VALUE": nodata = val; i = 6; break; // last header line
                default: throw new InvalidDataException($"Unknown ASCII grid header key '{parts[0]}'.");
            }
        }
        if (ncols <= 0 || nrows <= 0 || cell <= 0) throw new InvalidDataException("Incomplete ASCII grid header.");

        double originE = xIsCenter ? xll - cell / 2 : xll;
        double originN = (yIsCenter ? yll - cell / 2 : yll) + nrows * cell;
        if (lv03ToLv95) { originE += 2_000_000; originN += 1_000_000; }

        var data = new float[ncols * nrows];
        int idx = 0;
        var buffer = new char[1 << 16];
        var token = new System.Text.StringBuilder(16);
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (char.IsWhiteSpace(c))
                {
                    if (token.Length > 0) { Emit(); }
                }
                else token.Append(c);
            }
        }
        if (token.Length > 0) Emit();
        if (idx != data.Length) throw new InvalidDataException($"ASCII grid has {idx} values, expected {data.Length}.");
        return new HeightGrid(originE, originN, cell, ncols, nrows, data);

        void Emit()
        {
            if (idx >= data.Length) throw new InvalidDataException("ASCII grid has more values than declared.");
            double v = double.Parse(token.ToString(), CultureInfo.InvariantCulture);
            data[idx++] = Math.Abs(v - nodata) < 0.5 ? float.NaN : (float)v;
            token.Clear();
        }
    }

    /// <summary>Slope (whole degrees) and hillshade (0..255) for every cell; NaN cells get 0 / 128.</summary>
    private static (byte[] Slope, byte[] Shade) ComputeShading(HeightGrid grid)
    {
        var slope = new byte[grid.Data.Length];
        var shade = new byte[grid.Data.Length];
        // light from the north-west, 45° above the horizon
        double az = 315.0 * Math.PI / 180.0, alt = 45.0 * Math.PI / 180.0;
        double lx = Math.Cos(alt) * Math.Sin(az), ly = Math.Cos(alt) * Math.Cos(az), lz = Math.Sin(alt);

        Parallel.For(0, grid.Height, z =>
        {
            for (int x = 0; x < grid.Width; x++)
            {
                int i = z * grid.Width + x;
                if (float.IsNaN(grid.Data[i])) { slope[i] = 0; shade[i] = 128; continue; }
                var (de, dn) = SafeGradient(grid, x, z);
                double s = Math.Atan(Math.Sqrt(de * de + dn * dn)) * 180.0 / Math.PI;
                slope[i] = (byte)Math.Clamp(Math.Round(s), 0, 90);
                // surface normal (-dE, -dN, 1) normalised; dot with light
                double nx = -de, ny = -dn, nz = 1.0;
                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double dot = (nx * lx + ny * ly + nz * lz) / len;
                // flat ground (normal 0,0,1) gives dot == lz; map that to 128
                shade[i] = (byte)Math.Clamp(Math.Round(128 + 127 * (dot - lz) / (1 - lz)), 0, 255);
            }
        });
        return (slope, shade);
    }

    private static (double DE, double DN) SafeGradient(HeightGrid g, int x, int z)
    {
        float c = g[x, z];
        float l = Val(x - 1, z), r = Val(x + 1, z), u = Val(x, z - 1), d = Val(x, z + 1);
        double de = (r - l) / (2 * g.CellSize);
        double dn = (u - d) / (2 * g.CellSize);
        return (de, dn);

        float Val(int xx, int zz)
        {
            if (xx < 0 || zz < 0 || xx >= g.Width || zz >= g.Height) return c;
            float v = g[xx, zz];
            return float.IsNaN(v) ? c : v;
        }
    }
}
