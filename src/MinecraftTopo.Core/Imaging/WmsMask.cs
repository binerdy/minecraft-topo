using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Imaging;

/// <summary>
/// Fetches a geo.admin.ch WMS layer (©swisstopo) over a block grid as PNG tiles in LV95 and turns
/// every pixel into one byte per grid cell through a classifier. The layer is sampled no finer
/// than <c>minPixelMetres</c>; tiles are cached on disk by request URL.
/// </summary>
public sealed class WmsMask
{
    private const string WmsBase = "https://wms.geo.admin.ch/?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&STYLES=&CRS=EPSG:2056&FORMAT=image/png";
    private const int MaxTilePixels = 4000;

    private readonly Downloader _downloader;
    private readonly string _cacheDir;

    public WmsMask(Downloader downloader, string cacheDir)
    {
        _downloader = downloader;
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    /// <summary>Classifier: (r, g, b, a) -> cell value; 0 is "nothing".</summary>
    public delegate byte Classifier(byte r, byte g, byte b, byte a);

    /// <summary>Classifies one WMS layer over the grid. Returns one byte per grid cell.</summary>
    public async Task<byte[]> FetchAsync(string layer, HeightGrid grid, double minPixelMetres, bool transparent, string phase, string what,
        Classifier classify, IProgress<ProgressInfo>? progress, CancellationToken ct, string? extraQuery = null)
    {
        var (classes, pw, step) = await FetchPixelsAsync(layer, grid, minPixelMetres, transparent, phase, what, classify, progress, ct, extraQuery);
        var result = new byte[grid.Width * grid.Height];
        for (int z = 0; z < grid.Height; z++)
        {
            int prow = (z / step) * pw, row = z * grid.Width;
            for (int x = 0; x < grid.Width; x++) result[row + x] = classes[prow + x / step];
        }
        return result;
    }

    /// <summary>Like <see cref="FetchAsync"/> but returns the coarser pixel grid itself (for filters before resampling).</summary>
    public async Task<(byte[] Classes, int PixelWidth, int Step)> FetchPixelsAsync(string layer, HeightGrid grid, double minPixelMetres, bool transparent,
        string phase, string what, Classifier classify, IProgress<ProgressInfo>? progress, CancellationToken ct, string? extraQuery = null)
    {
        int step = Math.Max(1, (int)Math.Round(minPixelMetres / grid.CellSize));
        double px = grid.CellSize * step;
        int pw = (grid.Width + step - 1) / step, ph = (grid.Height + step - 1) / step;
        int tilesX = (pw + MaxTilePixels - 1) / MaxTilePixels, tilesZ = (ph + MaxTilePixels - 1) / MaxTilePixels;
        int total = tilesX * tilesZ, done = 0;
        progress?.Report(new ProgressInfo(phase, 0, $"Fetching {what} ({total} map request{(total == 1 ? "" : "s")})", 0, total));

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
                    $"{WmsBase}&TRANSPARENT={(transparent ? "true" : "false")}&LAYERS={layer}&BBOX={minE:0.###},{minN:0.###},{maxE:0.###},{maxN:0.###}&WIDTH={w}&HEIGHT={h}{extraQuery}");
                string file = Path.Combine(_cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24] + ".png");
                await _downloader.GetFileAsync(url, file, null, ct);
                byte[] png = await File.ReadAllBytesAsync(file, ct);
                RgbaImage img;
                try { img = PngReader.Decode(png); }
                catch (InvalidDataException)
                {
                    File.Delete(file);
                    string text = Encoding.UTF8.GetString(png, 0, Math.Min(png.Length, 400));
                    throw new InvalidOperationException($"The map service returned an error for {what}: " + text.Replace('\n', ' '));
                }
                if (img.Width != w || img.Height != h) throw new InvalidOperationException($"Map tile for {what} has unexpected size {img.Width}x{img.Height}.");
                var p = img.Pixels;
                for (int y = 0; y < h; y++)
                {
                    int row = (z0 + y) * pw + x0, rowPx = y * w * 4;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowPx + x * 4;
                        classes[row + x] = classify(p[o], p[o + 1], p[o + 2], p[o + 3]);
                    }
                }
                done++;
                progress?.Report(new ProgressInfo(phase, 100.0 * done / total, $"Fetching {what}", done, total));
            }
        }
        return (classes, pw, step);
    }

    /// <summary>Index of the nearest colour in a table, or -1 when none is within <paramref name="maxDistance"/>.</summary>
    public static int Nearest((byte R, byte G, byte B)[] table, byte r, byte g, byte b, int maxDistance)
    {
        int best = -1, bestDist = int.MaxValue;
        for (int i = 0; i < table.Length; i++)
        {
            var (lr, lg, lb) = table[i];
            int d = (r - lr) * (r - lr) + (g - lg) * (g - lg) + (b - lb) * (b - lb);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best >= 0 && bestDist <= (long)maxDistance * maxDistance ? best : -1;
    }

    /// <summary>Gives zero cells the commonest non-zero value among their 8 neighbours (a few passes).</summary>
    public static void FillZeros(byte[] classes, int w, int h, int maxClass, int passes = 3)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var src = (byte[])classes.Clone();
            bool any = false;
            Span<int> votes = stackalloc int[maxClass + 1];
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
                    for (int c = 1; c < votes.Length; c++) if (votes[c] > 0 && (best == 0 || votes[c] > votes[best])) best = c;
                    if (best != 0) { classes[z * w + x] = (byte)best; any = true; }
                }
            }
            if (!any) break;
        }
    }
}
