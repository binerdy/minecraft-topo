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
        RoadMaterial.Stairs => StoneBricks,
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

    // Extras from swissTLM3D, imagery and glaciers (ids 122..).
    public const byte Farmland = 122;
    public const byte CoarseDirt = 123;
    public const byte CobblestoneWall = 124;
    public const byte DamWall = 125;       // stone bricks
    public const byte Runway = 126;        // gray concrete
    public const byte Platform = 127;      // smooth stone
    public const byte Parking = 128;       // light gray concrete
    public const byte LiftMast = 129;      // iron bars
    public const byte Lantern = 130;
    public const byte RunningTrack = 131;  // red terracotta
    public const byte Jetty = 132;         // oak planks
    public const byte Monument = 133;      // chiseled stone bricks
    public const byte Podzol = 134;
    public const byte BlueIce = 135;
    public const byte SportsLine = 136;    // white concrete
    public const byte Barrier = 137;       // iron bars (avalanche barriers)
    public const byte BankWall = 138;      // stone bricks (river bank revetments)
    public const byte Quarry = 139;        // stone
    // The colour palette occupies 140..CropBase-1; everything after starts at CropBase.
    public const byte CropBase = 220;
    public const byte Wheat = CropBase;
    public const byte Carrots = CropBase + 1;
    public const byte Potatoes = CropBase + 2;
    public const byte Beetroots = CropBase + 3;
    public const byte StreetLamp = CropBase + 4;   // oak fence post with a lantern on top
    /// <summary>Snow layer with n layers (1..7) is SnowLayer1 + n - 1.</summary>
    public const byte SnowLayer1 = CropBase + 5;
    public const byte DarkRoof = CropBase + 12;    // deepslate tiles
    public const byte LightRoof = CropBase + 13;   // light gray concrete
    public const byte WhiteRoof = CropBase + 14;   // smooth quartz
    public const byte IronBlock = CropBase + 15;
    public const byte Glowstone = CropBase + 16;
    public const byte RoofPlanks = CropBase + 17;  // spruce planks (covered bridges)
    /// <summary>Stone brick stairs facing direction d (0 N, 1 E, 2 S, 3 W) is Stairs0 + d.</summary>
    public const byte Stairs0 = CropBase + 18;
    public const byte DoorLower0 = CropBase + 22;
    public const byte DoorUpper0 = CropBase + 26;
    public const byte Ladder0 = CropBase + 30;
    public const byte LastId = Ladder0 + 3;
    public static readonly string[] Facings = ["north", "east", "south", "west"];
    /// <summary>Coloured blocks for draping images over the ground: id ColourBase + i names <see cref="ColourPalette"/>[i].</summary>
    public const byte ColourBase = 140;

    /// <summary>Blocks used to paint pictures onto the ground with their approximate top-face colours.</summary>
    public static readonly (string Name, byte R, byte G, byte B)[] ColourPalette =
    [
        ("white_concrete", 207, 213, 214), ("light_gray_concrete", 125, 125, 115), ("gray_concrete", 55, 58, 62), ("black_concrete", 8, 10, 15),
        ("brown_concrete", 96, 60, 32), ("red_concrete", 142, 33, 33), ("orange_concrete", 224, 97, 1), ("yellow_concrete", 241, 175, 21),
        ("lime_concrete", 94, 169, 25), ("green_concrete", 73, 91, 36), ("cyan_concrete", 21, 119, 136), ("light_blue_concrete", 36, 137, 199),
        ("blue_concrete", 45, 47, 143), ("purple_concrete", 100, 32, 156), ("magenta_concrete", 169, 48, 159), ("pink_concrete", 214, 101, 143),
        ("white_wool", 234, 236, 237), ("light_gray_wool", 142, 142, 135), ("gray_wool", 63, 68, 72), ("black_wool", 21, 21, 26),
        ("brown_wool", 114, 72, 41), ("red_wool", 161, 39, 35), ("orange_wool", 241, 118, 20), ("yellow_wool", 249, 198, 40),
        ("lime_wool", 112, 185, 26), ("green_wool", 85, 110, 28), ("cyan_wool", 21, 138, 145), ("light_blue_wool", 58, 175, 217),
        ("blue_wool", 53, 57, 157), ("purple_wool", 122, 42, 173), ("magenta_wool", 190, 69, 180), ("pink_wool", 238, 141, 172),
        ("white_terracotta", 210, 178, 161), ("light_gray_terracotta", 135, 107, 98), ("gray_terracotta", 58, 42, 36), ("black_terracotta", 37, 23, 16),
        ("brown_terracotta", 77, 51, 36), ("red_terracotta", 143, 61, 47), ("orange_terracotta", 162, 84, 38), ("yellow_terracotta", 186, 133, 35),
        ("lime_terracotta", 104, 118, 53), ("green_terracotta", 76, 83, 42), ("cyan_terracotta", 87, 91, 91), ("light_blue_terracotta", 113, 109, 138),
        ("blue_terracotta", 74, 60, 91), ("purple_terracotta", 118, 70, 86), ("magenta_terracotta", 150, 88, 109), ("pink_terracotta", 162, 78, 79),
        ("terracotta", 152, 94, 68), ("sandstone", 216, 203, 155), ("sand", 219, 207, 163), ("gravel", 136, 126, 126), ("stone", 125, 125, 125),
        ("calcite", 223, 224, 220), ("tuff", 108, 110, 100), ("deepslate", 80, 80, 85), ("dirt", 134, 96, 67), ("coarse_dirt", 119, 85, 59),
        ("moss_block", 89, 109, 45), ("grass_block", 91, 161, 57), ("snow_block", 245, 248, 250), ("mud", 60, 57, 60), ("packed_mud", 142, 106, 80),
        ("oak_planks", 162, 130, 78), ("spruce_planks", 114, 84, 48), ("birch_planks", 192, 175, 121), ("dark_oak_planks", 66, 43, 20),
        ("smooth_quartz", 236, 233, 226), ("bricks", 150, 85, 70), ("netherrack", 97, 38, 38), ("end_stone", 219, 222, 158),
    ];

    public static readonly int ColourCount = ColourPalette.Length;

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
        // 122.. extras
        "minecraft:farmland", "minecraft:coarse_dirt", "minecraft:cobblestone_wall", "minecraft:stone_bricks", "minecraft:gray_concrete",
        "minecraft:smooth_stone", "minecraft:light_gray_concrete", "minecraft:iron_bars", "minecraft:lantern", "minecraft:red_terracotta",
        "minecraft:oak_planks", "minecraft:chiseled_stone_bricks", "minecraft:podzol", "minecraft:blue_ice", "minecraft:white_concrete",
        "minecraft:iron_bars", "minecraft:stone_bricks", "minecraft:stone",
        // 140.. colour palette, padded up to CropBase where the crops start
        .. ColourPalette.Select(c => "minecraft:" + c.Name),
        .. Enumerable.Repeat("minecraft:air", CropBase - ColourBase - ColourPalette.Length),
        "minecraft:wheat", "minecraft:carrots", "minecraft:potatoes", "minecraft:beetroots",
        "minecraft:oak_fence",
        .. Enumerable.Repeat("minecraft:snow", 7),
        "minecraft:deepslate_tiles", "minecraft:light_gray_concrete", "minecraft:smooth_quartz",
        "minecraft:iron_block", "minecraft:glowstone", "minecraft:spruce_planks",
        .. Enumerable.Repeat("minecraft:stone_brick_stairs", 4),
        .. Enumerable.Repeat("minecraft:oak_door", 8),
        .. Enumerable.Repeat("minecraft:ladder", 4),
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
        d[Wheat] = [("age", "7")];
        d[Carrots] = [("age", "7")];
        d[Potatoes] = [("age", "7")];
        d[Beetroots] = [("age", "3")];
        for (int n = 1; n <= 7; n++) d[(byte)(SnowLayer1 + n - 1)] = [("layers", n.ToString())];
        for (int f = 0; f < 4; f++)
        {
            d[(byte)(Stairs0 + f)] = [("facing", Facings[f]), ("half", "bottom"), ("shape", "straight")];
            d[(byte)(DoorLower0 + f)] = [("facing", Facings[f]), ("half", "lower"), ("hinge", "left"), ("open", "false"), ("powered", "false")];
            d[(byte)(DoorUpper0 + f)] = [("facing", Facings[f]), ("half", "upper"), ("hinge", "left"), ("open", "false"), ("powered", "false")];
            d[(byte)(Ladder0 + f)] = [("facing", Facings[f])];
        }
        return d;
    }
}

/// <summary>Builds and compresses one chunk's NBT from classified terrain.</summary>
public static class ChunkBuilder
{
    public const int DataVersion = 5023; // Java Edition 26.3
    public const int MinSectionY = -4;   // world Y -64 (standard height)
    public const int SectionCount = 24;  // up to Y 319 (standard height)

    private const string BiomePlains = "minecraft:plains";
    private const string BiomeSnowy = "minecraft:snowy_slopes";
    private const string BiomeStony = "minecraft:stony_peaks";
    private const string BiomeRiver = "minecraft:river";
    private const string BiomeForest = "minecraft:forest";
    private const string BiomeTaiga = "minecraft:taiga";
    private const string BiomeMeadow = "minecraft:meadow";
    private const string BiomeSwamp = "minecraft:swamp";
    private const string BiomeFrozenRiver = "minecraft:frozen_river";

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
        public byte Geology;             // bedrock lithology class (0 = unknown -> plain stone)
        public byte Deposit;             // unconsolidated deposit class for the top DepositBlocks (0 = none)
        public short IceBase;            // glacier columns: last rock block under the ice (NoWater = unknown)
        public byte TopBlock;            // top block override from imagery/extras (0 = none)
        public byte Wall;                // block standing on the surface (0 = none)
        public byte RoofBlock;           // roof override from the orthophoto (0 = default)
        public float Elevation;          // real elevation (m), for the snow gradient
        public int GridIndex;            // index into the terrain arrays (for per-column horizon lookups)
        public short Deck;               // surveyed deck Y for bridges/tunnels/platforms (NoWater = on the ground)
        public byte DeckFlags;           // DeckFlag bits
        public byte BKind;               // BuildingKind
        public byte Detail;              // BuildingDetail bits
        public byte StairFacing;         // outdoor stairs: 0 N, 1 E, 2 S, 3 W
        /// <summary>Y of the surface a road/rail sits on: the terrain top, a bridge or tunnel deck, or a deck above water.</summary>
        public int SurfaceY => Deck != ClassifiedTerrain.NoWater ? Deck : WaterY != ClassifiedTerrain.NoWater && WaterY >= Top ? WaterY + 1 : Top;
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
        DepositBlocks = terrain.DepositBlocks;
        Horizons = terrain.Horizons;
        WorldMinYStatic = terrain.WorldMinY;
        StoreyBlocks = BuildingDetails.StoreyBlocks(terrain.MetresPerBlock);
        TunnelClearance = terrain.TunnelClearance;
        int minSection = terrain.WorldMinY >> 4;
        int sectionCount = (terrain.WorldMaxY + 1 - terrain.WorldMinY) / 16;
        int snowy = 0, stony = 0, watery = 0, forested = 0, spruces = 0, oaks = 0, count = 0, muddy = 0, grassy = 0;
        double elevSum = 0;
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
                c.Deposit = terrain.Deposit?[gi] ?? 0;
                c.IceBase = terrain.IceBase?[gi] ?? ClassifiedTerrain.NoWater;
                c.TopBlock = terrain.TopBlock?[gi] ?? 0;
                c.Wall = terrain.Wall?[gi] ?? 0;
                c.RoofBlock = terrain.RoofBlock?[gi] ?? 0;
                c.Elevation = terrain.Elevation?[gi] ?? float.NaN;
                c.GridIndex = gi;
                c.Deck = terrain.DeckY?[gi] ?? ClassifiedTerrain.NoWater;
                c.DeckFlags = terrain.DeckFlags?[gi] ?? 0;
                c.BKind = terrain.BuildingKind?[gi] ?? 0;
                c.Detail = terrain.BuildingDetail?[gi] ?? 0;
                count++;
                if (k is Surface.Snow or Surface.Glacier) snowy++;
                if (k == Surface.Mud) muddy++;
                if (k is Surface.Grass or Surface.Vineyard) grassy++;
                if (terrain.Elevation is { } el) elevSum += el[gi];
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
                if (c.Deck != ClassifiedTerrain.NoWater) colTop = Math.Max(colTop, c.Deck + 6);

                if (terrain.Road is not null && terrain.Road[gi] != 0)
                {
                    c.Road = terrain.Road[gi];
                    c.RoadFlags = terrain.RoadFlags![gi];
                    if ((RoadMaterial)c.Road == RoadMaterial.Stairs) c.StairFacing = StairFacing(terrain, tx, tz);
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
                    colTop = Math.Max(colTop, c.BuildingFloor + Math.Max(c.BuildingHeight + 4, c.TowerHeight));
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
                if (y > terrain.WorldMaxY) return;
                byte b = tree.Type == TreeType.Spruce
                    ? (isLog ? Blocks.SpruceLog : Blocks.SpruceLeaves)
                    : (isLog ? Blocks.OakLog : Blocks.OakLeaves);
                int key = ((y - terrain.WorldMinY) << 8) | (lz << 4) | lx;
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
                        int key = ((ny - terrain.WorldMinY) << 8) | (lz << 4) | lx;
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
            StructurePlanner.Rasterize(s, terrain.MetresPerBlock, terrain.WorldMaxY - ground, (x, z, dy, block) =>
            {
                int lx = x - cx * 16, lz = z - cz * 16;
                if (lx < 0 || lz < 0 || lx > 15 || lz > 15) return;
                int y = ground + dy;
                if (y > terrain.WorldMaxY) return;
                int key = ((y - terrain.WorldMinY) << 8) | (lz << 4) | lx;
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
                if (lx < 0 || lz < 0 || lx > 15 || lz > 15 || y > terrain.WorldMaxY) return;
                int key = ((y - terrain.WorldMinY) << 8) | (lz << 4) | lx;
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
            if (y > terrain.WorldMaxY) continue;
            int key = ((y - terrain.WorldMinY) << 8) | (lz << 4) | lx;
            overlay ??= new Dictionary<int, byte>(512);
            if (overlay.ContainsKey(key)) continue;
            overlay[key] = (byte)(Blocks.OakSign0 + sign.Rotation);
            (signs ??= []).Add((sign.X, y, sign.Z, sign.Lines));
            if (y > chunkMaxY) chunkMaxY = y;
        }

        // ---- balconies: planks with a fence outside house walls ---------------------------------------
        foreach (var bal in terrain.Balconies.Get(cx, cz))
        {
            int lx = bal.X - cx * 16, lz = bal.Z - cz * 16;
            if (lx < 0 || lz < 0 || lx > 15 || lz > 15) continue;
            ref var bc = ref cols[lz * 16 + lx];
            if (!bc.Present || bal.Y <= bc.Top || bal.Y + 1 > terrain.WorldMaxY) continue;
            overlay ??= new Dictionary<int, byte>(512);
            int k0 = ((bal.Y - terrain.WorldMinY) << 8) | (lz << 4) | lx;
            if (!overlay.ContainsKey(k0)) overlay[k0] = Blocks.Jetty;
            if (!overlay.ContainsKey(k0 + 256)) overlay[k0 + 256] = StructureBlocks.OakFence;
            if (bal.Y + 1 > chunkMaxY) chunkMaxY = bal.Y + 1;
        }

        // ---- walls, fences, jetties and other blocks standing on the ground --------------------------
        if (count > 0 && terrain.Wall is not null)
        {
            for (int i = 0; i < 256; i++)
            {
                ref var c = ref cols[i];
                if (!c.Present || c.Wall == 0 || c.Road != 0 || c.Rail != 0 || c.BuildingHeight != 0) continue;
                int y = (c.Wall == Blocks.Jetty ? c.SurfaceY : c.Top) + 1;
                if (y > terrain.WorldMaxY) continue;
                overlay ??= new Dictionary<int, byte>(512);
                overlay[((y - terrain.WorldMinY) << 8) | i] = c.Wall;
                if (c.Wall == Blocks.DamWall || c.Wall == Blocks.Barrier)
                {
                    // dams and barriers stand two blocks high
                    if (y + 1 <= terrain.WorldMaxY) overlay[((y + 1 - terrain.WorldMinY) << 8) | i] = c.Wall;
                    y++;
                }
                else if (c.Wall == Blocks.StreetLamp)
                {
                    // two fence posts with a lantern on top
                    if (y + 2 <= terrain.WorldMaxY)
                    {
                        overlay[((y + 1 - terrain.WorldMinY) << 8) | i] = Blocks.StreetLamp;
                        overlay[((y + 2 - terrain.WorldMinY) << 8) | i] = Blocks.Lantern;
                        y += 2;
                    }
                }
                if (y > chunkMaxY) chunkMaxY = y;
            }
        }

        // ---- ground cover on grass columns (never on roads, rails, buildings, trunks or nests) --
        if (terrain.Vegetation && count > 0)
        {
            overlay ??= new Dictionary<int, byte>(512);
            for (int i = 0; i < 256; i++)
            {
                ref var c = ref cols[i];
                if (!c.Present || c.Road != 0 || c.Rail != 0 || c.BuildingHeight != 0 || c.Wall != 0) continue;
                // snow layers thickening towards the snow line on open ground
                if (!float.IsNaN(c.Elevation) && c.Elevation >= terrain.SnowLine - 150 && c.Elevation < terrain.SnowLine
                    && (Surface)c.Kind is Surface.Grass or Surface.Vineyard or Surface.Stone or Surface.Gravel)
                {
                    int layers = 1 + (int)Math.Min(6, 7 * (c.Elevation - (terrain.SnowLine - 150)) / 150);
                    int sy = c.Top + 1;
                    int skey = ((sy - terrain.WorldMinY) << 8) | i;
                    if (sy <= terrain.WorldMaxY && !overlay.ContainsKey(skey)) { overlay[skey] = (byte)(Blocks.SnowLayer1 + layers - 1); if (sy > chunkMaxY) chunkMaxY = sy; }
                    continue;
                }
                if (c.TopBlock == Blocks.Farmland && terrain.Crops)
                {
                    int tx2 = cx * 16 + (i & 15), tz2 = cz * 16 + (i >> 4);
                    uint fh = TreePlanner.Hash(tx2 / 40, tz2 / 40, terrain.Seed ^ 0xC207); // one crop kind per field patch
                    int cropPick = (int)(fh % 10);
                    byte crop = cropPick < 6 ? Blocks.Wheat : cropPick < 8 ? Blocks.Potatoes : cropPick < 9 ? Blocks.Carrots : Blocks.Beetroots;
                    int cy = c.Top + 1;
                    if (cy > terrain.WorldMaxY) continue;
                    int ckey = ((cy - terrain.WorldMinY) << 8) | i;
                    if (!overlay.ContainsKey(ckey)) { overlay[ckey] = crop; if (cy > chunkMaxY) chunkMaxY = cy; }
                    continue;
                }
                if (c.TopBlock != 0 && c.TopBlock != Blocks.GrassBlock) continue;
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
                if (y + 1 > terrain.WorldMaxY) continue;
                int key = ((y - terrain.WorldMinY) << 8) | i;
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
        var ores = resources && count > 0 ? OrePlanner.Plan(cx, cz, terrain.Seed, minSection, sectionCount) : null;

        double meanElev = count > 0 && terrain.Elevation is not null ? elevSum / count : 0;
        string biome = count == 0 ? BiomePlains
            : watery * 2 >= count ? (meanElev > 2300 ? BiomeFrozenRiver : BiomeRiver)
            : snowy * 2 >= count ? BiomeSnowy
            : stony * 2 >= count ? BiomeStony
            : muddy * 2 >= count ? BiomeSwamp
            : forested * 2 >= count ? (spruces > oaks ? BiomeTaiga : BiomeForest)
            : grassy * 2 >= count && meanElev > 1000 ? BiomeMeadow
            : BiomePlains;
        bool coarseForest = terrain.CoarseForest;
        bool windows = terrain.MetresPerBlock <= 2;

        // ---- write NBT --------------------------------------------------------------------------
        using var raw = new MemoryStream(32 * 1024);
        var w = new NbtWriter(raw);
        w.BeginCompound("");
        w.WriteInt("DataVersion", DataVersion);
        w.WriteInt("xPos", cx);
        w.WriteInt("yPos", minSection);
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
        w.BeginList("PostProcessing", TagType.List, sectionCount);
        for (int i = 0; i < sectionCount; i++) w.BeginList(null, TagType.Short, 0);
        w.BeginCompound("structures");
        w.BeginCompound("References");
        w.EndCompound();
        w.BeginCompound("starts");
        w.EndCompound();
        w.EndCompound();

        w.BeginList("sections", TagType.Compound, sectionCount);
        Span<byte> blocks = stackalloc byte[4096];
        var palette = terrain.Palette;
        Span<int> paletteIndex = stackalloc int[palette.Names.Length];
        for (int s = 0; s < sectionCount; s++)
        {
            int sy = minSection + s;
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
                            int key = ((y - terrain.WorldMinY) << 8) | i;
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
        if (t.DeckY is { } deck && deck[gi] != ClassifiedTerrain.NoWater) surface = deck[gi];
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
        if (y == WorldMinYStatic) return Blocks.Bedrock;

        // Buildings: walls on the footprint edge, a floor slab and a roof; storeys, doors, ladders,
        // chimneys and cellars for houses; special shapes per building type.
        if (c.BuildingHeight != 0)
        {
            byte b = BuildingBlock(y, in c, windows, i);
            if (b != FallThrough) return b;
        }

        int surface = c.SurfaceY;
        bool onDeck = c.Deck != ClassifiedTerrain.NoWater;
        bool bridge = onDeck && (c.DeckFlags & (DeckFlag.Bridge | DeckFlag.Platform)) != 0;
        bool tunnel = onDeck && (c.DeckFlags & DeckFlag.Tunnel) != 0;
        bool track = (c.Rail & RailFlag.Track) != 0;

        if (bridge)
        {
            bool covered = (c.DeckFlags & DeckFlag.Covered) != 0, railing = (c.DeckFlags & DeckFlag.Railing) != 0;
            if (covered)
            {
                if (y == surface + 4) return Blocks.RoofPlanks;
                if (railing && y > surface && y < surface + 4) return Blocks.Jetty;
            }
            else if (railing && y == surface + 1 && !track) return Blocks.Barrier;
            if (y == surface - 1) return c.Road != 0 ? Blocks.RoadBlock((RoadMaterial)c.Road) : Blocks.DamWall;
            if (y < surface - 1 && y > c.Top && (c.WaterY == ClassifiedTerrain.NoWater || y > c.WaterY))
                return (c.DeckFlags & DeckFlag.Pier) != 0 ? Blocks.DamWall : Blocks.Air;
            if (c.Road == 0 && c.Rail == 0 && y == surface) return c.TopBlock != 0 ? c.TopBlock : Blocks.Platform;
        }
        if (tunnel)
        {
            int floorY = surface + (track ? 1 : 0);
            if (y > floorY && y <= floorY + TunnelClearance) return Blocks.Air;
            if (y == floorY + TunnelClearance + 1 && (c.DeckFlags & DeckFlag.Light) != 0) return Blocks.Glowstone;
        }

        // Railways: gravel bed with the rail on top; over water a floating deck.
        if (c.Rail != 0)
        {
            if (track && y == surface + 1) return c.RailShape;
            if (y == surface)
            {
                if (c.Road != 0) return RoadSurface(in c); // level crossing / tram in the street
                return Blocks.RailBed;
            }
            if (c.OverWater && y > c.Top && y <= c.WaterY && y < surface) return Blocks.Water;
            if (c.OverWater && y > c.WaterY && y < surface) return Blocks.Air;
        }

        // Roads: two blocks thick on land, a deck one block above the water on bridges/fords.
        if (c.Road != 0)
        {
            if (y == surface) return RoadSurface(in c);
            if (!c.OverWater && y == surface - 1) return Blocks.RoadBlock((RoadMaterial)c.Road);
            if (c.OverWater && y > c.Top && y <= c.WaterY && y < surface) return Blocks.Water;
            if (c.OverWater && y > c.WaterY && y < surface) return Blocks.Air;
        }

        return TerrainBlock(y, in c, coarseForest);
    }

    private const byte FallThrough = 255;

    private static byte RoadSurface(in Column c)
    {
        if ((RoadMaterial)c.Road == RoadMaterial.Stairs) return (byte)(Blocks.Stairs0 + c.StairFacing);
        return (c.RoadFlags & RoadFlag.Marking) != 0 ? Blocks.RoadMarking : Blocks.RoadTopBlock((RoadMaterial)c.Road);
    }

    /// <summary>Facing of outdoor stairs: towards the highest neighbouring column.</summary>
    private static byte StairFacing(ClassifiedTerrain t, int tx, int tz)
    {
        int here = t.TopY[tz * t.Width + tx];
        ReadOnlySpan<int> dx = [0, 1, 0, -1];
        ReadOnlySpan<int> dz = [-1, 0, 1, 0];
        int best = 0, bestDiff = int.MinValue;
        for (int d = 0; d < 4; d++)
        {
            int nx = tx + dx[d], nz = tz + dz[d];
            if (nx < 0 || nz < 0 || nx >= t.Width || nz >= t.Height) continue;
            int diff = t.TopY[nz * t.Width + nx] - here;
            if (diff > bestDiff) { bestDiff = diff; best = d; }
        }
        return (byte)best;
    }

    /// <summary>Block of a building column at y, or FallThrough where the terrain takes over.</summary>
    private static byte BuildingBlock(int y, in Column c, bool windows, int i)
    {
        int floor = c.BuildingFloor, top = floor + c.BuildingHeight, roof = top + 1;
        bool edge = (c.BuildingFlags & BuildingFlag.Edge) != 0;
        bool industrial = (c.BuildingFlags & BuildingFlag.Industrial) != 0;
        bool church = (c.BuildingFlags & BuildingFlag.Church) != 0;
        var kind = (BuildingKind)c.BKind;
        int dir = (c.Detail >> BuildingDetail.DirShift) & 3;
        int storey = StoreyBlocks;
        int parity = (i & 15) + (i >> 4);
        bool cellar = (c.Detail & BuildingDetail.Cellar) != 0 && floor - 4 > WorldMinYStatic + 1;
        bool detailed = kind is BuildingKind.House or BuildingKind.HighRise && !church;

        byte wall = church ? Blocks.ChurchWall : kind switch
        {
            BuildingKind.Tank => Blocks.IronBlock,
            BuildingKind.Reservoir or BuildingKind.BigWall or BuildingKind.Tower => Blocks.StoneBricks,
            BuildingKind.Greenhouse or BuildingKind.Walkway => Blocks.Glass,
            BuildingKind.Construction => StructureBlocks.OakFence,
            BuildingKind.Chimney => Blocks.Bricks,
            BuildingKind.HighRise => Blocks.WallLight,
            BuildingKind.Observatory => Blocks.WhiteRoof,
            BuildingKind.CarPark => Blocks.GrayConcrete,
            BuildingKind.Stadium => Blocks.WhiteConcrete,
            BuildingKind.Open => Blocks.Platform,
            _ => (c.BuildingFlags >> 4) switch { 1 => Blocks.WallLight, 2 => Blocks.SmoothSandstone, _ => Blocks.WhiteConcrete },
        };
        byte roofBlock = kind switch
        {
            BuildingKind.Open or BuildingKind.Walkway => Blocks.LightRoof,
            BuildingKind.Tank => Blocks.IronBlock,
            BuildingKind.Greenhouse => Blocks.Glass,
            BuildingKind.Construction or BuildingKind.Chimney => Blocks.Air,
            BuildingKind.Tower => church ? StructureBlocks.DeepslateTiles : Blocks.Platform,
            BuildingKind.HighRise or BuildingKind.CarPark => Blocks.IndustrialRoof,
            BuildingKind.Observatory => Blocks.WhiteRoof,
            BuildingKind.BigWall => Blocks.StoneBricks,
            BuildingKind.Reservoir => Blocks.GrassBlock,
            BuildingKind.Stadium => Blocks.WhiteConcrete,
            _ => c.RoofBlock != 0 ? c.RoofBlock : church ? StructureBlocks.DeepslateTiles : industrial ? Blocks.IndustrialRoof : Blocks.Bricks,
        };

        if (c.TowerHeight != 0)
        {
            // church tower: solid stone-brick shaft up to the tower top, spire comes from the overlay
            int towerTop = floor + c.TowerHeight;
            if (y > towerTop) return Blocks.Air;
            if (y > c.Top)
            {
                bool slit = windows && y > floor + 2 && y < towerTop - 1 && (y - floor) % 4 == 0 && (parity & 1) == 0;
                return slit ? Blocks.Glass : wall;
            }
            return FallThrough;
        }

        // chimney stack above the roof
        if ((c.Detail & BuildingDetail.Chimney) != 0 && y > roof && y <= roof + 2) return Blocks.Bricks;
        if (y == roof)
        {
            if (kind == BuildingKind.Stadium && !edge) return Blocks.Air;
            return roofBlock;
        }
        if (y > roof)
        {
            // viewing platform railing on towers
            if (kind == BuildingKind.Tower && !church && edge && y == roof + 1) return StructureBlocks.OakFence;
            return Blocks.Air;
        }

        if (edge)
        {
            if (y > c.Top)
            {
                if ((c.Detail & BuildingDetail.Door) != 0 && y == floor + 1) return (byte)(Blocks.DoorLower0 + dir);
                if ((c.Detail & BuildingDetail.Door) != 0 && y == floor + 2) return (byte)(Blocks.DoorUpper0 + dir);
                switch (kind)
                {
                    case BuildingKind.Open: return parity % 3 == 0 ? wall : Blocks.Air;
                    case BuildingKind.CarPark: return parity % 2 == 0 || (y - floor) % storey == 0 ? wall : Blocks.Air;
                    case BuildingKind.Construction:
                    case BuildingKind.Tank:
                    case BuildingKind.Chimney:
                    case BuildingKind.Greenhouse:
                    case BuildingKind.BigWall:
                    case BuildingKind.Reservoir:
                        return wall;
                }
                bool windowRow = windows && y > floor + 1 && y < top && (y - floor - 2) % 3 == 0 && (parity & 1) == 0;
                return windowRow ? Blocks.Glass : wall;
            }
            if (cellar && y >= floor - 4 && y < floor) return Blocks.Cobblestone;
            return FallThrough;
        }

        // interior
        bool ladder = (c.Detail & BuildingDetail.Ladder) != 0;
        int ladderBottom = cellar ? floor - 4 : floor;
        if (ladder && y > ladderBottom && y < top) return (byte)(Blocks.Ladder0 + ((dir + 2) & 3));
        if (kind is BuildingKind.Open or BuildingKind.Tank or BuildingKind.Chimney or BuildingKind.Greenhouse or BuildingKind.Reservoir)
        {
            // no floor slab and no base: the ground shows through
            return y > c.Top ? Blocks.Air : FallThrough;
        }
        if (kind == BuildingKind.BigWall) return y > c.Top ? Blocks.StoneBricks : FallThrough;
        if (y == floor) return kind == BuildingKind.Stadium ? Blocks.GrassBlock : Blocks.SmoothStone;
        if (y > floor)
        {
            bool storeys = detailed || kind is BuildingKind.Tower or BuildingKind.CarPark or BuildingKind.Observatory;
            if (storeys && (y - floor) % storey == 0 && y < top - 1) return kind == BuildingKind.CarPark ? Blocks.Platform : Blocks.SmoothStone;
            return Blocks.Air;
        }
        if (cellar)
        {
            if (y == floor - 4) return Blocks.SmoothStone;
            if (y > floor - 4) return Blocks.Air;
        }
        if (kind == BuildingKind.Walkway) return y > c.Top ? Blocks.Air : FallThrough;
        if (y > c.Top) return wall; // base under the floor on sloping ground
        return FallThrough;
    }

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
            if (c.TopBlock != 0 && kind is not (Surface.Water or Surface.Glacier or Surface.Snow)) return c.TopBlock;
            return kind switch
            {
                Surface.Grass => coarseForest && c.Forest ? Blocks.MossBlock : Blocks.GrassBlock,
                Surface.Stone => RockBlock(in c, y),
                Surface.Snow => Blocks.SnowBlock,
                Surface.Sand => Blocks.Sand,
                Surface.Water => (c.Bed & 1) != 0 ? Blocks.Clay : Blocks.Sand,
                Surface.Gravel => Blocks.Gravel,
                Surface.Glacier => Blocks.SnowBlock,
                Surface.Mud => Blocks.Mud,
                Surface.Vineyard => Blocks.GrassBlock,
                _ => RockBlock(in c, y),
            };
        }
        if (kind == Surface.Glacier && c.IceBase != ClassifiedTerrain.NoWater)
        {
            // ice from the surface down to the modelled bed; blue ice deep inside thick glaciers
            if (y > c.IceBase) return top - y > 40 ? Blocks.BlueIce : Blocks.PackedIce;
            return y < 0 ? Blocks.Deepslate : RockBlock(in c, y);
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
                _ => RockBlock(in c, y),
            };
        }
        return y < 0 ? Blocks.Deepslate : RockBlock(in c, y);
    }

    /// <summary>Bedrock or, in the top layers, the unconsolidated deposit mapped over it; below a modelled formation top, that formation.</summary>
    private static byte RockBlock(in Column c, int y)
    {
        if (c.Deposit != 0 && y > c.Top - DepositBlocks) return Blocks.GeologyBlock(c.Deposit);
        if (Horizons is { } hz)
        {
            // the unit at y is the one whose top is the lowest top still at or above y
            int bestY = int.MaxValue;
            byte cls = 0;
            foreach (var (hClass, hY) in hz)
            {
                short top = hY[c.GridIndex];
                if (top == ClassifiedTerrain.NoWater || top < y || top >= bestY) continue;
                bestY = top;
                cls = hClass;
            }
            if (cls != 0) return Blocks.GeologyBlock(cls);
        }
        return Blocks.GeologyBlock(c.Geology);
    }

    [ThreadStatic] private static int DepositBlocks;
    [ThreadStatic] private static int WorldMinYStatic;
    [ThreadStatic] private static int StoreyBlocks;
    [ThreadStatic] private static int TunnelClearance;
    [ThreadStatic] private static (byte Class, short[] Y)[]? Horizons;
}
