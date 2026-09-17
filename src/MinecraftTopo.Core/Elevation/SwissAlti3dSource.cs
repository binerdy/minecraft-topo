using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>
/// swissALTI3D elevation tiles (©swisstopo) discovered through the geo.admin.ch STAC API and
/// downloaded as zipped XYZ text (1 km x 1 km tiles in LV95).
/// </summary>
public sealed class SwissAlti3dSource : IElevationSource
{
    public const string StacItemsUrl = "https://data.geo.admin.ch/api/stac/v0.9/collections/ch.swisstopo.swissalti3d/items";
    private const int MaxParallelDownloads = 4;

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    /// <summary>Tile resolution in metres: 2 or 0.5.</summary>
    public double Resolution { get; }

    public SwissAlti3dSource(Downloader downloader, AppPaths paths, double resolution = 2)
    {
        if (resolution != 2 && resolution != 0.5) throw new ArgumentOutOfRangeException(nameof(resolution), "swissALTI3D is available at 2 m or 0.5 m.");
        _downloader = downloader;
        _cacheDir = paths.Alti3dCacheDir;
        Resolution = resolution;
    }

    public string Name => $"swissALTI3D {Resolution:0.#} m";
    public double NativeResolution => Resolution;

    /// <summary>Rough size of one zipped XYZ tile.</summary>
    public long EstimatedTileBytes => Resolution == 2 ? 2_500_000 : 40_000_000;

    public sealed record TileInfo(string Key, int EKm, int NKm, int Year, string Url)
    {
        public string FileName => Path.GetFileName(new Uri(Url).LocalPath);
    }

    /// <summary>Number of 1 km tiles a rectangle touches (no network).</summary>
    public static int CountTiles(Lv95Rect rect)
    {
        int e0 = (int)Math.Floor(rect.MinE / 1000), e1 = (int)Math.Floor((rect.MaxE - 1e-6) / 1000);
        int n0 = (int)Math.Floor(rect.MinN / 1000), n1 = (int)Math.Floor((rect.MaxN - 1e-6) / 1000);
        return Math.Max(0, e1 - e0 + 1) * Math.Max(0, n1 - n0 + 1);
    }

    public Task<(int Files, long Bytes)> EstimateDownloadAsync(Lv95Rect rect, CancellationToken ct)
    {
        int tiles = CountTiles(rect);
        return Task.FromResult((tiles, tiles * EstimatedTileBytes));
    }

    /// <summary>Queries the STAC API for all tiles intersecting the rectangle (newest year per tile).</summary>
    public async Task<IReadOnlyList<TileInfo>> FindTilesAsync(Lv95Rect rect, CancellationToken ct)
    {
        var (west, south, east, north) = rect.ToWgs84Bbox();
        string bbox = string.Create(CultureInfo.InvariantCulture, $"{west:0.######},{south:0.######},{east:0.######},{north:0.######}");
        string? url = $"{StacItemsUrl}?bbox={bbox}&limit=100";
        var best = new Dictionary<string, TileInfo>();
        string suffix = string.Create(CultureInfo.InvariantCulture, $"_{Resolution:0.#}_2056_");

        int pages = 0;
        while (url is not null)
        {
            if (++pages > 200) throw new InvalidOperationException("STAC pagination did not terminate.");
            string json = await _downloader.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("features", out var features))
            {
                foreach (var feature in features.EnumerateArray())
                {
                    string id = feature.GetProperty("id").GetString() ?? "";
                    // id: swissalti3d_<year>_<Ekm>-<Nkm>
                    var parts = id.Split('_');
                    if (parts.Length < 3) continue;
                    if (!int.TryParse(parts[1], out int year)) continue;
                    var km = parts[2].Split('-');
                    if (km.Length != 2 || !int.TryParse(km[0], out int ekm) || !int.TryParse(km[1], out int nkm)) continue;
                    var tileRect = new Lv95Rect(ekm * 1000, nkm * 1000, ekm * 1000 + 1000, nkm * 1000 + 1000);
                    if (!tileRect.Intersects(rect)) continue;

                    if (!feature.TryGetProperty("assets", out var assets)) continue;
                    string? href = null;
                    foreach (var asset in assets.EnumerateObject())
                    {
                        if (asset.Name.Contains(suffix, StringComparison.Ordinal) &&
                            asset.Name.EndsWith(".xyz.zip", StringComparison.OrdinalIgnoreCase) &&
                            asset.Value.TryGetProperty("href", out var h))
                        {
                            href = h.GetString();
                            break;
                        }
                    }
                    if (href is null) continue;

                    string key = parts[2];
                    if (!best.TryGetValue(key, out var existing) || existing.Year < year)
                    {
                        best[key] = new TileInfo(key, ekm, nkm, year, href);
                    }
                }
            }

            url = null;
            if (root.TryGetProperty("links", out var links))
            {
                foreach (var link in links.EnumerateArray())
                {
                    if (link.TryGetProperty("rel", out var rel) && rel.GetString() == "next" &&
                        link.TryGetProperty("href", out var h))
                    {
                        url = h.GetString();
                        break;
                    }
                }
            }
        }
        return best.Values.OrderBy(t => t.NKm).ThenBy(t => t.EKm).ToList();
    }

    public async Task FillAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        // Only tiles that contain a cell centre are needed: bilinear sampling clamps at tile edges,
        // which costs at most half a 2 m cell of accuracy along tile seams.
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        progress?.Report(new ProgressInfo("tiles", 0, "Looking up swissALTI3D tiles"));
        var tiles = await FindTilesAsync(rect, ct);
        if (tiles.Count == 0)
        {
            throw new InvalidOperationException("No swissALTI3D tiles cover the selected area. Is it inside Switzerland?");
        }
        progress?.Report(new ProgressInfo("tiles", 100, $"{tiles.Count} tiles cover the area", 0, tiles.Count));

        // Download (cached) and parse tiles.
        var parsed = new Dictionary<(int, int), HeightGrid>();
        var gate = new SemaphoreSlim(MaxParallelDownloads);
        int done = 0;
        var lockObj = new object();
        var tasks = tiles.Select(async tile =>
        {
            await gate.WaitAsync(ct);
            try
            {
                string path = Path.Combine(_cacheDir, tile.FileName);
                bool cached = File.Exists(path);
                await _downloader.GetFileAsync(tile.Url, path, null, ct);
                var tileGrid = await Task.Run(() => ParseXyzZip(path, Resolution, ct), ct);
                lock (lockObj)
                {
                    parsed[(tile.EKm, tile.NKm)] = tileGrid;
                    done++;
                    progress?.Report(new ProgressInfo("download", 100.0 * done / tiles.Count,
                        $"{(cached ? "Loaded" : "Downloaded")} {tile.FileName}", done, tiles.Count));
                }
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks);

        progress?.Report(new ProgressInfo("sample", 0, "Resampling to block grid"));
        await Task.Run(() =>
        {
            int rows = 0;
            Parallel.For(0, grid.Height, new ParallelOptions { CancellationToken = ct }, z =>
            {
                double n = grid.CellCenterN(z);
                int nkm = (int)Math.Floor(n / 1000);
                for (int x = 0; x < grid.Width; x++)
                {
                    double e = grid.CellCenterE(x);
                    int ekm = (int)Math.Floor(e / 1000);
                    grid[x, z] = parsed.TryGetValue((ekm, nkm), out var t) ? t.SampleBilinear(e, n) : float.NaN;
                }
                int r = Interlocked.Increment(ref rows);
                if ((r & 255) == 0) progress?.Report(new ProgressInfo("sample", 100.0 * r / grid.Height, "Resampling to block grid"));
            });
        }, ct);
        progress?.Report(new ProgressInfo("sample", 100, "Resampling done"));
    }

    /// <summary>Parses a zipped XYZ tile ("X Y Z" text lines, LV95) into a grid at the given resolution.</summary>
    internal static HeightGrid ParseXyzZip(string zipPath, double resolution, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"No .xyz entry in {Path.GetFileName(zipPath)}.");

        // First pass: read all points into arrays (a 2 m tile has 250k points, a 0.5 m tile 4M).
        var es = new List<float>(1 << 18);
        var ns = new List<float>(1 << 18);
        var hs = new List<float>(1 << 18);
        double minE = double.MaxValue, maxE = double.MinValue, minN = double.MaxValue, maxN = double.MinValue;

        using (var stream = entry.Open())
        using (var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1 << 16))
        {
            string? line;
            int lineNo = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNo++;
                if ((lineNo & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
                var span = line.AsSpan().Trim();
                if (span.IsEmpty) continue;
                if (!TryParse3(span, out double e, out double n, out double h)) continue; // header line
                es.Add((float)(e - 2_000_000)); // keep floats precise by removing the LV95 offset
                ns.Add((float)(n - 1_000_000));
                hs.Add((float)h);
                if (e < minE) minE = e; if (e > maxE) maxE = e;
                if (n < minN) minN = n; if (n > maxN) maxN = n;
            }
        }
        if (hs.Count == 0) throw new InvalidDataException($"{Path.GetFileName(zipPath)} contains no points.");

        int width = (int)Math.Round((maxE - minE) / resolution) + 1;
        int height = (int)Math.Round((maxN - minN) / resolution) + 1;
        var data = new float[width * height];
        Array.Fill(data, float.NaN);
        float baseE = (float)(minE - 2_000_000), baseN = (float)(maxN - 1_000_000);
        for (int i = 0; i < hs.Count; i++)
        {
            int x = (int)Math.Round((es[i] - baseE) / resolution);
            int z = (int)Math.Round((baseN - ns[i]) / resolution);
            if (x < 0 || z < 0 || x >= width || z >= height) continue;
            data[z * width + x] = hs[i];
        }
        // Points are cell centres: the north-west corner of cell (0,0) is half a cell up-left.
        return new HeightGrid(minE - resolution / 2, maxN + resolution / 2, resolution, width, height, data);
    }

    private static bool TryParse3(ReadOnlySpan<char> s, out double a, out double b, out double c)
    {
        a = b = c = 0;
        int i = 0;
        return Next(s, ref i, out a) && Next(s, ref i, out b) && Next(s, ref i, out c);

        static bool Next(ReadOnlySpan<char> s, ref int i, out double v)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == ';' || s[i] == ',')) i++;
            int start = i;
            while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != ';' && s[i] != ',') i++;
            return double.TryParse(s[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
