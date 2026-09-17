using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Geology;

/// <summary>
/// Lithology main groups of the 1:500 000 geological map GK500 (©swisstopo), read from the
/// geo.admin.ch WMS. Each legend colour maps to a class 1..23; water (24) and glaciers (25)
/// and unknown colours give 0, which the chunk writer renders as plain stone.
/// </summary>
public sealed class Gk500Source
{
    public const string Layer = "ch.swisstopo.geologie-geotechnik-gk500-lithologie_hauptgruppen";
    private const string WmsBase = "https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&STYLES=&CRS=EPSG:2056&FORMAT=image/png&TRANSPARENT=true";
    private const int MaxTilePixels = 4000;
    /// <summary>The map is generalised far beyond 10 m, so never sample finer than that.</summary>
    private const double MinPixelMetres = 10;

    /// <summary>Legend colours in class order (class = index + 1).</summary>
    public static readonly (byte R, byte G, byte B)[] LegendColours =
    [
        (255, 255, 153), (204, 255, 204), (224, 224, 224), (102, 255, 102), (153, 204, 102), (153, 102, 51), (204, 153, 102),
        (102, 153, 204), (0, 255, 153), (0, 102, 51), (102, 204, 102), (255, 102, 0), (153, 51, 51), (255, 102, 153),
        (102, 153, 153), (0, 204, 204), (255, 153, 51), (255, 204, 102), (255, 102, 255), (255, 102, 102), (102, 51, 102),
        (204, 153, 204), (102, 0, 0), (204, 255, 255), (255, 255, 255),
    ];
    private const int WaterClass = 24, GlacierClass = 25;

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    public Gk500Source(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _cacheDir = paths.GeologyCacheDir;
    }

    /// <summary>Nearest legend colour; 0 when no colour is close (anti-aliased edges, labels).</summary>
    public static byte Classify(byte r, byte g, byte b)
    {
        int best = -1, bestDist = int.MaxValue;
        for (int i = 0; i < LegendColours.Length; i++)
        {
            var (lr, lg, lb) = LegendColours[i];
            int d = (r - lr) * (r - lr) + (g - lg) * (g - lg) + (b - lb) * (b - lb);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        if (best < 0 || bestDist > 40 * 40) return 0;
        int cls = best + 1;
        return cls is WaterClass or GlacierClass ? (byte)0 : (byte)cls;
    }

    /// <summary>Lithology class per grid cell.</summary>
    public async Task<byte[]> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var result = new byte[grid.Width * grid.Height];
        // Sample on a coarser pixel grid when blocks are finer than the map deserves.
        int step = Math.Max(1, (int)Math.Round(MinPixelMetres / grid.CellSize));
        double px = grid.CellSize * step;
        int pw = (grid.Width + step - 1) / step, ph = (grid.Height + step - 1) / step;
        int tilesX = (pw + MaxTilePixels - 1) / MaxTilePixels, tilesZ = (ph + MaxTilePixels - 1) / MaxTilePixels;
        int total = tilesX * tilesZ, done = 0;
        progress?.Report(new ProgressInfo("geology", 0, $"Fetching GK500 rock types ({total} map request{(total == 1 ? "" : "s")})", 0, total));

        var classes = new byte[pw * ph];
        for (int tz = 0; tz < tilesZ; tz++)
        {
            for (int tx = 0; tx < tilesX; tx++)
            {
                ct.ThrowIfCancellationRequested();
                int x0 = tx * MaxTilePixels, z0 = tz * MaxTilePixels;
                int w = Math.Min(MaxTilePixels, pw - x0), h = Math.Min(MaxTilePixels, ph - z0);
                double minE = grid.OriginE + x0 * px, maxE = minE + w * px;
                double maxN = grid.OriginN - z0 * px, minN = maxN - h * px;
                string url = string.Create(CultureInfo.InvariantCulture,
                    $"{WmsBase}&LAYERS={Layer}&BBOX={minE:0.###},{minN:0.###},{maxE:0.###},{maxN:0.###}&WIDTH={w}&HEIGHT={h}");
                string file = Path.Combine(_cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24] + ".png");
                await _downloader.GetFileAsync(url, file, null, ct);
                byte[] png = await File.ReadAllBytesAsync(file, ct);
                RgbaImage img;
                try { img = PngReader.Decode(png); }
                catch (InvalidDataException)
                {
                    File.Delete(file);
                    string text = Encoding.UTF8.GetString(png, 0, Math.Min(png.Length, 400));
                    throw new InvalidOperationException("The geology map service returned an error: " + text.Replace('\n', ' '));
                }
                if (img.Width != w || img.Height != h) throw new InvalidOperationException($"Geology tile has unexpected size {img.Width}x{img.Height}.");
                var p = img.Pixels;
                for (int y = 0; y < h; y++)
                {
                    int row = (z0 + y) * pw + x0, rowPx = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowPx + x * 4;
                        classes[row + x] = p[o + 3] < 128 ? (byte)0 : Classify(p[o], p[o + 1], p[o + 2]);
                    }
                }
                done++;
                progress?.Report(new ProgressInfo("geology", 100.0 * done / total, "Fetching GK500 rock types", done, total));
            }
        }

        // Edge pixels between two colours end up as class 0; give them the commonest neighbour class.
        FillUnknown(classes, pw, ph);

        var counts = new int[Anvil.Blocks.GeologyClasses + 1];
        for (int z = 0; z < grid.Height; z++)
        {
            int prow = (z / step) * pw;
            int row = z * grid.Width;
            for (int x = 0; x < grid.Width; x++)
            {
                byte c = classes[prow + x / step];
                result[row + x] = c;
                counts[c]++;
            }
        }
        var summary = string.Join(", ", counts.Select((n, c) => (n, c)).Where(t => t.c > 0 && t.n > 0).OrderByDescending(t => t.n).Take(4)
            .Select(t => $"{Anvil.BlockRoles.Find("geo" + t.c)!.Label} {100.0 * t.n / result.Length:0}%"));
        progress?.Report(new ProgressInfo("geology", 100, summary.Length > 0 ? "Rock types: " + summary : "No GK500 rock types in this area (plain stone)"));
        return result;
    }

    private static void FillUnknown(byte[] classes, int w, int h)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            var src = (byte[])classes.Clone();
            bool any = false;
            Span<int> votes = stackalloc int[Anvil.Blocks.GeologyClasses + 1];
            for (int z = 0; z < h; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (src[z * w + x] != 0) continue;
                    votes.Clear();
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                            votes[src[nz * w + nx]]++;
                        }
                    int best = 0;
                    for (int c = 1; c < votes.Length; c++) if (votes[c] > votes[best] || best == 0 && votes[c] > 0) best = c;
                    if (best != 0) { classes[z * w + x] = (byte)best; any = true; }
                }
            }
            if (!any) break;
        }
    }
}
