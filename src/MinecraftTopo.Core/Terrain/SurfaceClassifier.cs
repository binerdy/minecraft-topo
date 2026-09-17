namespace MinecraftTopo.Core.Terrain;

public enum Surface : byte
{
    Grass = 0,
    Stone = 1,
    Snow = 2,
    Sand = 3,
    Water = 4,
}

/// <summary>Options that control how real elevations map to block columns.</summary>
public sealed record TerrainOptions
{
    /// <summary>Lowest terrain block Y (the lowest real elevation lands here).</summary>
    public int BaseY { get; init; } = 0;

    /// <summary>Blocks per metre of real height; null = auto-fit into the world height.</summary>
    public double? VerticalScale { get; init; }

    /// <summary>Real elevation (m) below which everything is flooded; null = no global flooding.</summary>
    public double? WaterLevel { get; init; }

    /// <summary>Place lakes and rivers from swisstopo water surfaces.</summary>
    public bool WaterBodies { get; init; } = true;

    /// <summary>Plant trees in swisstopo forest areas.</summary>
    public bool Trees { get; init; } = true;

    /// <summary>Scatter grass, ferns and flowers on grass blocks.</summary>
    public bool Vegetation { get; init; } = true;

    /// <summary>Ores inside the terrain, bee nests, berries, mushrooms, pumpkins, clay and seagrass.</summary>
    public bool Resources { get; init; } = true;

    /// <summary>Real elevation (m) above which the surface is snow.</summary>
    public double SnowLine { get; init; } = 2500;

    /// <summary>Slope in degrees at or above which the surface is bare stone.</summary>
    public double SlopeStoneDegrees { get; init; } = 32;

    public const int WorldMinY = -64;
    public const int WorldMaxY = 319;
}

/// <summary>Per-column recipe derived from the height grid.</summary>
public sealed class ClassifiedTerrain
{
    public const short NoWater = short.MinValue;

    public int Width { get; }
    public int Height { get; }
    /// <summary>Y of the top solid block per column.</summary>
    public short[] TopY { get; }
    public Surface[] Kind { get; }
    /// <summary>Y of the water surface (top water block) per column, or <see cref="NoWater"/>.</summary>
    public short[] WaterY { get; }
    public double VerticalScale { get; }
    public float MinElevation { get; }
    public float MaxElevation { get; }
    public int MinY { get; }
    public int MaxY { get; }
    public int WaterCells { get; }
    /// <summary>Forest flag per column (null when land cover was not used).</summary>
    public bool[]? Forest { get; internal set; }
    /// <summary>Planned trees (empty when trees are off or the scale is too coarse).</summary>
    public TreeMap Trees { get; internal set; } = new();
    /// <summary>True when forest columns should show a forest-floor block instead of individual trees.</summary>
    public bool CoarseForest { get; internal set; }
    /// <summary>Scatter ground cover (grass, ferns, flowers) on grass columns.</summary>
    public bool Vegetation { get; internal set; }
    /// <summary>Place ores and other resources.</summary>
    public bool Resources { get; internal set; }
    /// <summary>Seed used for deterministic tree and plant placement.</summary>
    public long Seed { get; internal set; }

    internal ClassifiedTerrain(int width, int height, short[] topY, Surface[] kind, short[] waterY,
        double verticalScale, float minElev, float maxElev, int minY, int maxY, int waterCells)
    {
        Width = width; Height = height; TopY = topY; Kind = kind; WaterY = waterY;
        VerticalScale = verticalScale; MinElevation = minElev; MaxElevation = maxElev; MinY = minY; MaxY = maxY;
        WaterCells = waterCells;
    }

    public bool IsForest(int x, int z) => Forest is not null && Forest[z * Width + x];

    public short TopAt(int x, int z) => TopY[z * Width + x];
    public Surface KindAt(int x, int z) => Kind[z * Width + x];
    public short WaterAt(int x, int z) => WaterY[z * Width + x];

    /// <summary>Highest block (solid or water) in a column.</summary>
    public int ColumnTop(int x, int z)
    {
        int i = z * Width + x;
        return WaterY[i] == NoWater ? TopY[i] : Math.Max(TopY[i], WaterY[i]);
    }
}

public static class SurfaceClassifier
{
    /// <summary>Computes the vertical scale that would be used for a grid with the given elevation range.</summary>
    public static double ResolveVerticalScale(TerrainOptions o, float minElev, float maxElev, double metresPerBlock)
    {
        double range = Math.Max(maxElev - minElev, 1e-3);
        double available = AvailableHeight(o);
        // "1 block per metre of height" at 1 m/block; at coarser horizontal scales keep true proportions by default
        double natural = 1.0 / metresPerBlock;
        double fit = available / range;
        double scale = o.VerticalScale ?? Math.Min(natural, fit);
        return Math.Min(scale, fit); // never exceed the world height
    }

    /// <summary>Block rows available for relief above the base Y (with a little headroom).</summary>
    public static double AvailableHeight(TerrainOptions o) => TerrainOptions.WorldMaxY - 4 - o.BaseY;

    /// <summary>
    /// Smallest whole metres-per-block at which the relief keeps true proportions (vertical scale
    /// equal to 1/metresPerBlock) without exceeding the world height.
    /// </summary>
    public static int TrueProportionMetresPerBlock(TerrainOptions o, float minElev, float maxElev)
    {
        double range = Math.Max(maxElev - minElev, 1e-3);
        return Math.Max(1, (int)Math.Ceiling(range / AvailableHeight(o) - 1e-9));
    }

    /// <param name="cover">Optional land cover (water, forest) aligned with the grid.</param>
    /// <param name="seed">Seed for deterministic tree placement.</param>
    public static ClassifiedTerrain Classify(HeightGrid grid, TerrainOptions o, double metresPerBlock, Water.LandCover? cover, long seed, CancellationToken ct)
    {
        var (minElev, maxElev) = grid.Range();
        if (float.IsNaN(minElev)) throw new InvalidOperationException("The height grid has no valid values.");
        double vs = ResolveVerticalScale(o, minElev, maxElev, metresPerBlock);

        int w = grid.Width, h = grid.Height;
        bool[]? waterMask = o.WaterBodies ? cover?.Water : null;
        bool[]? forestMask = o.Trees ? cover?.Forest : null;
        if (waterMask is not null && waterMask.Length != w * h) throw new ArgumentException("Land cover size does not match the grid.", nameof(cover));

        int? floodY = null;
        if (o.WaterLevel is { } wl && wl > minElev) floodY = ToY(wl);

        // Water surface per masked cell: local minimum of the DEM over masked neighbours, so that
        // shoreline mismatches between the water polygons and the DEM do not raise the water.
        float[]? waterSurface = waterMask is null ? null : LocalMinimum(grid, waterMask, radius: 3, ct);
        bool[]? shore = waterMask is null ? null : Dilate(waterMask, w, h, radius: 2, ct);
        int depthBlocks = Math.Max(1, (int)Math.Round(3 * vs));

        var topY = new short[w * h];
        var kind = new Surface[w * h];
        var waterY = new short[w * h];
        Array.Fill(waterY, ClassifiedTerrain.NoWater);
        int minY = int.MaxValue, maxY = int.MinValue, waterCells = 0;
        var lockObj = new object();

        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            int localMin = int.MaxValue, localMax = int.MinValue, localWater = 0;
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float elev = grid[x, z];
                int y = ToY(elev);
                Surface s;
                int wy = ClassifiedTerrain.NoWater;

                if (waterMask is not null && waterMask[i])
                {
                    // Lake or river: water surface at the DEM level, bed a few blocks below.
                    wy = ToY(waterSurface![i]);
                    y = Math.Max(TerrainOptions.WorldMinY + 1, wy - depthBlocks);
                    s = Surface.Water;
                }
                else if (floodY is { } fy && y < fy)
                {
                    wy = fy;
                    s = Surface.Water;
                }
                else if ((shore is not null && shore[i]) || (floodY is { } fy2 && y <= fy2 + Math.Max(1, (int)Math.Round(2 * vs))))
                {
                    s = Surface.Sand;
                }
                else if (elev >= o.SnowLine)
                {
                    s = Surface.Snow;
                }
                else if (grid.SlopeDegrees(x, z) >= o.SlopeStoneDegrees)
                {
                    s = Surface.Stone;
                }
                else
                {
                    s = Surface.Grass;
                }

                topY[i] = (short)y;
                kind[i] = s;
                waterY[i] = (short)wy;
                int colTop = wy == ClassifiedTerrain.NoWater ? y : Math.Max(y, wy);
                if (y < localMin) localMin = y;
                if (colTop > localMax) localMax = colTop;
                if (s == Surface.Water) localWater++;
            }
            lock (lockObj)
            {
                if (localMin < minY) minY = localMin;
                if (localMax > maxY) maxY = localMax;
                waterCells += localWater;
            }
        });

        var result = new ClassifiedTerrain(w, h, topY, kind, waterY, vs, minElev, maxElev, minY, maxY, waterCells)
        {
            Vegetation = o.Vegetation,
            Resources = o.Resources,
            Seed = seed,
        };
        if (forestMask is not null)
        {
            result.Forest = forestMask;
            result.CoarseForest = false; // trees are planted at every scale
            result.Trees = TreePlanner.Plan(grid, result, forestMask, metresPerBlock, seed);
        }
        return result;

        int ToY(double elev)
        {
            int y = o.BaseY + (int)Math.Round((elev - minElev) * vs);
            return Math.Clamp(y, TerrainOptions.WorldMinY + 1, TerrainOptions.WorldMaxY);
        }
    }

    /// <summary>Separable minimum filter over masked cells only (unmasked cells keep their own value).</summary>
    private static float[] LocalMinimum(HeightGrid grid, bool[] mask, int radius, CancellationToken ct)
    {
        int w = grid.Width, h = grid.Height;
        var tmp = new float[w * h];
        var result = new float[w * h];
        // horizontal pass
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float m = grid.Data[i];
                if (mask[i])
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        int j = z * w + xx;
                        if (mask[j] && grid.Data[j] < m) m = grid.Data[j];
                    }
                }
                tmp[i] = m;
            }
        });
        // vertical pass
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                float m = tmp[i];
                if (mask[i])
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int zz = z + dz;
                        if (zz < 0 || zz >= h) continue;
                        int j = zz * w + x;
                        if (mask[j] && tmp[j] < m) m = tmp[j];
                    }
                }
                result[i] = m;
            }
        });
        return result;
    }

    /// <summary>Cells within <paramref name="radius"/> (Chebyshev) of a masked cell, excluding masked cells themselves.</summary>
    private static bool[] Dilate(bool[] mask, int w, int h, int radius, CancellationToken ct)
    {
        var tmp = new bool[w * h];
        var result = new bool[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dx = -radius; dx <= radius && !any; dx++)
                {
                    int xx = x + dx;
                    if (xx >= 0 && xx < w && mask[z * w + xx]) any = true;
                }
                tmp[z * w + x] = any;
            }
        });
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dz = -radius; dz <= radius && !any; dz++)
                {
                    int zz = z + dz;
                    if (zz >= 0 && zz < h && tmp[zz * w + x]) any = true;
                }
                result[z * w + x] = any && !mask[z * w + x];
            }
        });
        return result;
    }
}
