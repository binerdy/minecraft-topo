using System.IO.Compression;
using System.Numerics;
using MinecraftTopo.Core.Nbt;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

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
    public const byte RailNorthSouth = 48;
    public const byte RailEastWest = 49;
    public const byte RailNorthEast = 50;
    public const byte RailNorthWest = 51;
    public const byte RailSouthEast = 52;
    public const byte RailSouthWest = 53;
    public const byte RailAscendingNorth = 54;
    public const byte RailAscendingSouth = 55;
    public const byte RailAscendingEast = 56;
    public const byte RailAscendingWest = 57;
    public const byte GrayConcrete = 58;
    public const byte LightGrayConcrete = 59;
    public const byte Cobblestone = 60;
    public const byte StoneBricks = 61;
    public const byte WhiteConcrete = 62;
    public const byte SmoothSandstone = 63;
    public const byte Bricks = 64;
    public const byte SmoothStone = 65;
    public const byte Glass = 66;

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
        "minecraft:rail", "minecraft:rail", "minecraft:rail", "minecraft:rail", "minecraft:rail",
        "minecraft:rail", "minecraft:rail", "minecraft:rail", "minecraft:rail", "minecraft:rail",
        "minecraft:gray_concrete", "minecraft:light_gray_concrete", "minecraft:cobblestone", "minecraft:stone_bricks", "minecraft:white_concrete",
        "minecraft:smooth_sandstone", "minecraft:bricks", "minecraft:smooth_stone", "minecraft:glass",
    ];

    /// <summary>Block state properties that must be written (leaves must be persistent or they decay; tall grass has two halves; rails need a shape).</summary>
    public static readonly IReadOnlyDictionary<byte, (string Key, string Value)[]> Properties = new Dictionary<byte, (string, string)[]>
    {
        [OakLeaves] = [("persistent", "true")],
        [SpruceLeaves] = [("persistent", "true")],
        [TallGrassLower] = [("half", "lower")],
        [TallGrassUpper] = [("half", "upper")],
        [BeeNest] = [("facing", "south"), ("honey_level", "0")],
        [BeeNestFull] = [("facing", "south"), ("honey_level", "5")],
        [SweetBerryBush] = [("age", "3")],
        [RailNorthSouth] = [("shape", "north_south")],
        [RailEastWest] = [("shape", "east_west")],
        [RailNorthEast] = [("shape", "north_east")],
        [RailNorthWest] = [("shape", "north_west")],
        [RailSouthEast] = [("shape", "south_east")],
        [RailSouthWest] = [("shape", "south_west")],
        [RailAscendingNorth] = [("shape", "ascending_north")],
        [RailAscendingSouth] = [("shape", "ascending_south")],
        [RailAscendingEast] = [("shape", "ascending_east")],
        [RailAscendingWest] = [("shape", "ascending_west")],
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

    public static byte RoadBlock(RoadMaterial m) => m switch
    {
        RoadMaterial.Asphalt => GrayConcrete,
        RoadMaterial.LightAsphalt => LightGrayConcrete,
        RoadMaterial.DirtPath => Dirt, // topped by a dirt path via RoadTopBlock
        RoadMaterial.Gravel => RoadGravel,
        RoadMaterial.Cobblestone => Cobblestone,
        RoadMaterial.StoneBricks => StoneBricks,
        _ => GrayConcrete,
    };

    public static byte RoadTopBlock(RoadMaterial m) => m == RoadMaterial.DirtPath ? DirtPathBlock : RoadBlock(m);

    public const byte DirtPathBlock = 67;
    // 68..73 are defined in Terrain.StructureBlocks (iron bars, oak fence, chain x/z, smooth quartz, deepslate tiles)
    /// <summary>Standing oak sign with rotation r is id OakSign0 + r (r = 0..15).</summary>
    public const byte OakSign0 = 74;
    public const byte Mud = 90;
    public const byte PackedIce = 91;
    public const byte Azalea = 92;
    // Separate ids for blocks that share a default name, so each role can be overridden on its own.
    public const byte RoadGravel = 93;
    public const byte RailBed = 94;
    public const byte RoadMarking = 95;
    public const byte IndustrialRoof = 96;
    public const byte WallLight = 97;
    public const byte ChurchWall = 98;
    /// <summary>GK500 lithology class c (1..23) is id GeologyBase + c - 1.</summary>
    public const byte GeologyBase = 99;
    public const int GeologyClasses = 23;

    public static bool IsGeology(byte b) => b >= GeologyBase && b < GeologyBase + GeologyClasses;
    public static byte GeologyBlock(byte lithologyClass) => lithologyClass is >= 1 and <= GeologyClasses ? (byte)(GeologyBase + lithologyClass - 1) : Stone;

    public static readonly string[] ExtraNames =
    [
        "minecraft:dirt_path", "minecraft:iron_bars", "minecraft:oak_fence", "minecraft:chain", "minecraft:chain", "minecraft:smooth_quartz",
        "minecraft:deepslate_tiles",
        .. Enumerable.Repeat("minecraft:oak_sign", 16),
        "minecraft:mud", "minecraft:packed_ice", "minecraft:azalea",
        "minecraft:gravel", "minecraft:gravel", "minecraft:white_concrete", "minecraft:gray_concrete", "minecraft:light_gray_concrete", "minecraft:stone_bricks",
        // GK500 lithology classes 1..23 (defaults; see BlockRoles)
        "minecraft:clay", "minecraft:gravel", "minecraft:cobblestone", "minecraft:tuff", "minecraft:sandstone", "minecraft:packed_mud", "minecraft:deepslate",
        "minecraft:stone", "minecraft:diorite", "minecraft:terracotta", "minecraft:white_terracotta", "minecraft:granite", "minecraft:polished_granite",
        "minecraft:basalt", "minecraft:polished_tuff", "minecraft:calcite", "minecraft:smooth_quartz", "minecraft:polished_andesite", "minecraft:polished_deepslate",
        "minecraft:andesite", "minecraft:cobbled_deepslate", "minecraft:blackstone", "minecraft:polished_blackstone",
    ];

    /// <summary>Properties for the extra ids (chains need an axis, signs a rotation).</summary>
    public static readonly IReadOnlyDictionary<byte, (string Key, string Value)[]> ExtraProperties = BuildExtraProperties();

    private static Dictionary<byte, (string, string)[]> BuildExtraProperties()
    {
        var d = new Dictionary<byte, (string, string)[]>
        {
            [StructureBlocks.ChainX] = [("axis", "x")],
            [StructureBlocks.ChainZ] = [("axis", "z")],
        };
        for (int r = 0; r < 16; r++) d[(byte)(OakSign0 + r)] = [("rotation", r.ToString())];
        return d;
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

    /// <summary>Per-column infrastructure info gathered for one chunk.</summary>
    private struct Column
    {
        public short Top, WaterY;
        public byte Kind;
        public bool Present, Forest;
        public byte Bed;                 // resources: 1 = clay bed, 2 = seagrass
        public byte Road, RoadFlags;     // RoadMaterial + RoadFlag bits
        public byte Rail;                // RailFlag bits
        public byte RailShape;           // block id of the rail here (0 = none)
        public byte BuildingHeight, BuildingFlags;
        public short BuildingFloor;
        public byte TowerHeight;         // church tower cells: height above the floor
        public byte Geology;             // GK500 lithology class (0 = unknown -> plain stone)
        /// <summary>Y of the surface a road/rail sits on: the terrain top or a bridge deck above water.</summary>
        public int SurfaceY => WaterY != ClassifiedTerrain.NoWater && WaterY >= Top ? WaterY + 1 : Top;
        public bool OverWater => WaterY != ClassifiedTerrain.NoWater && WaterY >= Top;
    }

    /// <summary>
    /// Builds chunk (cx, cz). <paramref name="terrain"/> coordinates: block x = cx*16 + i maps to
    /// terrain column x; columns outside the terrain are left as air. Returns zlib-compressed NBT.
    /// </summary>
    public static byte[] Build(int cx, int cz, ClassifiedTerrain terrain)
    {
        // ---- gather column info ------------------------------------------------------------
        var cols = new Column[256];
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
                if (tx < 0 || tz < 0 || tx >= terrain.Width || tz >= terrain.Height) continue;
                int gi = tz * terrain.Width + tx;
                ref var c = ref cols[i];
                c.Present = true;
                c.Top = terrain.TopY[gi];
                c.WaterY = terrain.WaterY[gi];
                var k = terrain.Kind[gi];
                c.Kind = (byte)k;
                c.Forest = terrain.IsForest(tx, tz);
                c.Geology = terrain.Geology?[gi] ?? 0;
                count++;
                if (k is Surface.Snow or Surface.Glacier) snowy++;
                if (k == Surface.Stone) stony++;
                if (k == Surface.Water)
                {
                    watery++;
                    if (resources)
                    {
                        uint wh = TreePlanner.Hash(tx, tz, terrain.Seed ^ 0x5EA);
                        c.Bed = (byte)((wh % 100 < 35 ? 1 : 0) + ((wh >> 8) % 100 < 15 && c.WaterY - c.Top >= 2 ? 2 : 0));
                    }
                }
                if (c.Forest) forested++;
                int colTop = terrain.ColumnTop(tx, tz);

                if (terrain.Road is not null && terrain.Road[gi] != 0)
                {
                    c.Road = terrain.Road[gi];
                    c.RoadFlags = terrain.RoadFlags![gi];
                    colTop = Math.Max(colTop, c.SurfaceY);
                }
                if (terrain.Rail is not null && terrain.Rail[gi] != 0)
                {
                    c.Rail = terrain.Rail[gi];
                    if ((c.Rail & RailFlag.Track) != 0)
                    {
                        c.RailShape = RailShape(terrain, tx, tz, c.SurfaceY + 1);
                        colTop = Math.Max(colTop, c.SurfaceY + 1);
                    }
                    else colTop = Math.Max(colTop, c.SurfaceY);
                }
                if (terrain.BuildingHeight is not null && terrain.BuildingHeight[gi] != 0)
                {
                    c.BuildingHeight = terrain.BuildingHeight[gi];
                    c.BuildingFloor = terrain.BuildingFloor![gi];
                    c.BuildingFlags = terrain.BuildingFlags![gi];
                    c.TowerHeight = terrain.BuildingTower?[gi] ?? 0;
                    colTop = Math.Max(colTop, c.BuildingFloor + Math.Max(c.BuildingHeight + 1, c.TowerHeight));
                }
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

        // ---- pylons, poles, wind turbines and their wires ------------------------------------------
        foreach (var s in terrain.Structures.Get(cx, cz))
        {
            overlay ??= new Dictionary<int, byte>(512);
            var o = overlay;
            int ground = terrain.ColumnTop(s.X, s.Z);
            StructurePlanner.Rasterize(s, terrain.MetresPerBlock, TerrainOptions.WorldMaxY - ground, (x, z, dy, block) =>
            {
                int lx = x - cx * 16, lz = z - cz * 16;
                if (lx < 0 || lz < 0 || lx > 15 || lz > 15) return;
                int y = ground + dy;
                if (y > TerrainOptions.WorldMaxY) return;
                int key = ((y - TerrainOptions.WorldMinY) << 8) | (lz << 4) | lx;
                o[key] = block; // structures win over leaves and plants
                if (y > chunkMaxY) chunkMaxY = y;
            });
        }
        foreach (var (wire, y0, y1) in terrain.Wires.Get(cx, cz))
        {
            overlay ??= new Dictionary<int, byte>(512);
            var o = overlay;
            StructurePlanner.RasterizeWire(wire, y0, y1, (x, z, y, block) =>
            {
                int lx = x - cx * 16, lz = z - cz * 16;
                if (lx < 0 || lz < 0 || lx > 15 || lz > 15 || y > TerrainOptions.WorldMaxY) return;
                int key = ((y - TerrainOptions.WorldMinY) << 8) | (lz << 4) | lx;
                if (!o.ContainsKey(key)) o[key] = block;
                if (y > chunkMaxY) chunkMaxY = y;
            });
        }

        // ---- street name signs on the ground beside the road ----------------------------------------
        List<(int X, int Y, int Z, string[] Lines)>? signs = null;
        foreach (var sign in terrain.Signs.Get(cx, cz))
        {
            int lx = sign.X - cx * 16, lz = sign.Z - cz * 16;
            if (lx < 0 || lz < 0 || lx > 15 || lz > 15) continue;
            ref var c = ref cols[lz * 16 + lx];
            if (!c.Present || c.Road != 0 || c.Rail != 0 || c.BuildingHeight != 0 || (Surface)c.Kind == Surface.Water) continue;
            int y = c.Top + 1;
            if (y > TerrainOptions.WorldMaxY) continue;
            int key = ((y - TerrainOptions.WorldMinY) << 8) | (lz << 4) | lx;
            overlay ??= new Dictionary<int, byte>(512);
            if (overlay.ContainsKey(key)) continue;
            overlay[key] = (byte)(Blocks.OakSign0 + sign.Rotation);
            (signs ??= []).Add((sign.X, y, sign.Z, sign.Lines));
            if (y > chunkMaxY) chunkMaxY = y;
        }

        // ---- ground cover on grass columns (never on roads, rails, buildings, trunks or nests) --
        if (terrain.Vegetation && count > 0)
        {
            overlay ??= new Dictionary<int, byte>(512);
            for (int i = 0; i < 256; i++)
            {
                ref var c = ref cols[i];
                if (!c.Present || c.Road != 0 || c.Rail != 0 || c.BuildingHeight != 0) continue;
                var kindHere = (Surface)c.Kind;
                if (kindHere is not (Surface.Grass or Surface.Vineyard or Surface.Mud)) continue;
                int tx = cx * 16 + (i & 15), tz = cz * 16 + (i >> 4);
                uint hash = TreePlanner.Hash(tx, tz, terrain.Seed ^ 0x5EED_1234L);
                byte plant = kindHere switch
                {
                    Surface.Vineyard => tx % 3 == 0 ? Blocks.Azalea : (hash % 100 < 25 ? Blocks.ShortGrass : Blocks.Air), // rows of vines
                    Surface.Mud => hash % 100 < 45 ? (hash % 2 == 0 ? Blocks.Fern : Blocks.ShortGrass) : Blocks.Air,       // reeds
                    _ => Blocks.PickPlant(hash, c.Forest, resources),
                };
                if (plant == Blocks.Air) continue;
                int y = c.Top + 1;
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
        bool windows = terrain.MetresPerBlock <= 2;

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

        w.BeginList("block_entities", TagType.Compound, (nests?.Count ?? 0) + (signs?.Count ?? 0));
        if (nests is not null)
        {
            foreach (var n in nests) WriteBeeNest(w, n);
        }
        if (signs is not null)
        {
            foreach (var (sx, sy, sz, lines) in signs) WriteSign(w, sx, sy, sz, lines);
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
        var palette = terrain.Palette;
        Span<int> paletteIndex = stackalloc int[palette.Names.Length];
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
                        ref var c = ref cols[i];
                        if (!c.Present) continue;
                        byte b = BlockAt(y, in c, coarseForest, windows, i);
                        if (b == Blocks.Air && overlay is not null && y > c.Top)
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
                        if (blocks[idx] == Blocks.Stone || Blocks.IsGeology(blocks[idx])) blocks[idx] = ore.Stone;
                        else if (blocks[idx] == Blocks.Deepslate) blocks[idx] = ore.Deepslate;
                    }
                }
            }

            w.BeginCompound("block_states");
            if (!anyNonAir)
            {
                w.BeginList("palette", TagType.String, 1);
                w.WriteString(null, palette.Names[Blocks.Air]);
            }
            else
            {
                // Build palette (block id -> palette index) in order of first appearance.
                paletteIndex.Fill(-1);
                var used = new List<byte>(8);
                foreach (byte b in blocks)
                {
                    if (paletteIndex[b] < 0) { paletteIndex[b] = used.Count; used.Add(b); }
                }
                WritePalette(w, palette, used);

                if (used.Count > 1)
                {
                    int bits = Math.Max(4, BitOperations.Log2((uint)(used.Count - 1)) + 1);
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

    /// <summary>Y of the rail block in a column, or int.MinValue when there is no track there.</summary>
    private static int RailY(ClassifiedTerrain t, int tx, int tz)
    {
        if (tx < 0 || tz < 0 || tx >= t.Width || tz >= t.Height) return int.MinValue;
        int gi = tz * t.Width + tx;
        if (t.Rail is null || (t.Rail[gi] & RailFlag.Track) == 0) return int.MinValue;
        int top = t.TopY[gi];
        short wy = t.WaterY[gi];
        int surface = wy != ClassifiedTerrain.NoWater && wy >= top ? wy + 1 : top;
        return surface + 1;
    }

    /// <summary>Picks the rail shape from the neighbouring track cells (rails only join orthogonally).</summary>
    private static byte RailShape(ClassifiedTerrain t, int tx, int tz, int y)
    {
        int n = RailY(t, tx, tz - 1), s = RailY(t, tx, tz + 1), e = RailY(t, tx + 1, tz), w = RailY(t, tx - 1, tz);
        bool hn = n != int.MinValue, hs = s != int.MinValue, he = e != int.MinValue, hw = w != int.MinValue;
        int conns = (hn ? 1 : 0) + (hs ? 1 : 0) + (he ? 1 : 0) + (hw ? 1 : 0);
        if (conns == 2)
        {
            if (hn && he) return Blocks.RailNorthEast;
            if (hn && hw) return Blocks.RailNorthWest;
            if (hs && he) return Blocks.RailSouthEast;
            if (hs && hw) return Blocks.RailSouthWest;
        }
        bool northSouth = hn || hs ? !(he && hw && !(hn && hs)) : false;
        if (!hn && !hs && !he && !hw) northSouth = true;
        if (northSouth)
        {
            if (hn && n == y + 1) return Blocks.RailAscendingNorth;
            if (hs && s == y + 1) return Blocks.RailAscendingSouth;
            return Blocks.RailNorthSouth;
        }
        if (he && e == y + 1) return Blocks.RailAscendingEast;
        if (hw && w == y + 1) return Blocks.RailAscendingWest;
        return Blocks.RailEastWest;
    }

    /// <summary>A waxed standing sign with the street name on the front (plain strings are literal text components).</summary>
    private static void WriteSign(NbtWriter w, int x, int y, int z, string[] lines)
    {
        w.BeginCompound(null);
        w.WriteString("id", "minecraft:sign");
        w.WriteInt("x", x);
        w.WriteInt("y", y);
        w.WriteInt("z", z);
        w.WriteBool("is_waxed", true);
        foreach (var side in new[] { "front_text", "back_text" })
        {
            w.BeginCompound(side);
            w.BeginList("messages", TagType.String, 4);
            for (int i = 0; i < 4; i++) w.WriteString(null, side == "front_text" && i < lines.Length ? lines[i] : "");
            w.WriteString("color", "black");
            w.WriteBool("has_glowing_text", false);
            w.EndCompound();
        }
        w.EndCompound();
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
    private static void WritePalette(NbtWriter w, BlockPalette palette, List<byte> used)
    {
        var names = palette.Names;
        var properties = palette.Properties;
        bool anyProperties = used.Any(properties.ContainsKey);
        if (!anyProperties)
        {
            w.BeginList("palette", TagType.String, used.Count);
            foreach (byte b in used) w.WriteString(null, names[b]);
            return;
        }
        w.BeginList("palette", TagType.Compound, used.Count);
        foreach (byte b in used)
        {
            w.BeginCompound(null);
            w.WriteString("id", names[b]);
            if (properties.TryGetValue(b, out var props))
            {
                w.BeginCompound("properties");
                foreach (var (key, value) in props) w.WriteString(key, value);
                w.EndCompound();
            }
            w.EndCompound();
        }
    }

    /// <summary>The block at world Y for a column, in precedence order: building, rail, road, terrain.</summary>
    private static byte BlockAt(int y, in Column c, bool coarseForest, bool windows, int i)
    {
        if (y == TerrainOptions.WorldMinY) return Blocks.Bedrock;

        // Buildings: walls on the footprint edge, a floor slab and a flat roof; a solid base on slopes.
        if (c.BuildingHeight != 0)
        {
            int floor = c.BuildingFloor, top = floor + c.BuildingHeight, roof = top + 1;
            bool edge = (c.BuildingFlags & BuildingFlag.Edge) != 0;
            bool industrial = (c.BuildingFlags & BuildingFlag.Industrial) != 0;
            bool church = (c.BuildingFlags & BuildingFlag.Church) != 0;
            byte wall = church ? Blocks.ChurchWall
                : (c.BuildingFlags >> 4) switch { 1 => Blocks.WallLight, 2 => Blocks.SmoothSandstone, _ => Blocks.WhiteConcrete };
            if (c.TowerHeight != 0)
            {
                // church tower: solid stone-brick shaft up to the tower top, spire comes from the overlay
                int towerTop = floor + c.TowerHeight;
                if (y > towerTop) return Blocks.Air;
                if (y > c.Top)
                {
                    bool slit = windows && y > floor + 2 && y < towerTop - 1 && (y - floor) % 4 == 0 && (((i & 15) + (i >> 4)) & 1) == 0;
                    return slit ? Blocks.Glass : wall;
                }
            }
            else
            {
                if (y == roof) return church ? StructureBlocks.DeepslateTiles : industrial ? Blocks.IndustrialRoof : Blocks.Bricks;
                if (y > roof) return Blocks.Air;
            }
            if (edge)
            {
                if (y > c.Top)
                {
                    bool windowRow = windows && y > floor + 1 && y < top && (y - floor - 2) % 3 == 0 && (((i & 15) + (i >> 4)) & 1) == 0;
                    return windowRow ? Blocks.Glass : wall;
                }
            }
            else
            {
                if (y == floor) return Blocks.SmoothStone;
                if (y > floor) return Blocks.Air;
                if (y > c.Top) return wall; // base under the floor on sloping ground
            }
            // below the terrain top: fall through to terrain
        }

        int surface = c.SurfaceY;

        // Railways: gravel bed with the rail on top; over water a floating deck.
        if (c.Rail != 0)
        {
            if ((c.Rail & RailFlag.Track) != 0 && y == surface + 1) return c.RailShape;
            if (y == surface)
            {
                if (c.Road != 0) return RoadSurface(in c); // level crossing / tram in the street
                return Blocks.RailBed;
            }
            if (c.OverWater && y > c.Top && y < surface) return Blocks.Water;
        }

        // Roads: two blocks thick on land, a deck one block above the water on bridges/fords.
        if (c.Road != 0)
        {
            if (y == surface) return RoadSurface(in c);
            if (!c.OverWater && y == surface - 1) return Blocks.RoadBlock((RoadMaterial)c.Road);
            if (c.OverWater && y > c.Top && y < surface) return Blocks.Water;
        }

        return TerrainBlock(y, in c, coarseForest);
    }

    private static byte RoadSurface(in Column c) =>
        (c.RoadFlags & RoadFlag.Marking) != 0 ? Blocks.RoadMarking : Blocks.RoadTopBlock((RoadMaterial)c.Road);

    private static byte TerrainBlock(int y, in Column c, bool coarseForest)
    {
        int top = c.Top;
        var kind = (Surface)c.Kind;
        if (y > top)
        {
            if (c.WaterY != ClassifiedTerrain.NoWater && y <= c.WaterY)
            {
                return y == top + 1 && (c.Bed & 2) != 0 ? Blocks.Seagrass : Blocks.Water;
            }
            return Blocks.Air;
        }
        if (y == top)
        {
            return kind switch
            {
                Surface.Grass => coarseForest && c.Forest ? Blocks.MossBlock : Blocks.GrassBlock,
                Surface.Stone => Blocks.GeologyBlock(c.Geology),
                Surface.Snow => Blocks.SnowBlock,
                Surface.Sand => Blocks.Sand,
                Surface.Water => (c.Bed & 1) != 0 ? Blocks.Clay : Blocks.Sand,
                Surface.Gravel => Blocks.Gravel,
                Surface.Glacier => Blocks.SnowBlock,
                Surface.Mud => Blocks.Mud,
                Surface.Vineyard => Blocks.GrassBlock,
                _ => Blocks.GeologyBlock(c.Geology),
            };
        }
        if (y > top - 4)
        {
            return kind switch
            {
                Surface.Grass => Blocks.Dirt,
                Surface.Sand => Blocks.Sand,
                Surface.Water => y > top - 2 ? ((c.Bed & 1) != 0 ? Blocks.Clay : Blocks.Sand) : Blocks.Gravel,
                Surface.Gravel => Blocks.Gravel,
                Surface.Glacier => Blocks.PackedIce,
                Surface.Mud => y > top - 2 ? Blocks.Mud : Blocks.Dirt,
                Surface.Vineyard => Blocks.Dirt,
                _ => Blocks.GeologyBlock(c.Geology),
            };
        }
        return y < 0 ? Blocks.Deepslate : Blocks.GeologyBlock(c.Geology);
    }
}
