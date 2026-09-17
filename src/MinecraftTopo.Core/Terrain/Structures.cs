using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Terrain;

/// <summary>Items bucketed by every chunk their bounding box touches, for fast per-chunk lookup.</summary>
public sealed class ChunkIndex<T>
{
    private readonly Dictionary<long, List<T>> _byChunk = new();

    public int Count { get; private set; }

    private static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

    public void Add(T item, int minX, int minZ, int maxX, int maxZ)
    {
        Count++;
        for (int cz = minZ >> 4; cz <= (maxZ >> 4); cz++)
            for (int cx = minX >> 4; cx <= (maxX >> 4); cx++)
            {
                long key = Key(cx, cz);
                if (!_byChunk.TryGetValue(key, out var list)) _byChunk[key] = list = new List<T>(4);
                list.Add(item);
            }
    }

    public IReadOnlyList<T> Get(int cx, int cz) => _byChunk.TryGetValue(Key(cx, cz), out var list) ? list : [];
}

/// <summary>Block shapes for pylons, poles, wires and wind turbines, all scaled by metres per block.</summary>
public static class StructurePlanner
{
    public delegate void Place(int x, int z, int yAboveGround, byte block, bool absoluteY);

    /// <summary>Wire height above a pylon's ground level, in blocks.</summary>
    public static int TowerHeight(double mpb) => Math.Clamp((int)Math.Round(35 / mpb), 4, 60);
    public static int PoleHeight(double mpb) => Math.Clamp((int)Math.Round(9 / mpb), 3, 12);
    public static int HubHeight(double mpb) => Math.Clamp((int)Math.Round(90 / mpb), 6, 150);
    public static int BladeLength(double mpb) => Math.Clamp((int)Math.Round(45 / mpb), 3, 70);

    /// <summary>Horizontal reach of a structure in blocks (for indexing).</summary>
    public static int Reach(StructureKind kind, double mpb) => kind == StructureKind.WindTurbine ? BladeLength(mpb) + 2 : 3;

    /// <summary>Church tower height in blocks: twice the nave, at least 12 m.</summary>
    public static int ChurchTowerHeight(int naveHeight, double mpb) => Math.Max(naveHeight * 2, (int)Math.Round(12 / mpb));

    /// <summary>Pylon or pole height limited by the room left below the world ceiling.</summary>
    public static int LineHeight(bool highVoltage, double mpb, int headroom) =>
        Math.Max(2, Math.Min(highVoltage ? TowerHeight(mpb) : PoleHeight(mpb), headroom - 1));

    /// <summary>
    /// Emits the blocks of a structure relative to its own ground level (y = 1 is the first block
    /// above ground). <paramref name="headroom"/> is the number of blocks between the ground and the
    /// world ceiling; structures that would not fit are scaled down.
    /// </summary>
    public static void Rasterize(Structure s, double mpb, int headroom, Action<int, int, int, byte> place)
    {
        switch (s.Kind)
        {
            case StructureKind.Tower:
            {
                int hgt = LineHeight(true, mpb, headroom);
                for (int y = 1; y <= hgt; y++) place(s.X, s.Z, y, StructureBlocks.IronBars);
                // cross arm perpendicular to the line
                bool alongX = Math.Abs(s.DirX) >= Math.Abs(s.DirZ);
                for (int d = -2; d <= 2; d++)
                {
                    if (alongX) place(s.X, s.Z + d, hgt, StructureBlocks.IronBars);
                    else place(s.X + d, s.Z, hgt, StructureBlocks.IronBars);
                }
                break;
            }
            case StructureKind.ChurchSpire:
            {
                // slate pyramid on the tower top, then a cross
                int half = s.Size / 2;
                if (headroom < 2) break;
                for (int dz = -half; dz <= half; dz++)
                    for (int dx = -half; dx <= half; dx++) place(s.X + dx, s.Z + dz, 1, StructureBlocks.DeepslateTiles);
                if (half > 0) place(s.X, s.Z, 2, StructureBlocks.DeepslateTiles);
                int crossBase = half > 0 ? 3 : 2;
                if (crossBase + 2 > headroom) break;
                place(s.X, s.Z, crossBase, StructureBlocks.IronBars);
                place(s.X, s.Z, crossBase + 1, StructureBlocks.IronBars);
                place(s.X - 1, s.Z, crossBase + 1, StructureBlocks.IronBars);
                place(s.X + 1, s.Z, crossBase + 1, StructureBlocks.IronBars);
                place(s.X, s.Z, crossBase + 2, StructureBlocks.IronBars);
                break;
            }
            case StructureKind.Pole:
            {
                int hgt = LineHeight(false, mpb, headroom);
                for (int y = 1; y <= hgt; y++) place(s.X, s.Z, y, StructureBlocks.OakFence);
                break;
            }
            case StructureKind.WindTurbine:
            {
                int hub = HubHeight(mpb), blade = BladeLength(mpb);
                int total = hub + 2 + blade;
                if (total > headroom)
                {
                    double f = Math.Max(0.1, (double)Math.Max(headroom, 6) / total);
                    hub = Math.Max(4, (int)(hub * f));
                    blade = Math.Max(2, (int)(blade * f));
                }
                for (int y = 1; y <= hub; y++) place(s.X, s.Z, y, StructureBlocks.SmoothQuartz);
                // nacelle along x, rotor plane in front of it (x + 2), spanning z and y
                for (int d = -1; d <= 1; d++) place(s.X + d, s.Z, hub + 1, StructureBlocks.LightGrayConcrete);
                place(s.X + 2, s.Z, hub + 1, StructureBlocks.LightGrayConcrete);
                foreach (double angle in new[] { 90.0, 210.0, 330.0 })
                {
                    double a = angle * Math.PI / 180;
                    for (double r = 1; r <= blade; r += 0.5)
                    {
                        int z = s.Z + (int)Math.Round(Math.Cos(a) * r);
                        int y = hub + 1 + (int)Math.Round(Math.Sin(a) * r);
                        if (y >= 1) place(s.X + 2, z, y, StructureBlocks.SmoothQuartz);
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// Emits chain blocks along a wire with an absolute Y interpolated between the two end heights.
    /// High-voltage lines get two wires offset 2 blocks to either side of the pylon axis.
    /// </summary>
    public static void RasterizeWire(Wire wire, int y0, int y1, Action<int, int, int, byte> placeAbsolute)
    {
        double dx = wire.X1 - wire.X0, dz = wire.Z1 - wire.Z0;
        double len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.5) return;
        byte block = Math.Abs(dx) >= Math.Abs(dz) ? StructureBlocks.ChainX : StructureBlocks.ChainZ;
        double px = -dz / len, pz = dx / len; // perpendicular unit vector
        int[] offsets = wire.HighVoltage ? [-2, 2] : [0];
        int steps = Math.Max(1, (int)Math.Ceiling(len * 2));
        foreach (int off in offsets)
        {
            int lastX = int.MinValue, lastZ = int.MinValue, lastY = int.MinValue;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                int x = (int)Math.Floor(wire.X0 + dx * t + px * off);
                int z = (int)Math.Floor(wire.Z0 + dz * t + pz * off);
                int y = (int)Math.Round(y0 + (y1 - y0) * t);
                if (x == lastX && z == lastZ && y == lastY) continue;
                placeAbsolute(x, z, y, block);
                lastX = x; lastZ = z; lastY = y;
            }
        }
    }
}

/// <summary>Block ids for structures, defined next to the other ids in <c>Anvil.Blocks</c>.</summary>
public static class StructureBlocks
{
    public const byte IronBars = 68;
    public const byte OakFence = 69;
    public const byte ChainX = 70;
    public const byte ChainZ = 71;
    public const byte SmoothQuartz = 72;
    public const byte DeepslateTiles = 73;
    public const byte LightGrayConcrete = 59; // same as Anvil.Blocks.LightGrayConcrete
    public const byte StoneBricks = 61;       // same as Anvil.Blocks.StoneBricks
}

/// <summary>Where villagers stand: on the ground next to a house.</summary>
public readonly record struct VillagerSpawn(int X, int Y, int Z, float Yaw);

public static class VillagerPlanner
{
    public const int MaxVillagers = 4000;

    /// <summary>One villager next to roughly 60 % of non-industrial buildings with at least 20 footprint cells.</summary>
    public static List<VillagerSpawn> Plan(ClassifiedTerrain t, LandCover cover, long seed)
    {
        var result = new List<VillagerSpawn>();
        var ids = cover.BuildingId;
        if (ids is null || t.BuildingHeight is null) return result;
        int w = t.Width, h = t.Height;

        var cells = new Dictionary<int, int>();          // building id -> footprint size
        var candidate = new Dictionary<int, int>();      // building id -> chosen ground cell index
        for (int z = 1; z < h - 1; z++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = z * w + x;
                int id = ids[i];
                if (id == 0) continue;
                cells[id] = cells.GetValueOrDefault(id) + 1;
                if (candidate.ContainsKey(id) || (cover.BuildingFlags![i] & BuildingFlag.Industrial) != 0) continue;
                foreach (int n in new[] { i - 1, i + 1, i - w, i + w })
                {
                    if (ids[n] != 0 || t.WaterY[n] != ClassifiedTerrain.NoWater || t.Kind[n] == Surface.Water) continue;
                    if (t.Rail is not null && t.Rail[n] != 0) continue;
                    candidate[id] = n;
                    break;
                }
            }
        }

        foreach (var (id, cell) in candidate)
        {
            if (cells[id] < 20) continue;
            uint hash = TreePlanner.Hash(id, 99, seed ^ 0x71114);
            if (hash % 100 >= 60) continue;
            int x = cell % w, z = cell / w;
            int y = t.ColumnTop(x, z) + 1;
            result.Add(new VillagerSpawn(x, y, z, (hash >> 8) % 360));
            if (result.Count >= MaxVillagers) break;
        }
        return result;
    }
}
