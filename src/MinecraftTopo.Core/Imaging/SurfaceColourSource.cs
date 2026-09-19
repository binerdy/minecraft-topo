using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Imaging;

/// <summary>
/// Colours the ground from swisstopo imagery or maps (©swisstopo): the swissIMAGE orthophoto
/// classified into natural ground types, or any of orthophoto, Siegfried map, Dufour map and
/// national map draped onto the terrain with the closest coloured blocks.
/// </summary>
public sealed class SurfaceColourSource
{
    private readonly WmsMask _wms;

    public SurfaceColourSource(Downloader downloader, AppPaths paths) => _wms = new WmsMask(downloader, paths.ImageryCacheDir);

    private static readonly (byte R, byte G, byte B)[] Palette = Blocks.ColourPalette.Select(c => (c.R, c.G, c.B)).ToArray();

    public static (string Layer, double MinPixel, string What, string? Extra) Layer(SurfaceStyle style) => style switch
    {
        SurfaceStyle.Photo or SurfaceStyle.PhotoBlocks => ("ch.swisstopo.swissimage", 1, "swissIMAGE orthophoto", null),
        SurfaceStyle.Siegfried => ("ch.swisstopo.hiks-siegfried", 2, "Siegfried map", null),
        SurfaceStyle.Dufour => ("ch.swisstopo.hiks-dufour", 4, "Dufour map", null),
        _ => ("ch.swisstopo.pixelkarte-farbe", 2, "national map", null),
    };

    /// <summary>Natural ground type from an orthophoto pixel: 0 = keep the land cover surface.</summary>
    public static byte ClassifyPhoto(byte r, byte g, byte b, byte a)
    {
        if (a < 128) return 0;
        (double hue, double sat, double val) = Hsv(r, g, b);
        if (val > 0.82 && sat < 0.12) return Blocks.SnowBlock;
        if (sat < 0.14) return val < 0.35 ? Blocks.Stone : val < 0.6 ? Blocks.Gravel : Blocks.Sand;
        if (hue is >= 62 and <= 170)
        {
            return val < 0.32 ? Blocks.MossBlock : Blocks.GrassBlock;
        }
        if (hue is >= 15 and < 62)
        {
            // browns and ochres: ploughed fields, stubble, bare soil, sand
            if (sat > 0.45 && val > 0.55) return hue >= 40 ? Blocks.Sand : Blocks.Farmland;
            return val < 0.4 ? Blocks.CoarseDirt : Blocks.Farmland;
        }
        return val < 0.35 ? Blocks.CoarseDirt : Blocks.Podzol;
    }

    /// <summary>Closest coloured block for a picture pixel.</summary>
    public static byte ClassifyColour(byte r, byte g, byte b, byte a)
    {
        if (a < 128) return 0;
        int i = WmsMask.Nearest(Palette, r, g, b, int.MaxValue / 4);
        return i < 0 ? (byte)0 : (byte)(Blocks.ColourBase + i);
    }

    /// <summary>Roof block from an orthophoto pixel: bricks for tiles, dark, grey or white roofs, moss for green roofs; 0 = undecided.</summary>
    public static byte ClassifyRoof(byte r, byte g, byte b, byte a)
    {
        if (a < 128) return 0;
        (double hue, double sat, double val) = Hsv(r, g, b);
        if (val < 0.28) return Blocks.DarkRoof;
        if (sat < 0.16) return val > 0.7 ? Blocks.WhiteRoof : val > 0.42 ? Blocks.LightRoof : Blocks.DarkRoof;
        if (hue is >= 70 and <= 170 && sat > 0.2) return Blocks.MossBlock;
        if ((hue <= 45 || hue >= 335) && sat >= 0.16) return Blocks.Bricks;
        return val > 0.5 ? Blocks.LightRoof : Blocks.DarkRoof;
    }

    /// <summary>Majority roof block per building from the orthophoto (0 where no building or no clear majority).</summary>
    public async Task<byte[]> GetRoofBlocksAsync(HeightGrid grid, int[] buildingId, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var (layer, minPixel, what, extra) = Layer(SurfaceStyle.Photo);
        var classes = await _wms.FetchAsync(layer, grid, minPixel, false, "imagery", "roof colours from the orthophoto", ClassifyRoof, progress, ct, extra);
        var votes = new Dictionary<int, Dictionary<byte, int>>();
        for (int i = 0; i < buildingId.Length; i++)
        {
            int id = buildingId[i];
            if (id == 0 || classes[i] == 0) continue;
            if (!votes.TryGetValue(id, out var v)) votes[id] = v = new Dictionary<byte, int>();
            v[classes[i]] = v.GetValueOrDefault(classes[i]) + 1;
        }
        var choice = new Dictionary<int, byte>();
        foreach (var (id, v) in votes)
        {
            int total = v.Values.Sum();
            var best = v.MaxBy(kv => kv.Value);
            if (best.Value * 100 >= total * 40) choice[id] = best.Key;
        }
        var result = new byte[buildingId.Length];
        for (int i = 0; i < result.Length; i++) if (buildingId[i] != 0 && choice.TryGetValue(buildingId[i], out var blk)) result[i] = blk;
        var summary = string.Join(", ", choice.Values.GroupBy(b => b).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {BlockPalette.Default.Names[g.Key].Replace("minecraft:", "")}"));
        progress?.Report(new ProgressInfo("imagery", 100, $"Roof colours from the orthophoto: {summary}"));
        return result;
    }

    /// <summary>Top-block override per cell (0 = none).</summary>
    public async Task<byte[]> GetAsync(SurfaceStyle style, HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var (layer, minPixel, what, extra) = Layer(style);
        var classify = style == SurfaceStyle.Photo ? (WmsMask.Classifier)ClassifyPhoto : ClassifyColour;
        var result = await _wms.FetchAsync(layer, grid, minPixel, false, "imagery", what, classify, progress, ct, extra);
        if (style == SurfaceStyle.Photo)
        {
            // Smooth speckle: majority of the 3x3 neighbourhood.
            result = Majority(result, grid.Width, grid.Height);
            var counts = new Dictionary<byte, int>();
            foreach (var v in result) counts[v] = counts.GetValueOrDefault(v) + 1;
            string summary = string.Join(", ", counts.Where(kv => kv.Key != 0).OrderByDescending(kv => kv.Value).Take(5)
                .Select(kv => $"{BlockPalette.Default.Names[kv.Key].Replace("minecraft:", "")} {100.0 * kv.Value / result.Length:0}%"));
            progress?.Report(new ProgressInfo("imagery", 100, "Ground from the orthophoto: " + summary));
        }
        else
        {
            progress?.Report(new ProgressInfo("imagery", 100, $"{what} draped over the ground"));
        }
        return result;
    }

    private static byte[] Majority(byte[] src, int w, int h)
    {
        var dst = new byte[src.Length];
        Span<int> votes = stackalloc int[256];
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                votes.Clear();
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        votes[src[nz * w + nx]]++;
                    }
                int best = src[z * w + x];
                for (int v = 1; v < 256; v++) if (votes[v] > votes[best]) best = v;
                dst[z * w + x] = (byte)best;
            }
        }
        return dst;
    }

    private static (double H, double S, double V) Hsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double d = max - min;
        double h = 0;
        if (d > 1e-6)
        {
            if (max == rf) h = 60 * (((gf - bf) / d) % 6);
            else if (max == gf) h = 60 * ((bf - rf) / d + 2);
            else h = 60 * ((rf - gf) / d + 4);
            if (h < 0) h += 360;
        }
        return (h, max < 1e-6 ? 0 : d / max, max);
    }
}
