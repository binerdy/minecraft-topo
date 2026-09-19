using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>
/// swissBATHY3D lake floors (©swisstopo): one zipped XYZ archive per lake (1 m grid, split into
/// 1 km tiles inside the archive), found through the STAC API. Only tiles under the block grid are parsed.
/// </summary>
public sealed class SwissBathy3dSource
{
    public const string StacItemsUrl = "https://data.geo.admin.ch/api/stac/v0.9/collections/ch.swisstopo.swissbathy3d/items";

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    public SwissBathy3dSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _cacheDir = paths.BathyCacheDir;
    }

    public sealed record LakeInfo(string Id, string Name, Lv95Rect Bounds, string Url, long? Bytes);

    /// <summary>Lakes with bathymetry that intersect the rectangle.</summary>
    public async Task<IReadOnlyList<LakeInfo>> FindLakesAsync(Lv95Rect rect, CancellationToken ct)
    {
        var (west, south, east, north) = rect.ToWgs84Bbox();
        string bbox = string.Create(CultureInfo.InvariantCulture, $"{west:0.######},{south:0.######},{east:0.######},{north:0.######}");
        string json = await _downloader.GetStringAsync($"{StacItemsUrl}?bbox={bbox}&limit=100", ct);
        using var doc = JsonDocument.Parse(json);
        var lakes = new List<LakeInfo>();
        if (!doc.RootElement.TryGetProperty("features", out var features)) return lakes;
        foreach (var f in features.EnumerateArray())
        {
            string id = f.GetProperty("id").GetString() ?? "";
            if (!f.TryGetProperty("assets", out var assets)) continue;
            string? href = null;
            long? bytes = null;
            foreach (var a in assets.EnumerateObject())
            {
                if (!a.Name.EndsWith(".xyz.zip", StringComparison.OrdinalIgnoreCase)) continue;
                href = a.Value.GetProperty("href").GetString();
                if (a.Value.TryGetProperty("file:size", out var fs) && fs.TryGetInt64(out long b)) bytes = b;
                break;
            }
            if (href is null) continue;
            var bounds = rect;
            if (f.TryGetProperty("bbox", out var bb) && bb.GetArrayLength() == 4)
            {
                var sw = Lv95.FromWgs84(bb[1].GetDouble(), bb[0].GetDouble());
                var ne = Lv95.FromWgs84(bb[3].GetDouble(), bb[2].GetDouble());
                bounds = new Lv95Rect(sw.E, sw.N, ne.E, ne.N);
            }
            string name = id.Replace("swissbathy3d_", "");
            name = char.ToUpperInvariant(name[0]) + name[1..];
            lakes.Add(new LakeInfo(id, name, bounds, href, bytes));
        }
        return lakes;
    }

    /// <summary>Lake floor elevation per grid cell (NaN where no bathymetry exists), or null when no lake is covered.</summary>
    public async Task<float[]?> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        progress?.Report(new ProgressInfo("bathy", 0, "Looking up swissBATHY3D lake floors"));
        var lakes = await FindLakesAsync(rect, ct);
        if (lakes.Count == 0)
        {
            progress?.Report(new ProgressInfo("bathy", 100, "No surveyed lake floor in this area"));
            return null;
        }

        var tiles = new Dictionary<(int, int), HeightGrid>();
        foreach (var lake in lakes)
        {
            ct.ThrowIfCancellationRequested();
            string path = Path.Combine(_cacheDir, lake.Id + ".xyz.zip");
            bool cached = File.Exists(path);
            if (!cached)
            {
                long shown = -1;
                var bytes = new Progress<(long Received, long? Total)>(p =>
                {
                    long mb = p.Received / 5_000_000;
                    if (mb == shown) return;
                    shown = mb;
                    double pct = p.Total is { } t && t > 0 ? 90.0 * p.Received / t : 0;
                    progress?.Report(new ProgressInfo("bathy", pct, $"Downloading lake floor of {lake.Name}: {p.Received / 1_048_576} of {(p.Total ?? lake.Bytes ?? 0) / 1_048_576} MB (one-time)"));
                });
                await _downloader.GetFileAsync(lake.Url, path, bytes, ct);
            }
            progress?.Report(new ProgressInfo("bathy", 90, $"Reading lake floor of {lake.Name}"));
            await Task.Run(() => ParseTiles(path, rect, tiles, ct), ct);
        }
        if (tiles.Count == 0)
        {
            progress?.Report(new ProgressInfo("bathy", 100, "The lake floor data does not reach into this area"));
            return null;
        }

        var bed = new float[grid.Width * grid.Height];
        int filled = 0;
        Parallel.For(0, grid.Height, new ParallelOptions { CancellationToken = ct }, z =>
        {
            double n = grid.CellCenterN(z);
            int nkm = (int)Math.Floor(n / 1000);
            int local = 0;
            for (int x = 0; x < grid.Width; x++)
            {
                double e = grid.CellCenterE(x);
                int ekm = (int)Math.Floor(e / 1000);
                float v = tiles.TryGetValue((ekm, nkm), out var t) && t.ContainsPoint(e, n) ? t.SampleBilinear(e, n) : float.NaN;
                bed[z * grid.Width + x] = v;
                if (!float.IsNaN(v)) local++;
            }
            Interlocked.Add(ref filled, local);
        });
        progress?.Report(new ProgressInfo("bathy", 100, $"Lake floor known for {filled:N0} cells ({string.Join(", ", lakes.Select(l => l.Name))})"));
        return filled > 0 ? bed : null;
    }

    /// <summary>Parses the 1 km XYZ tiles of a lake archive that intersect the rectangle.</summary>
    private static void ParseTiles(string zipPath, Lv95Rect rect, Dictionary<(int, int), HeightGrid> tiles, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (!entry.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase)) continue;
            // swissBATHY3D_CHLV95_LN02_<Ekm>_<Nkm>.xyz
            var parts = Path.GetFileNameWithoutExtension(entry.Name).Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[^2], out int ekm) || !int.TryParse(parts[^1], out int nkm)) continue;
            var tileRect = new Lv95Rect(ekm * 1000, nkm * 1000, ekm * 1000 + 1000, nkm * 1000 + 1000);
            if (!tileRect.Intersects(rect)) continue;
            ct.ThrowIfCancellationRequested();
            var g = SwissAlti3dSource.ParseXyzEntry(entry, 1, ct);
            if (g is not null) tiles[(ekm, nkm)] = g;
        }
    }
}
