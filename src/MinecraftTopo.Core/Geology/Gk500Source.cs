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

    private readonly WmsMask _wms;

    public Gk500Source(Downloader downloader, AppPaths paths) => _wms = new WmsMask(downloader, paths.GeologyCacheDir);

    /// <summary>Nearest legend colour; 0 when no colour is close (anti-aliased edges, labels).</summary>
    public static byte Classify(byte r, byte g, byte b, byte a)
    {
        if (a < 128) return 0;
        int best = WmsMask.Nearest(LegendColours, r, g, b, 40);
        if (best < 0) return 0;
        int cls = best + 1;
        return cls is WaterClass or GlacierClass ? (byte)0 : (byte)cls;
    }

    /// <summary>Lithology class per grid cell.</summary>
    public async Task<byte[]> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var (classes, pw, step) = await _wms.FetchPixelsAsync(Layer, grid, MinPixelMetres, true, "geology", "GK500 rock types", Classify, progress, ct);
        int ph = classes.Length / pw;
        // Edge pixels between two colours end up as class 0; give them the commonest neighbour class.
        WmsMask.FillZeros(classes, pw, ph, Anvil.Blocks.GeologyClasses);

        var result = new byte[grid.Width * grid.Height];
        var counts = new int[Anvil.Blocks.GeologyClasses + 1];
        for (int z = 0; z < grid.Height; z++)
        {
            int prow = (z / step) * pw, row = z * grid.Width;
            for (int x = 0; x < grid.Width; x++)
            {
                byte c = classes[prow + x / step];
                result[row + x] = c;
                counts[c]++;
            }
        }
        progress?.Report(new ProgressInfo("geology", 100, Summary(counts, result.Length, "GK500")));
        return result;
    }

    internal static string Summary(int[] counts, int total, string source)
    {
        var top = counts.Select((n, c) => (n, c)).Where(t => t.c > 0 && t.n > 0).OrderByDescending(t => t.n).Take(4)
            .Select(t => $"{Anvil.BlockRoles.Find("geo" + t.c)!.Label} {100.0 * t.n / total:0}%");
        string summary = string.Join(", ", top);
        return summary.Length > 0 ? $"Rock types ({source}): {summary}" : $"No {source} rock types in this area (plain stone)";
    }
}
