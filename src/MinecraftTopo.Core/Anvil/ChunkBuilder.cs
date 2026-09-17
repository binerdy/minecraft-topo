using System.IO.Compression;
using System.Numerics;
using MinecraftTopo.Core.Nbt;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Anvil;

/// <summary>Block ids used by the generator; the index is what goes into per-section palettes.</summary>
public static class Blocks
{
    public const byte Air = 0;
    public const byte Bedrock = 1;
    public const byte Stone = 2;
    public const byte Deepslate = 3;
    public const byte Dirt = 4;
    public const byte GrassBlock = 5;
    public const byte Sand = 6;
    public const byte SnowBlock = 7;
    public const byte Water = 8;
    public const byte Gravel = 9;
    public const byte OakLog = 10;
    public const byte OakLeaves = 11;
    public const byte SpruceLog = 12;
    public const byte SpruceLeaves = 13;
    public const byte MossBlock = 14;
    public const byte ShortGrass = 15;
    public const byte TallGrassLower = 16;
    public const byte TallGrassUpper = 17;
    public const byte Fern = 18;
    public const byte Dandelion = 19;
    public const byte Poppy = 20;
    public const byte Cornflower = 21;
    public const byte OxeyeDaisy = 22;
    public const byte AzureBluet = 23;
    public const byte CoalOre = 24;
    public const byte DeepslateCoalOre = 25;
    public const byte IronOre = 26;
    public const byte DeepslateIronOre = 27;
    public const byte CopperOre = 28;
    public const byte DeepslateCopperOre = 29;
    public const byte GoldOre = 30;
    public const byte DeepslateGoldOre = 31;
    public const byte RedstoneOre = 32;
    public const byte DeepslateRedstoneOre = 33;
    public const byte LapisOre = 34;
    public const byte DeepslateLapisOre = 35;
    public const byte DiamondOre = 36;
    public const byte DeepslateDiamondOre = 37;
    public const byte EmeraldOre = 38;
    public const byte DeepslateEmeraldOre = 39;
    public const byte BeeNest = 40;
    public const byte BeeNestFull = 41;
    public const byte SweetBerryBush = 42;
    public const byte Pumpkin = 43;
    public const byte BrownMushroom = 44;
    public const byte RedMushroom = 45;
    public const byte Clay = 46;
    public const byte Seagrass = 47;

    public static readonly string[] Names =
    [
        "minecraft:air", "minecraft:bedrock", "minecraft:stone", "minecraft:deepslate", "minecraft:dirt",
        "minecraft:grass_block", "minecraft:sand", "minecraft:snow_block", "minecraft:water", "minecraft:gravel",
        "minecraft:oak_log", "minecraft:oak_leaves", "minecraft:spruce_log", "minecraft:spruce_leaves", "minecraft:moss_block",
        "minecraft:short_grass", "minecraft:tall_grass", "minecraft:tall_grass", "minecraft:fern",
        "minecraft:dandelion", "minecraft:poppy", "minecraft:cornflower", "minecraft:oxeye_daisy", "minecraft:azure_bluet",
        "minecraft:coal_ore", "minecraft:deepslate_coal_ore", "minecraft:iron_ore", "minecraft:deepslate_iron_ore",
        "minecraft:copper_ore", "minecraft:deepslate_copper_ore", "minecraft:gold_ore", "minecraft:deepslate_gold_ore",
        "minecraft:redstone_ore", "minecraft:deepslate_redstone_ore", "minecraft:lapis_ore", "minecraft:deepslate_lapis_ore",
        "minecraft:diamond_ore", "minecraft:deepslate_diamond_ore", "minecraft:emerald_ore", "minecraft:deepslate_emerald_ore",
        "minecraft:bee_nest", "minecraft:bee_nest", "minecraft:sweet_berry_bush", "minecraft:pumpkin",
        "minecraft:brown_mushroom", "minecraft:red_mushroom", "minecraft:clay", "minecraft:seagrass",
    ];

    /// <summary>Block state properties that must be written (leaves must be persistent or they decay; tall grass has two halves).</summary>
    public static readonly IReadOnlyDictionary<byte, (string Key, string Value)[]> Properties = new Dictionary<byte, (string, string)[]>
    {
        [OakLeaves] = [("persistent", "true")],
        [SpruceLeaves] = [("persistent", "true")],
        [TallGrassLower] = [("half", "lower")],
        [TallGrassUpper] = [("half", "upper")],
        [BeeNest] = [("facing", "south"), ("honey_level", "0")],
        [BeeNestFull] = [("facing", "south"), ("honey_level", "5")],
        [SweetBerryBush] = [("age", "3")],
    };

    private static readonly byte[] Flowers = [Dandelion, Poppy, Cornflower, OxeyeDaisy, AzureBluet];

    /// <summary>Picks a ground-cover plant for a grass column from a hash, or Air for bare grass.</summary>
    public static byte PickPlant(uint hash, bool forest, bool resources)
    {
        uint r = hash % 1000;
        if (forest)
        {
            if (r < 220) return Fern;
            if (r < 480) return ShortGrass;
            if (r < 495) return Flowers[(hash >> 10) % (uint)Flowers.Length];
            if (resources && r < 503) return SweetBerryBush;
            if (resources && r < 510) return (hash >> 12) % 2 == 0 ? BrownMushroom : RedMushroom;
            return Air;
        }
        if (r < 340) return ShortGrass;
        if (r < 370) return TallGrassLower;
        if (r < 405) return Flowers[(hash >> 10) % (uint)Flowers.Length];
        if (resources && r < 406) return Pumpkin;
        return Air;
    }
}

/// <summary>Builds and compresses one chunk's NBT from classified terrain.</summary>
public static class ChunkBuilder
{
    public const int DataVersion = 5023; // Java Edition 26.3
    public const int MinSectionY = -4;   // world Y -64
    public const int SectionCount = 24;  // up to Y 319

    private const string BiomePlains = "minecraft:plains";
    private const string BiomeSnowy = "minecraft:snowy_slopes";
    private const string BiomeStony = "minecraft:stony_peaks";
    private const string BiomeRiver = "minecraft:river";
    private const string BiomeForest = "minecraft:forest";
    private const string BiomeTaiga = "minecraft:taiga";

    private readonly record struct BeeNestEntity(int X, int Y, int Z);

    /// <summary>
    /// Builds chunk (cx, cz). <paramref name="terrain"/> coordinates: block x = cx*16 + i maps to
    /// terrain column x; columns outside the terrain are left as air. Returns zlib-compressed NBT.
    /// </summary>
    public static byte[] Build(int cx, int cz, ClassifiedTerrain terrain)
    {
        // ---- gather column info ------------------------------------------------------------
        Span<short> top = stackalloc short[256];
        Span<short> water = stackalloc short[256];
        Span<byte> kind = stackalloc byte[256];
        Span<bool> present = stackalloc bool[256];
        Span<bool> forest = stackalloc bool[256];
        Span<byte> bed = stackalloc byte[256]; // 0 = sand bed, 1 = clay bed, +2 = seagrass above the bed
        int chunkMaxY = int.MinValue;
        int snowy = 0, stony = 0, watery = 0, forested = 0, spruces = 0, oaks = 0, count = 0;
        bool resources = terrain.Resources;
        for (int lz = 0; lz < 16; lz++)
        {
            int tz = cz * 16 + lz;
            for (int lx = 0; lx < 16; lx++)
            {
                int tx = cx * 16 + lx;
                int i = lz * 16 + lx;
                if (tx < 0 || tz < 0 || tx >= terrain.Width || tz >= terrain.Height) { present[i] = false; continue; }
                present[i] = true;
                top[i] = terrain.TopAt(tx, tz);
                water[i] = terrain.WaterAt(tx, tz);
                var k = terrain.KindAt(tx, tz);
                kind[i] = (byte)k;
                forest[i] = terrain.IsForest(tx, tz);
                count++;
                if (k == Surface.Snow) snowy++;
                if (k == Surface.Stone) stony++;
                if (k == Surface.Water)
                {
                    watery++;
                    if (resources)
                    {
                        uint wh = TreePlanner.Hash(tx, tz, terrain.Seed ^ 0x5EA);
                        bed[i] = (byte)((wh % 100 < 35 ? 1 : 0) + ((wh >> 8) % 100 < 15 && water[i] - top[i] >= 2 ? 2 : 0));
                    }
                }
                if (forest[i]) forested++;
                int colTop = terrain.ColumnTop(tx, tz);
                if (colTop > chunkMaxY) chunkMaxY = colTop;
            }
        }

        // ---- trees: rasterise every tree that can reach this chunk into an overlay ------------
        Dictionary<int, byte>? overlay = null;
        List<BeeNestEntity>? nests = null;
        foreach (var tree in terrain.Trees.Near(cx, cz))
        {
            if (tree.Type == TreeType.Spruce) spruces++; else oaks++;
            int baseY = terrain.TopAt(tree.X, tree.Z);
            overlay ??= new Dictionary<int, byte>(512);
            var o = overlay;
            TreePlanner.Rasterize(tree, (x, z, dy, isLog) =>
            {
                int lx = x - cx * 16, lz = z - cz * 16;
                if (lx < 0 || lz < 0 || lx > 15 || lz > 15) return;
                int y = baseY + dy;
                if (y > TerrainOptions.WorldMaxY) return;
                byte b = tree.Type == TreeType.Spruce
                    ? (isLog ? Blocks.SpruceLog : Blocks.SpruceLeaves)
                    : (isLog ? Blocks.OakLog : Blocks.OakLeaves);
                int key = ((y - TerrainOptions.WorldMinY) << 8) | (lz << 4) | lx;
                if (isLog || !o.ContainsKey(key)) o[key] = b; // logs win over leaves of neighbours
                if (y > chunkMaxY) chunkMaxY = y;
            });

            // Bee nests hang on the south side of about 4 % of oaks, one block above the ground.
            if (resources && tree.Type == TreeType.Oak)
            {
                uint nh = TreePlanner.Hash(tree.X, tree.Z, terrain.Seed ^ 0xBEE5);
                if (nh % 100 < 4)
                {
                    int nx = tree.X, nz = tree.Z + 1, ny = baseY + 1;
                    int lx = nx - cx * 16, lz = nz - cz * 16;
                    if (lx >= 0 && lz >= 0 && lx <= 15 && lz <= 15 && nz < terrain.Height)
                    {
                        int key = ((ny - TerrainOptions.WorldMinY) << 8) | (lz << 4) | lx;
                        if (!overlay.TryGetValue(key, out var existing) || existing is Blocks.OakLeaves or Blocks.SpruceLeaves)
                        {
                            overlay[key] = (nh >> 8) % 2 == 0 ? Blocks.BeeNestFull : Blocks.BeeNest;
                            (nests ??= []).Add(new BeeNestEntity(nx, ny, nz));
                        }
                    }
                }
            }
        }

        // ---- ground cover on grass columns (never where a trunk or nest stands) --------------
        if (terrain.Vegetation && count > 0)
        {
            overlay ??= new Dictionary<int, byte>(512);
            for (int i = 0; i < 256; i++)
            {
                if (!present[i] || (Surface)kind[i] != Surface.Grass) continue;
                int tx = cx * 16 + (i & 15), tz = cz * 16 + (i >> 4);
                uint hash = TreePlanner.Hash(tx, tz, terrain.Seed ^ 0x5EED_1234L);
                byte plant = Blocks.PickPlant(hash, forest[i], resources);
                if (plant == Blocks.Air) continue;
                int y = top[i] + 1;
                if (y + 1 > TerrainOptions.WorldMaxY) continue;
                int key = ((y - TerrainOptions.WorldMinY) << 8) | i;
                if (overlay.ContainsKey(key)) continue;
                if (plant == Blocks.TallGrassLower)
                {
                    int upperKey = key + 256;
                    if (overlay.ContainsKey(upperKey)) plant = Blocks.ShortGrass;
                    else overlay[upperKey] = Blocks.TallGrassUpper;
                }
                overlay[key] = plant;
                if (y + 1 > chunkMaxY) chunkMaxY = y + 1;
            }
        }

        // ---- ores -----------------------------------------------------------------------------
        var ores = resources && count > 0 ? OrePlanner.Plan(cx, cz, terrain.Seed, MinSectionY, SectionCount) : null;

        string biome = count == 0 ? BiomePlains
            : watery * 2 >= count ? BiomeRiver
            : snowy * 2 >= count ? BiomeSnowy
            : stony * 2 >= count ? BiomeStony
            : forested * 2 >= count ? (spruces > oaks ? BiomeTaiga : BiomeForest)
            : BiomePlains;
        bool coarseForest = terrain.CoarseForest;

        // ---- write NBT --------------------------------------------------------------------------
        using var raw = new MemoryStream(32 * 1024);
        var w = new NbtWriter(raw);
        w.BeginCompound("");
        w.WriteInt("DataVersion", DataVersion);
        w.WriteInt("xPos", cx);
        w.WriteInt("yPos", MinSectionY);
        w.WriteInt("zPos", cz);
        w.WriteString("Status", "minecraft:full");
        w.WriteLong("LastUpdate", 0);
        w.WriteLong("InhabitedTime", 0);

        w.BeginList("block_entities", TagType.Compound, nests?.Count ?? 0);
        if (nests is not null)
        {
            foreach (var n in nests) WriteBeeNest(w, n);
        }
        // Bookkeeping tags the game writes itself; empty here (verified against a 26.3 chunk).
        w.BeginList("block_ticks", TagType.Compound, 0);
        w.BeginList("fluid_ticks", TagType.Compound, 0);
        w.BeginList("PostProcessing", TagType.List, SectionCount);
        for (int i = 0; i < SectionCount; i++) w.BeginList(null, TagType.Short, 0);
        w.BeginCompound("structures");
        w.BeginCompound("References");
        w.EndCompound();
        w.BeginCompound("starts");
        w.EndCompound();
        w.EndCompound();

        w.BeginList("sections", TagType.Compound, SectionCount);
        Span<byte> blocks = stackalloc byte[4096];
        Span<int> paletteIndex = stackalloc int[Blocks.Names.Length];
        for (int s = 0; s < SectionCount; s++)
        {
            int sy = MinSectionY + s;
            int baseY = sy * 16;
            w.BeginCompound(null);
            w.WriteByte("Y", (sbyte)sy);

            // Fill block ids for this section.
            blocks.Clear();
            bool anyNonAir = false;
            if (count > 0 && baseY <= chunkMaxY)
            {
                for (int ly = 0; ly < 16; ly++)
                {
                    int y = baseY + ly;
                    for (int i = 0; i < 256; i++)
                    {
                        if (!present[i]) continue;
                        byte b = BlockAt(y, top[i], (Surface)kind[i], water[i], forest[i] && coarseForest, bed[i]);
                        if (b == Blocks.Air && overlay is not null && y > top[i])
                        {
                            int key = ((y - TerrainOptions.WorldMinY) << 8) | i;
                            if (overlay.TryGetValue(key, out var ob)) b = ob;
                        }
                        if (b != Blocks.Air)
                        {
                            blocks[ly * 256 + i] = b;
                            anyNonAir = true;
                        }
                    }
                }
                // Ores replace stone / deepslate only, so the terrain shape never changes.
                if (ores?[s] is { } bucket)
                {
                    foreach (int e in bucket)
                    {
                        int idx = (e >> 12) * 256 + ((e >> 4) & 255);
                        var ore = OrePlanner.Ores[e & 15];
                        if (blocks[idx] == Blocks.Stone) blocks[idx] = ore.Stone;
                        else if (blocks[idx] == Blocks.Deepslate) blocks[idx] = ore.Deepslate;
                    }
                }
            }

            w.BeginCompound("block_states");
            if (!anyNonAir)
            {
                w.BeginList("palette", TagType.String, 1);
                w.WriteString(null, Blocks.Names[Blocks.Air]);
            }
            else
            {
                // Build palette (block id -> palette index) in order of first appearance.
                paletteIndex.Fill(-1);
                var palette = new List<byte>(8);
                foreach (byte b in blocks)
                {
                    if (paletteIndex[b] < 0) { paletteIndex[b] = palette.Count; palette.Add(b); }
                }
                WritePalette(w, palette);

                if (palette.Count > 1)
                {
                    int bits = Math.Max(4, BitOperations.Log2((uint)(palette.Count - 1)) + 1);
                    int perLong = 64 / bits;
                    var data = new long[(4096 + perLong - 1) / perLong];
                    for (int i = 0; i < 4096; i++)
                    {
                        long idx = paletteIndex[blocks[i]];
                        data[i / perLong] |= idx << ((i % perLong) * bits);
                    }
                    w.WriteLongArray("data", data);
                }
            }
            w.EndCompound(); // block_states

            w.BeginCompound("biomes");
            w.BeginList("palette", TagType.String, 1);
            w.WriteString(null, biome);
            w.EndCompound(); // biomes

            w.EndCompound(); // section
        }

        w.EndCompound(); // root

        using var output = new MemoryStream((int)raw.Length / 4 + 64);
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }
        return output.ToArray();
    }

    /// <summary>A bee nest block entity holding three bees, so honey actually gets produced.</summary>
    private static void WriteBeeNest(NbtWriter w, BeeNestEntity n)
    {
        w.BeginCompound(null);
        w.WriteString("id", "minecraft:beehive");
        w.WriteInt("x", n.X);
        w.WriteInt("y", n.Y);
        w.WriteInt("z", n.Z);
        w.BeginList("bees", TagType.Compound, 3);
        for (int i = 0; i < 3; i++)
        {
            w.BeginCompound(null);
            w.BeginCompound("entity_data");
            w.WriteString("id", "minecraft:bee");
            w.EndCompound();
            w.WriteInt("ticks_in_hive", 0);
            w.WriteInt("min_ticks_in_hive", 600);
            w.EndCompound();
        }
        w.EndCompound();
    }

    /// <summary>
    /// Since Java Edition 26.x a palette entry without block properties is written as a plain
    /// string; an entry with properties is a compound {id, properties}. NBT lists are homogeneous,
    /// so as soon as one entry needs properties every entry of that palette becomes a compound.
    /// (The pre-26 form {Name, Properties} is rejected with "No key id" and the section falls back to air.)
    /// </summary>
    private static void WritePalette(NbtWriter w, List<byte> palette)
    {
        bool anyProperties = palette.Any(b => Blocks.Properties.ContainsKey(b));
        if (!anyProperties)
        {
            w.BeginList("palette", TagType.String, palette.Count);
            foreach (byte b in palette) w.WriteString(null, Blocks.Names[b]);
            return;
        }
        w.BeginList("palette", TagType.Compound, palette.Count);
        foreach (byte b in palette)
        {
            w.BeginCompound(null);
            w.WriteString("id", Blocks.Names[b]);
            if (Blocks.Properties.TryGetValue(b, out var props))
            {
                w.BeginCompound("properties");
                foreach (var (key, value) in props) w.WriteString(key, value);
                w.EndCompound();
            }
            w.EndCompound();
        }
    }

    /// <summary>The block at world Y for a column with the given top and surface kind.</summary>
    private static byte BlockAt(int y, int top, Surface kind, short waterY, bool coarseForest, byte bed)
    {
        if (y == TerrainOptions.WorldMinY) return Blocks.Bedrock;
        if (y > top)
        {
            if (waterY != ClassifiedTerrain.NoWater && y <= waterY)
            {
                return y == top + 1 && (bed & 2) != 0 ? Blocks.Seagrass : Blocks.Water;
            }
            return Blocks.Air;
        }
        if (y == top)
        {
            return kind switch
            {
                Surface.Grass => coarseForest ? Blocks.MossBlock : Blocks.GrassBlock,
                Surface.Stone => Blocks.Stone,
                Surface.Snow => Blocks.SnowBlock,
                Surface.Sand => Blocks.Sand,
                Surface.Water => (bed & 1) != 0 ? Blocks.Clay : Blocks.Sand,
                _ => Blocks.Stone,
            };
        }
        if (y > top - 4)
        {
            return kind switch
            {
                Surface.Grass => Blocks.Dirt,
                Surface.Sand => Blocks.Sand,
                Surface.Water => y > top - 2 ? ((bed & 1) != 0 ? Blocks.Clay : Blocks.Sand) : Blocks.Gravel,
                _ => Blocks.Stone,
            };
        }
        return y < 0 ? Blocks.Deepslate : Blocks.Stone;
    }
}
