using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using ACadSharp.Entities;
using ACadSharp.IO;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Buildings;

/// <summary>Per-cell building geometry: roof and floor heights in metres above sea level.</summary>
public sealed class BuildingModel
{
    public required int[] Id { get; init; }
    public required float[] RoofHeight { get; init; }
    public required float[] FloorHeight { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// swissBUILDINGS3D 3.0 (©swisstopo): measured building models with real roof shapes, delivered
/// as DWG tiles through the geo.admin.ch STAC API. The "separated" drawing holds one polyface mesh
/// per roof, wall and floor surface; roof and floor faces are rasterised into per-block heights.
/// </summary>
public sealed class SwissBuildings3dSource
{
    public const string StacItemsUrl = "https://data.geo.admin.ch/api/stac/v0.9/collections/ch.swisstopo.swissbuildings3d_3_0/items";

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    public SwissBuildings3dSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _cacheDir = paths.Buildings3dCacheDir;
    }

    public string Name => "swissBUILDINGS3D 3.0";

    public sealed record TileInfo(string Key, int Year, string Url)
    {
        public string FileName => Path.GetFileName(new Uri(Url).LocalPath);
    }

    /// <summary>Newest DWG tile per map-sheet key intersecting the rectangle.</summary>
    public async Task<IReadOnlyList<TileInfo>> FindTilesAsync(Lv95Rect rect, CancellationToken ct)
    {
        var (west, south, east, north) = rect.ToWgs84Bbox();
        string? url = string.Create(CultureInfo.InvariantCulture, $"{StacItemsUrl}?bbox={west:0.######},{south:0.######},{east:0.######},{north:0.######}&limit=100");
        var best = new Dictionary<string, TileInfo>();
        int pages = 0;
        while (url is not null && ++pages < 50)
        {
            string json = await _downloader.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("features", out var features))
            {
                foreach (var f in features.EnumerateArray())
                {
                    string id = f.GetProperty("id").GetString() ?? ""; // swissbuildings3d_3_0_2020_1288-31
                    var parts = id.Split('_');
                    if (parts.Length < 5 || !int.TryParse(parts[3], out int year)) continue;
                    string key = parts[4];
                    string? href = null;
                    if (f.TryGetProperty("assets", out var assets))
                        foreach (var a in assets.EnumerateObject())
                            if (a.Name.EndsWith(".dwg.zip", StringComparison.OrdinalIgnoreCase) && a.Value.TryGetProperty("href", out var h)) { href = h.GetString(); break; }
                    if (href is null) continue;
                    if (!best.TryGetValue(key, out var cur) || cur.Year < year) best[key] = new TileInfo(key, year, href);
                }
            }
            url = null;
            if (root.TryGetProperty("links", out var links))
                foreach (var l in links.EnumerateArray())
                    if (l.TryGetProperty("rel", out var rel) && rel.GetString() == "next" && l.TryGetProperty("href", out var h)) { url = h.GetString(); break; }
        }
        return best.Values.ToList();
    }

    public async Task<BuildingModel> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        progress?.Report(new ProgressInfo("buildings", 0, "Looking up swissBUILDINGS3D tiles"));
        var tiles = await FindTilesAsync(rect, ct);
        if (tiles.Count == 0) throw new InvalidOperationException("No swissBUILDINGS3D tiles cover the selected area.");

        int n = grid.Width * grid.Height;
        var model = new BuildingModel { Id = new int[n], RoofHeight = new float[n], FloorHeight = new float[n] };
        Array.Fill(model.RoofHeight, float.NaN);
        Array.Fill(model.FloorHeight, float.NaN);
        int nextId = 0;

        int done = 0;
        foreach (var tile in tiles)
        {
            ct.ThrowIfCancellationRequested();
            string dwg = await EnsureSeparatedDwgAsync(tile, progress, done, tiles.Count, ct);
            progress?.Report(new ProgressInfo("buildings", 100.0 * (done + 0.5) / tiles.Count, $"Reading {Path.GetFileName(dwg)}", done, tiles.Count));
            await Task.Run(() => Rasterise(dwg, grid, model, ref nextId, ct), ct);
            done++;
        }
        var result = new BuildingModel { Id = model.Id, RoofHeight = model.RoofHeight, FloorHeight = model.FloorHeight, Count = nextId };
        progress?.Report(new ProgressInfo("buildings", 100, $"swissBUILDINGS3D: {nextId:N0} roofs, {model.Id.Count(i => i != 0):N0} building cells"));
        return result;
    }

    /// <summary>Downloads the tile zip (about 45 MB) once and keeps only the small "separated" drawing.</summary>
    private async Task<string> EnsureSeparatedDwgAsync(TileInfo tile, IProgress<ProgressInfo>? progress, int done, int total, CancellationToken ct)
    {
        string dwgPath = Path.Combine(_cacheDir, Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(tile.FileName)) + "_separated.dwg");
        if (File.Exists(dwgPath)) return dwgPath;

        string zipPath = Path.Combine(_cacheDir, tile.FileName);
        var bytes = new Progress<(long Received, long? Total)>(p =>
            progress?.Report(new ProgressInfo("buildings", 100.0 * (done + (p.Total is { } t && t > 0 ? 0.4 * p.Received / t : 0)) / total,
                $"Downloading {tile.FileName} ({p.Received / 5_242_880 * 5} of {(p.Total ?? 0) / 1_048_576} MB)", done, total)));
        await _downloader.GetFileAsync(tile.Url, zipPath, bytes, ct);

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Contains("separated", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"{tile.FileName} contains no 'separated' DWG.");
            entry.ExtractToFile(dwgPath, overwrite: true);
        }
        try { File.Delete(zipPath); } catch (IOException) { }
        return dwgPath;
    }

    private static void Rasterise(string dwgPath, HeightGrid grid, BuildingModel model, ref int nextId, CancellationToken ct)
    {
        using var reader = new DwgReader(dwgPath);
        var doc = reader.Read();
        int w = grid.Width, h = grid.Height;
        var verts = new List<(double X, double Z, double H)>(64);

        foreach (var insert in doc.Entities.OfType<Insert>())
        {
            ct.ThrowIfCancellationRequested();
            string layer = insert.Layer.Name;
            bool roof = layer.Equals("Roof", StringComparison.OrdinalIgnoreCase);
            bool floor = layer.Equals("Floor", StringComparison.OrdinalIgnoreCase);
            if (!roof && !floor) continue;

            var mesh = insert.Block.Entities.OfType<PolyfaceMesh>().FirstOrDefault();
            if (mesh is null) continue;

            // Block geometry is local to the insert point (heights are absolute).
            double ix = insert.InsertPoint.X, iy = insert.InsertPoint.Y;
            double cos = Math.Cos(insert.Rotation), sin = Math.Sin(insert.Rotation);
            double sx = insert.XScale == 0 ? 1 : insert.XScale, sy = insert.YScale == 0 ? 1 : insert.YScale;
            verts.Clear();
            bool touches = false;
            foreach (var v in mesh.Vertices)
            {
                double lx = v.Location.X * sx, ly = v.Location.Y * sy;
                double e = ix + lx * cos - ly * sin, nn = iy + lx * sin + ly * cos;
                double gx = (e - grid.OriginE) / grid.CellSize, gz = (grid.OriginN - nn) / grid.CellSize;
                if (gx >= 0 && gz >= 0 && gx < w && gz < h) touches = true;
                verts.Add((gx, gz, v.Location.Z));
            }
            if (!touches) continue;

            int id = roof ? ++nextId : 0;
            foreach (var face in mesh.Faces)
            {
                int a = Math.Abs(face.Index1), b = Math.Abs(face.Index2), c = Math.Abs(face.Index3), d = Math.Abs(face.Index4);
                if (a == 0 || b == 0 || c == 0 || a > verts.Count || b > verts.Count || c > verts.Count) continue;
                Triangle(verts[a - 1], verts[b - 1], verts[c - 1], roof, id, grid, model);
                if (d != 0 && d <= verts.Count) Triangle(verts[a - 1], verts[c - 1], verts[d - 1], roof, id, grid, model);
            }
        }
    }

    /// <summary>Rasterises one triangle: roof faces raise the roof height, floor faces lower the floor height.</summary>
    private static void Triangle((double X, double Z, double H) p, (double X, double Z, double H) q, (double X, double Z, double H) r,
        bool roof, int id, HeightGrid grid, BuildingModel model)
    {
        int w = grid.Width, h = grid.Height;
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(p.X, Math.Min(q.X, r.X)))), x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(p.X, Math.Max(q.X, r.X))));
        int z0 = Math.Max(0, (int)Math.Floor(Math.Min(p.Z, Math.Min(q.Z, r.Z)))), z1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(p.Z, Math.Max(q.Z, r.Z))));
        if (x0 > x1 || z0 > z1) return;
        double det = (q.X - p.X) * (r.Z - p.Z) - (r.X - p.X) * (q.Z - p.Z);
        bool degenerate = Math.Abs(det) < 1e-9;
        for (int z = z0; z <= z1; z++)
        {
            double cz = z + 0.5;
            for (int x = x0; x <= x1; x++)
            {
                double cx = x + 0.5;
                double hgt;
                if (degenerate)
                {
                    // near-vertical or sliver face: mark the cells it touches with its max height
                    if (!NearSegment(cx, cz, p, q) && !NearSegment(cx, cz, q, r) && !NearSegment(cx, cz, r, p)) continue;
                    hgt = Math.Max(p.H, Math.Max(q.H, r.H));
                }
                else
                {
                    double l1 = ((q.X - cx) * (r.Z - cz) - (r.X - cx) * (q.Z - cz)) / det;
                    double l2 = ((r.X - cx) * (p.Z - cz) - (p.X - cx) * (r.Z - cz)) / det;
                    double l3 = 1 - l1 - l2;
                    const double eps = -0.02;
                    if (l1 < eps || l2 < eps || l3 < eps) continue;
                    hgt = l1 * p.H + l2 * q.H + l3 * r.H;
                }
                int i = z * w + x;
                if (roof)
                {
                    if (float.IsNaN(model.RoofHeight[i]) || hgt > model.RoofHeight[i]) { model.RoofHeight[i] = (float)hgt; model.Id[i] = id; }
                }
                else if (float.IsNaN(model.FloorHeight[i]) || hgt < model.FloorHeight[i]) model.FloorHeight[i] = (float)hgt;
            }
        }

        static bool NearSegment(double cx, double cz, (double X, double Z, double H) a, (double X, double Z, double H) b)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
            double t = len2 > 0 ? Math.Clamp(((cx - a.X) * dx + (cz - a.Z) * dz) / len2, 0, 1) : 0;
            double px = a.X + t * dx - cx, pz = a.Z + t * dz - cz;
            return px * px + pz * pz <= 0.25;
        }
    }
}
