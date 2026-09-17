using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Anvil;

/// <summary>Vanilla-like ore distribution. Ores only ever replace stone or deepslate, so the terrain shape is untouched.</summary>
public static class OrePlanner
{
    public readonly record struct OreConfig(byte Stone, byte Deepslate, int Attempts, int Size, int MinY, int MaxY);

    /// <summary>Per-chunk attempts and blob sizes, tuned to feel like vanilla at 1 m per block.</summary>
    public static readonly OreConfig[] Ores =
    [
        new(Blocks.CoalOre, Blocks.DeepslateCoalOre, 8, 12, 0, 190),
        new(Blocks.IronOre, Blocks.DeepslateIronOre, 6, 9, -24, 56),
        new(Blocks.IronOre, Blocks.DeepslateIronOre, 4, 9, 80, 319),     // mountain iron
        new(Blocks.CopperOre, Blocks.DeepslateCopperOre, 6, 10, 0, 112),
        new(Blocks.GoldOre, Blocks.DeepslateGoldOre, 2, 7, -64, 32),
        new(Blocks.RedstoneOre, Blocks.DeepslateRedstoneOre, 4, 7, -64, 15),
        new(Blocks.LapisOre, Blocks.DeepslateLapisOre, 2, 6, -64, 64),
        new(Blocks.DiamondOre, Blocks.DeepslateDiamondOre, 2, 5, -64, 16),
        new(Blocks.EmeraldOre, Blocks.DeepslateEmeraldOre, 6, 1, 80, 319), // mountains only
    ];

    /// <summary>
    /// Plans ore positions for one chunk, bucketed by section. Each entry packs
    /// (local y &lt;&lt; 12 | column index &lt;&lt; 4 | ore index); positions are only applied where the
    /// terrain block is stone or deepslate.
    /// </summary>
    public static List<int>?[] Plan(int cx, int cz, long seed, int minSectionY, int sectionCount)
    {
        var buckets = new List<int>?[sectionCount];
        for (int o = 0; o < Ores.Length; o++)
        {
            var ore = Ores[o];
            for (int a = 0; a < ore.Attempts; a++)
            {
                uint h = TreePlanner.Hash(cx * 31 + a, cz * 17 + o * 101, seed ^ 0x0BE5);
                int x = (int)(h & 15);
                int z = (int)((h >> 4) & 15);
                int y = ore.MinY + (int)((h >> 8) % (uint)(ore.MaxY - ore.MinY + 1));
                for (int k = 0; k < ore.Size; k++)
                {
                    uint h2 = TreePlanner.Hash(k + 7, a * 131 + o, h);
                    int bx = x + (int)(h2 % 3) - 1;
                    int bz = z + (int)((h2 >> 2) % 3) - 1;
                    int by = y + (int)((h2 >> 4) % 3) - 1;
                    if (ore.Size > 6 && (h2 >> 6) % 4 == 0) { bx += (int)((h2 >> 8) % 3) - 1; bz += (int)((h2 >> 10) % 3) - 1; }
                    if (bx < 0 || bz < 0 || bx > 15 || bz > 15) continue;
                    if (by <= TerrainOptions.WorldMinY || by > TerrainOptions.WorldMaxY) continue;
                    int section = (by - minSectionY * 16) >> 4;
                    if (section < 0 || section >= sectionCount) continue;
                    int ly = by & 15;
                    (buckets[section] ??= new List<int>(32)).Add((ly << 12) | ((bz * 16 + bx) << 4) | o);
                }
            }
        }
        return buckets;
    }
}
