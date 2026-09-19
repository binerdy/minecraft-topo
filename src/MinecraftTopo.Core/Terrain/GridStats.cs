namespace MinecraftTopo.Core.Terrain;

/// <summary>Small grid helpers shared by the planners.</summary>
public static class GridStats
{
    /// <summary>Fraction of non-zero cells in the (2r+1)² window around every cell, via an integral image.</summary>
    public static float[] BoxFraction(byte[] values, int w, int h, int r, CancellationToken ct)
    {
        var integral = new int[(w + 1) * (h + 1)];
        for (int z = 0; z < h; z++)
        {
            int rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                if (values[z * w + x] != 0) rowSum++;
                integral[(z + 1) * (w + 1) + x + 1] = integral[z * (w + 1) + x + 1] + rowSum;
            }
        }
        var result = new float[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            int z0 = Math.Max(0, z - r), z1 = Math.Min(h - 1, z + r);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - r), x1 = Math.Min(w - 1, x + r);
                int sum = integral[(z1 + 1) * (w + 1) + x1 + 1] - integral[z0 * (w + 1) + x1 + 1] - integral[(z1 + 1) * (w + 1) + x0] + integral[z0 * (w + 1) + x0];
                result[z * w + x] = (float)sum / ((z1 - z0 + 1) * (x1 - x0 + 1));
            }
        });
        return result;
    }

    /// <summary>Cells within <paramref name="radius"/> (Chebyshev) of a non-zero cell (separable dilation).</summary>
    public static bool[] Near(byte[] values, int w, int h, int radius, CancellationToken ct)
    {
        var tmp = new bool[w * h];
        var result = new bool[w * h];
        Parallel.For(0, h, new ParallelOptions { CancellationToken = ct }, z =>
        {
            int row = z * w;
            int run = -1;
            for (int x = 0; x < w; x++)
            {
                if (values[row + x] != 0) run = x;
                if (run >= 0 && x - run <= radius) tmp[row + x] = true;
            }
            run = -1;
            for (int x = w - 1; x >= 0; x--)
            {
                if (values[row + x] != 0) run = x;
                if (run >= 0 && run - x <= radius) tmp[row + x] = true;
            }
        });
        Parallel.For(0, w, new ParallelOptions { CancellationToken = ct }, x =>
        {
            int run = -1;
            for (int z = 0; z < h; z++)
            {
                if (tmp[z * w + x]) run = z;
                if (run >= 0 && z - run <= radius) result[z * w + x] = true;
            }
            run = -1;
            for (int z = h - 1; z >= 0; z--)
            {
                if (tmp[z * w + x]) run = z;
                if (run >= 0 && run - z <= radius) result[z * w + x] = true;
            }
        });
        return result;
    }
}

/// <summary>Finds a walkable cell for the world spawn.</summary>
public static class SpawnPlanner
{
    /// <summary>True when a player can stand there: land, no water or ice, no road, rail, building or wall.</summary>
    public static bool IsWalkable(ClassifiedTerrain t, int i)
    {
        var k = t.Kind[i];
        if (k is Surface.Water or Surface.Glacier || t.WaterY[i] != ClassifiedTerrain.NoWater) return false;
        if (t.IsOccupied(i)) return false;
        if (t.Wall is not null && t.Wall[i] != 0) return false;
        if (t.TopBlock is not null && t.TopBlock[i] == Anvil.Blocks.Water) return false;
        return true;
    }

    /// <summary>The cell itself when walkable, else the nearest walkable cell within the radius (cells), else the cell itself.</summary>
    public static (int X, int Z) Snap(ClassifiedTerrain t, int x, int z, int radius)
    {
        x = Math.Clamp(x, 0, t.Width - 1);
        z = Math.Clamp(z, 0, t.Height - 1);
        if (IsWalkable(t, z * t.Width + x)) return (x, z);
        for (int r = 1; r <= radius; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                int step = Math.Abs(dz) == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += step)
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= t.Width || cz >= t.Height) continue;
                    if (IsWalkable(t, cz * t.Width + cx)) return (cx, cz);
                }
            }
        }
        return (x, z);
    }

    /// <summary>Packed walkability, one byte per cell (for a server that keeps it after the terrain is gone).</summary>
    public static byte[] WalkableMask(ClassifiedTerrain t)
    {
        var m = new byte[t.Width * t.Height];
        for (int i = 0; i < m.Length; i++) m[i] = IsWalkable(t, i) ? (byte)1 : (byte)0;
        return m;
    }

    /// <summary>Same search as <see cref="Snap"/> over a packed mask.</summary>
    public static (int X, int Z) Snap(byte[] walkable, int w, int h, int x, int z, int radius)
    {
        x = Math.Clamp(x, 0, w - 1);
        z = Math.Clamp(z, 0, h - 1);
        if (walkable[z * w + x] != 0) return (x, z);
        for (int r = 1; r <= radius; r++)
            for (int dz = -r; dz <= r; dz++)
            {
                int step = Math.Abs(dz) == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += step)
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx >= 0 && cz >= 0 && cx < w && cz < h && walkable[cz * w + cx] != 0) return (cx, cz);
                }
            }
        return (x, z);
    }
}
