using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Imaging;

namespace MinecraftTopo.Core.Terrain;

/// <summary>Which layers a preview already knows about; null layers are simply not drawn.</summary>
public sealed class PreviewLayers
{
    public bool[]? Water { get; init; }
    public bool[]? Forest { get; init; }
    public byte[]? Road { get; init; }
    public byte[]? Rail { get; init; }
    public int[]? BuildingId { get; init; }
    public bool[]? Ice { get; init; }
    public byte[]? Geology { get; init; }
    public byte[]? Cover { get; init; }
    public byte[]? TopBlock { get; init; }
    public string[]? Names { get; init; }
}

/// <summary>A snapshot the generator offers while it works: a stage name and a renderer for any pixel size.</summary>
public sealed record MapSnapshotRequest(string Stage, int Width, int Height, Func<int, RgbaImage> Render);

/// <summary>
/// Map of the terrain before classification: hill-shaded relief coloured by elevation, with the
/// layers known so far drawn on top. Lets the app show the world being assembled layer by layer.
/// </summary>
public static class PreviewRenderer
{
    public static RgbaImage Render(HeightGrid grid, PreviewLayers layers, int cellsPerPixel, double snowLine = 2500)
    {
        int step = Math.Max(1, cellsPerPixel);
        int gw = grid.Width, gh = grid.Height;
        int w = (gw + step - 1) / step, h = (gh + step - 1) / step;
        var rgba = new byte[w * h * 4];
        var data = grid.Data;
        double cell = grid.CellSize;
        var names = layers.Names ?? BlockPalette.Default.Names;

        Parallel.For(0, h, pz =>
        {
            for (int px = 0; px < w; px++)
            {
                int x = Math.Min(gw - 1, px * step + step / 2), z = Math.Min(gh - 1, pz * step + step / 2);
                int i = z * gw + x;
                float e = data[i];
                (byte r, byte g, byte b) = float.IsNaN(e) ? ((byte)40, (byte)40, (byte)50) : Relief(e, snowLine);
                bool water = layers.Water is not null && layers.Water[i];
                if (layers.Geology is { } geo && geo[i] != 0 && !water) (r, g, b) = Blend(BlockColours.Of(names[Blocks.GeologyBlock(geo[i])]), (r, g, b), 0.55);
                if (layers.Cover is { } cov && cov[i] != 0 && !water)
                {
                    (r, g, b) = (Water.CoverClass)cov[i] switch
                    {
                        Water.CoverClass.Rock => ((byte)125, (byte)125, (byte)125),
                        Water.CoverClass.Scree => ((byte)136, (byte)126, (byte)126),
                        Water.CoverClass.Glacier => ((byte)235, (byte)240, (byte)245),
                        Water.CoverClass.Wetland => ((byte)80, (byte)90, (byte)60),
                        Water.CoverClass.Vineyard => ((byte)110, (byte)150, (byte)60),
                        Water.CoverClass.Orchard => ((byte)100, (byte)150, (byte)60),
                        _ => (r, g, b),
                    };
                }
                if (layers.Forest is not null && layers.Forest[i] && !water) (r, g, b) = (46, 106, 36);
                if (layers.TopBlock is { } top && top[i] != 0 && !water) (r, g, b) = BlockColours.Of(names[top[i]]);
                if (water) (r, g, b) = (63, 118, 228);
                if (layers.Ice is not null && layers.Ice[i]) (r, g, b) = (232, 240, 250);
                if (layers.Rail is not null && layers.Rail[i] != 0) (r, g, b) = (110, 90, 70);
                if (layers.Road is not null && layers.Road[i] != 0) (r, g, b) = (Water.RoadMaterial)layers.Road[i] == Water.RoadMaterial.DirtPath ? ((byte)150, (byte)125, (byte)80) : ((byte)70, (byte)72, (byte)76);
                if (layers.BuildingId is not null && layers.BuildingId[i] != 0) (r, g, b) = (150, 85, 70);

                // hill shade from the real heights (light from the north-west)
                int xw = Math.Max(0, x - step), xe = Math.Min(gw - 1, x + step), zn = Math.Max(0, z - step), zs = Math.Min(gh - 1, z + step);
                float dEast = data[z * gw + xe] - data[z * gw + xw], dSouth = data[zs * gw + x] - data[zn * gw + x];
                double shade = water ? 1.0 : Math.Clamp(1.0 - 0.035 * (dEast + dSouth) / (step * cell), 0.55, 1.35);
                int o = (pz * w + px) * 4;
                rgba[o] = (byte)Math.Clamp(r * shade, 0, 255);
                rgba[o + 1] = (byte)Math.Clamp(g * shade, 0, 255);
                rgba[o + 2] = (byte)Math.Clamp(b * shade, 0, 255);
                rgba[o + 3] = 255;
            }
        });
        return new RgbaImage(w, h, rgba);
    }

    /// <summary>Green lowlands, brown slopes, grey rock, white above the snow line.</summary>
    private static (byte, byte, byte) Relief(float e, double snowLine)
    {
        if (e >= snowLine) return (245, 248, 250);
        if (e >= snowLine - 500) return Blend((160, 160, 160), (245, 248, 250), (e - (snowLine - 500)) / 500.0);
        if (e >= 1700) return Blend((120, 140, 80), (160, 160, 160), (e - 1700) / (snowLine - 500 - 1700 + 1e-6));
        if (e >= 900) return Blend((91, 161, 57), (120, 140, 80), (e - 900) / 800.0);
        return (91, 161, 57);
    }

    private static (byte, byte, byte) Blend((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
