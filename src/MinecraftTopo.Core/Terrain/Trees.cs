namespace MinecraftTopo.Core.Terrain;

public enum TreeType : byte
{
    Oak = 0,
    Spruce = 1,
}

/// <summary>A tree rooted on terrain column (X, Z); the first log sits one block above the column top.</summary>
public readonly record struct Tree(int X, int Z, TreeType Type, byte TrunkHeight);

/// <summary>Trees bucketed by chunk so a chunk builder can find every tree whose canopy may reach it.</summary>
public sealed class TreeMap
{
    /// <summary>Largest horizontal canopy reach in blocks; trees this far outside a chunk can still touch it.</summary>
    public const int MaxReach = 2;
    /// <summary>Largest height above the column top a tree can occupy.</summary>
    public const int MaxHeight = 12;

    private readonly Dictionary<long, List<Tree>> _byChunk = new();

    public int Count { get; private set; }

    private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    public void Add(Tree t)
    {
        long key = Key(t.X >> 4, t.Z >> 4);
        if (!_byChunk.TryGetValue(key, out var list)) _byChunk[key] = list = new List<Tree>(8);
        list.Add(t);
        Count++;
    }

    /// <summary>Trees in the chunk and its eight neighbours.</summary>
    public IEnumerable<Tree> Near(int cx, int cz)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (_byChunk.TryGetValue(Key(cx + dx, cz + dz), out var list))
                    foreach (var t in list) yield return t;
    }
}

/// <summary>Places trees deterministically inside forest cells.</summary>
public static class TreePlanner
{
    /// <summary>Above this elevation (m) no trees grow (Swiss tree line is about 2000-2200 m).</summary>
    public const double TreeLine = 2100;

    public static TreeMap Plan(HeightGrid grid, ClassifiedTerrain terrain, bool[] forest, double metresPerBlock, long seed)
    {
        var map = new TreeMap();

        // Roughly one tree per 7 x 7 m at fine scales; at coarse scales one tree every 3 blocks so
        // canopies still close into a forest. A jittered grid keeps them evenly spread without clumps.
        int spacing = Math.Clamp((int)Math.Round(7.0 / metresPerBlock), 3, 8);
        int w = grid.Width, h = grid.Height;
        for (int gz = 0; gz < h; gz += spacing)
        {
            for (int gx = 0; gx < w; gx += spacing)
            {
                uint hash = Hash(gx, gz, seed);
                int x = gx + (int)(hash % (uint)spacing);
                int z = gz + (int)((hash >> 8) % (uint)spacing);
                if (x >= w || z >= h) continue;
                int i = z * w + x;
                if (!forest[i] || terrain.Kind[i] != Surface.Grass) continue;
                float elev = grid.Data[i];
                if (elev >= TreeLine) continue;
                // Keep a 1-block margin to the grid edge so canopies stay inside the world.
                if (x < 2 || z < 2 || x >= w - 2 || z >= h - 2) continue;

                bool spruce = elev > 1300 || (hash >> 16) % 3 == 0;
                byte height = spruce
                    ? (byte)(6 + (hash >> 20) % 4)   // 6..9
                    : (byte)(4 + (hash >> 20) % 3);  // 4..6
                map.Add(new Tree(x, z, spruce ? TreeType.Spruce : TreeType.Oak, height));
            }
        }
        return map;
    }

    /// <summary>Calls <paramref name="place"/> for every block of the tree: (x, z, yAboveColumnTop, isLog).</summary>
    public static void Rasterize(Tree t, Action<int, int, int, bool> place)
    {
        int h = t.TrunkHeight;
        for (int y = 1; y <= h; y++) place(t.X, t.Z, y, true);

        if (t.Type == TreeType.Oak)
        {
            Layer(t, h - 2, 2, cutCorners: true, place);
            Layer(t, h - 1, 2, cutCorners: true, place);
            Layer(t, h, 1, cutCorners: false, place);
            Layer(t, h + 1, 1, cutCorners: true, place); // plus shape
        }
        else
        {
            place(t.X, t.Z, h + 1, false);
            Layer(t, h, 1, cutCorners: true, place);
            for (int y = h - 1, k = 0; y >= 2; y--, k++)
            {
                int radius = k % 2 == 0 ? 1 : 2;
                Layer(t, y, radius, cutCorners: radius == 2, place);
            }
        }
    }

    private static void Layer(Tree t, int y, int radius, bool cutCorners, Action<int, int, int, bool> place)
    {
        if (y < 1) return;
        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dz == 0 && y <= t.TrunkHeight) continue; // trunk
                if (cutCorners && Math.Abs(dx) == radius && Math.Abs(dz) == radius) continue;
                place(t.X + dx, t.Z + dz, y, false);
            }
        }
    }

    public static uint Hash(int x, int z, long seed)
    {
        unchecked
        {
            uint h = (uint)seed ^ 0x9E3779B9u;
            h ^= (uint)x * 0x85EBCA6Bu; h = (h << 13) | (h >> 19); h *= 0xC2B2AE35u;
            h ^= (uint)z * 0x27D4EB2Fu; h = (h << 17) | (h >> 15); h *= 0x165667B1u;
            h ^= h >> 15;
            return h;
        }
    }
}
