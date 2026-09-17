using System.Globalization;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Terrain;

/// <summary>
/// Renders Web-Mercator map tiles of the country model in a Minecraft-like palette. Every pixel
/// looks up the DHM200 cell under it (nearest neighbour), so at high zoom the 200 m cells show as
/// crisp squares like a Minecraft map. Lakes and rivers from the swisstopo water surfaces are
/// painted on top.
/// </summary>
public sealed class OverviewTileRenderer
{
    public const int MinZoom = 5;
    public const int MaxZoom = 16;

    private const string WaterWmsBase = "https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&STYLES=&CRS=EPSG:3857&FORMAT=image/png&TRANSPARENT=true";
    private const double MercatorHalf = 20_037_508.342789244;

    private readonly Dhm200Model _model;
    private readonly Downloader _downloader;
    private readonly string _cacheDir;
    private readonly string _waterCacheDir;

    public OverviewTileRenderer(Dhm200Model model, AppPaths paths, Downloader downloader)
    {
        _model = model;
        _downloader = downloader;
        _cacheDir = paths.OverviewTileCacheDir;
        _waterCacheDir = Path.Combine(paths.WaterCacheDir, "overview");
    }

    public const string RoadsLayer = "ch.swisstopo.swisstlm3d-strassen";

    /// <summary>Returns PNG bytes for a tile, using the disk cache when possible.</summary>
    public async Task<byte[]> GetTileAsync(int z, int x, int y, CancellationToken ct, bool roads = false)
    {
        if (z < MinZoom || z > MaxZoom) throw new ArgumentOutOfRangeException(nameof(z));
        int n = 1 << z;
        if (x < 0 || y < 0 || x >= n || y >= n) throw new ArgumentOutOfRangeException(nameof(x));

        string path = Path.Combine(_cacheDir, roads ? "roads" : "plain", z.ToString(), x.ToString(), y + ".png");
        if (File.Exists(path)) return await File.ReadAllBytesAsync(path, ct);

        var grid = await _model.GetAsync(null, ct);
        (bool[] Water, bool[] Forest)? cover = null;
        bool[]? roadMask = null;
        if (TileIntersectsModel(z, x, y, grid))
        {
            cover = await TryGetLandCoverAsync(z, x, y, ct);
            if (roads) roadMask = await TryGetRoadMaskAsync(z, x, y, ct);
        }
        byte[] png = await Task.Run(() => Render(z, x, y, cover?.Water, cover?.Forest, roadMask), ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + "." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(tmp, png, ct);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { /* cache is best-effort */ }
        return png;
    }

    private static bool TileIntersectsModel(int z, int x, int y, HeightGrid grid)
    {
        double lonW = WebMercator.TileXToLon(x, z), lonE = WebMercator.TileXToLon(x + 1, z);
        double latN = WebMercator.TileYToLat(y, z), latS = WebMercator.TileYToLat(y + 1, z);
        var extent = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        var (w, s, e, nn) = extent.ToWgs84Bbox();
        return !(lonE < w || lonW > e || latN < s || latS > nn);
    }

    /// <summary>Water and forest flags per tile pixel from the swisstopo land cover, or null when unavailable.</summary>
    private async Task<(bool[] Water, bool[] Forest)?> TryGetLandCoverAsync(int z, int x, int y, CancellationToken ct)
    {
        const int size = WebMercator.TileSize;
        double minX = WebMercator.TileXToLon(x, z) / 180.0 * MercatorHalf;
        double maxX = WebMercator.TileXToLon(x + 1, z) / 180.0 * MercatorHalf;
        double maxY = LatToMercatorY(WebMercator.TileYToLat(y, z));
        double minY = LatToMercatorY(WebMercator.TileYToLat(y + 1, z));
        string url = string.Create(CultureInfo.InvariantCulture,
            $"{WaterWmsBase}&LAYERS={Vec25LandCoverSource.Layer}&BBOX={minX:0.###},{minY:0.###},{maxX:0.###},{maxY:0.###}&WIDTH={size}&HEIGHT={size}");
        string file = Path.Combine(_waterCacheDir, z.ToString(), x.ToString(), y + ".png");
        try
        {
            await _downloader.GetFileAsync(url, file, null, ct);
            var img = PngReader.Decode(await File.ReadAllBytesAsync(file, ct));
            if (img.Width != size || img.Height != size) return null;
            var water = new bool[size * size];
            var forest = new bool[size * size];
            var px = img.Pixels;
            for (int i = 0; i < water.Length; i++)
            {
                int o = i * 4;
                if (px[o + 3] < 128) continue;
                if (Vec25LandCoverSource.IsWater(px[o], px[o + 1], px[o + 2])) water[i] = true;
                else if (Vec25LandCoverSource.IsForest(px[o], px[o + 1], px[o + 2])) forest[i] = true;
            }
            return (water, forest);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or NotSupportedException)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            return null; // draw the tile without water rather than failing the map
        }
    }

    /// <summary>Road pixels from the swisstopo TLM road layer (any drawn pixel counts), or null when unavailable.</summary>
    private async Task<bool[]?> TryGetRoadMaskAsync(int z, int x, int y, CancellationToken ct)
    {
        const int size = WebMercator.TileSize;
        double minX = WebMercator.TileXToLon(x, z) / 180.0 * MercatorHalf;
        double maxX = WebMercator.TileXToLon(x + 1, z) / 180.0 * MercatorHalf;
        double maxY = LatToMercatorY(WebMercator.TileYToLat(y, z));
        double minY = LatToMercatorY(WebMercator.TileYToLat(y + 1, z));
        string url = string.Create(CultureInfo.InvariantCulture,
            $"{WaterWmsBase}&LAYERS={RoadsLayer}&BBOX={minX:0.###},{minY:0.###},{maxX:0.###},{maxY:0.###}&WIDTH={size}&HEIGHT={size}");
        string file = Path.Combine(_waterCacheDir, "roads", z.ToString(), x.ToString(), y + ".png");
        try
        {
            await _downloader.GetFileAsync(url, file, null, ct);
            var img = PngReader.Decode(await File.ReadAllBytesAsync(file, ct));
            if (img.Width != size || img.Height != size) return null;
            var mask = new bool[size * size];
            var px = img.Pixels;
            for (int i = 0; i < mask.Length; i++) mask[i] = px[i * 4 + 3] >= 96;
            return mask;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or NotSupportedException)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
            return null;
        }
    }

    private static double LatToMercatorY(double lat)
    {
        double rad = lat * Math.PI / 180.0;
        return Math.Log(Math.Tan(Math.PI / 4 + rad / 2)) * MercatorHalf / Math.PI;
    }

    public byte[] Render(int z, int x, int y, bool[]? water = null, bool[]? forest = null, bool[]? roads = null)
    {
        const int size = WebMercator.TileSize;
        var grid = _model.Grid;
        var shade = _model.Shade;
        var slope = _model.Slope;
        var rgba = new byte[size * size * 4];

        if (!TileIntersectsModel(z, x, y, grid)) return PngWriter.Encode(size, size, rgba);

        for (int py = 0; py < size; py++)
        {
            double lat = WebMercator.TileYToLat(y + (py + 0.5) / size, z);
            for (int px = 0; px < size; px++)
            {
                double lon = WebMercator.TileXToLon(x + (px + 0.5) / size, z);
                var p = Lv95.FromWgs84(lat, lon);
                var (cx, cz) = grid.CellOf(p.E, p.N);
                if (cx < 0) continue; // transparent
                int i = cz * grid.Width + cx;
                float h = grid.Data[i];
                if (float.IsNaN(h)) continue;

                int o = (py * size + px) * 4;
                if (roads is not null && roads[py * size + px])
                {
                    rgba[o] = 70; rgba[o + 1] = 70; rgba[o + 2] = 70; rgba[o + 3] = 255; // asphalt
                    continue;
                }
                if (water is not null && water[py * size + px])
                {
                    rgba[o] = 63; rgba[o + 1] = 118; rgba[o + 2] = 228; rgba[o + 3] = 255; // Minecraft water
                    continue;
                }

                var (r, g, b) = Palette(h, slope[i]);
                if (forest is not null && forest[py * size + px] && h < TreePlanner.TreeLine && slope[i] < 32)
                {
                    (r, g, b) = (46, 106, 36); // oak leaves: darker, cooler green than grass
                }
                double light = 0.55 + 0.9 * (shade[i] / 255.0); // 0.55 .. 1.45, flat = 1.0
                rgba[o] = Clamp(r * light);
                rgba[o + 1] = Clamp(g * light);
                rgba[o + 2] = Clamp(b * light);
                rgba[o + 3] = 255;
            }
        }
        return PngWriter.Encode(size, size, rgba);
    }

    /// <summary>Minecraft-ish colours: grass by elevation band, stone on steep slopes, snow above the snow line.</summary>
    public static (int R, int G, int B) Palette(float elevation, int slopeDegrees)
    {
        if (elevation >= 2500) return (240, 244, 246);        // snow block
        if (slopeDegrees >= 32) return (125, 125, 125);       // stone
        if (elevation >= 2000) return (128, 132, 96);         // sparse alpine (stony grass)
        // grass: lush green in the lowlands, duller and browner with altitude
        double t = Math.Clamp((elevation - 200) / 1800.0, 0, 1);
        int r = (int)(89 + (140 - 89) * t);
        int g = (int)(161 + (150 - 161) * t);
        int b = (int)(57 + (80 - 57) * t);
        return (r, g, b);
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
