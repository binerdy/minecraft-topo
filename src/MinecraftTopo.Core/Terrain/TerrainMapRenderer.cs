using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Imaging;

namespace MinecraftTopo.Core.Terrain;

/// <summary>
/// Top-down picture of a classified terrain (before or without region files): top block colours,
/// hill shading from the column heights, trees, roads, rails, buildings and water. Used for the
/// live map in the app and the world icon.
/// </summary>
public static class TerrainMapRenderer
{
    private static readonly (byte R, byte G, byte B) WaterColour = (63, 118, 228), Tree = (46, 106, 36), Spruce = (36, 78, 40),
        Road = (70, 72, 76), Path = (150, 125, 80), Rail = (110, 90, 70), House = (150, 85, 70), Industrial = (75, 78, 82), Church = (60, 60, 65),
        Wall = (110, 110, 110);

    /// <summary>Renders the terrain with <paramref name="cellsPerPixel"/> cells per pixel (1 = one pixel per block).</summary>
    public static RgbaImage Render(ClassifiedTerrain t, int cellsPerPixel)
    {
        int step = Math.Max(1, cellsPerPixel);
        int w = (t.Width + step - 1) / step, h = (t.Height + step - 1) / step;
        var rgba = new byte[w * h * 4];
        var names = t.Palette.Names;

        // trees as dark dots (trunk cell and its 4 neighbours at fine scales)
        bool[]? treeMask = null;
        if (t.Trees.Count > 0)
        {
            treeMask = new bool[t.Width * t.Height];
            for (int cz = 0; cz < (t.Height + 15) / 16; cz++)
                for (int cx = 0; cx < (t.Width + 15) / 16; cx++)
                    foreach (var tree in t.Trees.Near(cx, cz))
                    {
                        if ((tree.X >> 4) != cx || (tree.Z >> 4) != cz) continue;
                        int r = step == 1 ? 1 : 0;
                        for (int dz = -r; dz <= r; dz++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int x = tree.X + dx, z = tree.Z + dz;
                                if (x >= 0 && z >= 0 && x < t.Width && z < t.Height) treeMask[z * t.Width + x] = true;
                            }
                    }
        }

        Parallel.For(0, h, pz =>
        {
            for (int px = 0; px < w; px++)
            {
                int x = Math.Min(t.Width - 1, px * step + step / 2), z = Math.Min(t.Height - 1, pz * step + step / 2);
                int i = z * t.Width + x;
                var (r, g, b) = Colour(t, i, names, treeMask);
                // hill shade: light from the north-west
                int xw = Math.Max(0, x - step), xe = Math.Min(t.Width - 1, x + step), zn = Math.Max(0, z - step), zs = Math.Min(t.Height - 1, z + step);
                int dEast = t.TopY[z * t.Width + xe] - t.TopY[z * t.Width + xw];
                int dSouth = t.TopY[zs * t.Width + x] - t.TopY[zn * t.Width + x];
                double shade = t.WaterY[i] != ClassifiedTerrain.NoWater ? 1.0 : Math.Clamp(1.0 - 0.035 * (dEast + dSouth) / step, 0.55, 1.35);
                int o = (pz * w + px) * 4;
                rgba[o] = (byte)Math.Clamp(r * shade, 0, 255);
                rgba[o + 1] = (byte)Math.Clamp(g * shade, 0, 255);
                rgba[o + 2] = (byte)Math.Clamp(b * shade, 0, 255);
                rgba[o + 3] = 255;
            }
        });
        return new RgbaImage(w, h, rgba);
    }

    /// <summary>PNG no wider or taller than <paramref name="maxPixels"/>; returns the cells per pixel used.</summary>
    public static byte[] RenderPng(ClassifiedTerrain t, int maxPixels, out int cellsPerPixel)
    {
        cellsPerPixel = Math.Max(1, (int)Math.Ceiling(Math.Max(t.Width, t.Height) / (double)maxPixels));
        var img = Render(t, cellsPerPixel);
        return PngWriter.Encode(img.Width, img.Height, img.Pixels);
    }

    /// <summary>Square icon (Minecraft shows icon.png at 64x64) from the centre of the area.</summary>
    public static byte[] RenderIcon(ClassifiedTerrain t, int size = 64)
    {
        int side = Math.Min(t.Width, t.Height);
        int step = Math.Max(1, side / size);
        var img = Render(t, step);
        int x0 = Math.Max(0, (img.Width - size) / 2), z0 = Math.Max(0, (img.Height - size) / 2);
        int w = Math.Min(size, img.Width), h = Math.Min(size, img.Height);
        var rgba = new byte[w * h * 4];
        for (int z = 0; z < h; z++) Array.Copy(img.Pixels, ((z0 + z) * img.Width + x0) * 4, rgba, z * w * 4, w * 4);
        return PngWriter.Encode(w, h, rgba);
    }

    private static (byte, byte, byte) Colour(ClassifiedTerrain t, int i, string[] names, bool[]? treeMask)
    {
        if (t.BuildingHeight is not null && t.BuildingHeight[i] != 0)
        {
            byte flags = t.BuildingFlags![i];
            if (t.RoofBlock is not null && t.RoofBlock[i] != 0) return BlockColours.Of(names[t.RoofBlock[i]]);
            return (flags & Water.BuildingFlag.Church) != 0 ? Church : (flags & Water.BuildingFlag.Industrial) != 0 ? Industrial : House;
        }
        if (t.Rail is not null && t.Rail[i] != 0) return Rail;
        if (t.Road is not null && t.Road[i] != 0) return (Water.RoadMaterial)t.Road[i] == Water.RoadMaterial.DirtPath ? Path : Road;
        if (t.WaterY[i] != ClassifiedTerrain.NoWater) return WaterColour;
        if (treeMask is not null && treeMask[i]) return t.Elevation is { } el && el[i] > 1300 ? Spruce : Tree;
        if (t.Wall is not null && t.Wall[i] != 0) return Wall;
        if (t.TopBlock is not null && t.TopBlock[i] != 0) return BlockColours.Of(names[t.TopBlock[i]]);
        return t.Kind[i] switch
        {
            Surface.Grass or Surface.Vineyard => BlockColours.Of("minecraft:grass_block"),
            Surface.Stone => BlockColours.Of(names[Blocks.GeologyBlock(t.Geology?[i] ?? 0)]),
            Surface.Snow or Surface.Glacier => BlockColours.Of("minecraft:snow_block"),
            Surface.Sand => BlockColours.Of("minecraft:sand"),
            Surface.Gravel => BlockColours.Of("minecraft:gravel"),
            Surface.Mud => BlockColours.Of("minecraft:mud"),
            _ => BlockColours.Of("minecraft:stone"),
        };
    }
}
