using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Water;

/// <summary>
/// Land cover from swisstopo's VECTOR25 primary surfaces (©swisstopo) rendered by the
/// geo.admin.ch WMS in LV95. Lakes, rivers and forests are drawn as filled polygons in flat class
/// colours, so the masks are simply "pixels of those colours".
/// </summary>
public sealed class Vec25LandCoverSource : ILandCoverSource
{
    public const string Layer = "ch.swisstopo.vec25-primaerflaechen";
    private const string WmsBase = "https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&STYLES=&CRS=EPSG:2056&FORMAT=image/png&TRANSPARENT=true";
    private const int MaxTilePixels = 4000;

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    public Vec25LandCoverSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _cacheDir = paths.WaterCacheDir;
    }

    public string Name => "swisstopo VECTOR25 primary surfaces";
    public bool SupportsInfrastructure => false;

    /// <summary>
    /// VECTOR25 draws rivers in (82,209,255) and lakes in (187,252,255); every other class is
    /// beige, pink, green, grey or olive. A cyan test catches both water colours plus most
    /// anti-aliased edge pixels while rejecting all land classes.
    /// </summary>
    public static bool IsWater(byte r, byte g, byte b) => b >= 200 && g >= 180 && r <= 200 && b - r >= 50;

    /// <summary>Forest is light green (208,255,208); open forest / bushes use dotted greens such as (125,251,125).</summary>
    public static bool IsForest(byte r, byte g, byte b) => g >= 200 && g - r >= 40 && g - b >= 40 && r <= 215 && b <= 215;

    public async Task<LandCover> GetAsync(HeightGrid grid, LandCoverOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var water = new bool[grid.Width * grid.Height];
        var forest = new bool[grid.Width * grid.Height];
        int tilesX = (grid.Width + MaxTilePixels - 1) / MaxTilePixels;
        int tilesZ = (grid.Height + MaxTilePixels - 1) / MaxTilePixels;
        int total = tilesX * tilesZ, done = 0;
        progress?.Report(new ProgressInfo("landcover", 0, $"Fetching water and forest surfaces ({total} map request{(total == 1 ? "" : "s")})", 0, total));

        for (int tz = 0; tz < tilesZ; tz++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                ct.ThrowIfCancellationRequested();
                int x0 = tx * MaxTilePixels, z0 = tz * MaxTilePixels;
                int w = Math.Min(MaxTilePixels, grid.Width - x0);
                int h = Math.Min(MaxTilePixels, grid.Height - z0);
                double minE = grid.OriginE + x0 * grid.CellSize;
                double maxE = minE + w * grid.CellSize;
                double maxN = grid.OriginN - z0 * grid.CellSize;
                double minN = maxN - h * grid.CellSize;

                string url = string.Create(CultureInfo.InvariantCulture,
                    $"{WmsBase}&LAYERS={Layer}&BBOX={minE:0.###},{minN:0.###},{maxE:0.###},{maxN:0.###}&WIDTH={w}&HEIGHT={h}");
                string file = Path.Combine(_cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24] + ".png");
                await _downloader.GetFileAsync(url, file, null, ct);

                byte[] png = await File.ReadAllBytesAsync(file, ct);
                RgbaImage img;
                try
                {
                    img = PngReader.Decode(png);
                }
                catch (InvalidDataException)
                {
                    // Probably an XML service exception instead of an image; drop the bad cache entry.
                    File.Delete(file);
                    string text = Encoding.UTF8.GetString(png, 0, Math.Min(png.Length, 400));
                    throw new InvalidOperationException("The land cover map service returned an error: " + text.Replace('\n', ' '));
                }
                if (img.Width != w || img.Height != h) throw new InvalidOperationException($"Land cover tile has unexpected size {img.Width}x{img.Height}.");

                var px = img.Pixels;
                for (int y = 0; y < h; y++)
                {
                    int rowMask = (z0 + y) * grid.Width + x0;
                    int rowPx = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowPx + x * 4;
                        if (px[o + 3] < 128) continue;
                        if (IsWater(px[o], px[o + 1], px[o + 2])) water[rowMask + x] = true;
                        else if (IsForest(px[o], px[o + 1], px[o + 2])) forest[rowMask + x] = true;
                    }
                }
                done++;
                progress?.Report(new ProgressInfo("landcover", 100.0 * done / total, "Fetching water and forest surfaces", done, total));
            }
        }

        var cover = new LandCover { Water = water, Forest = forest };
        progress?.Report(new ProgressInfo("landcover", 100, $"Water covers {cover.WaterCount:N0} and forest {cover.ForestCount:N0} of {water.Length:N0} cells"));
        return cover;
    }
}
