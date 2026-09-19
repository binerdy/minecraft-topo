using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Anvil;

/// <summary>A user-selectable block role: one generator block id with a default vanilla block.</summary>
public sealed record BlockRole(string Key, string Label, string Group, byte Id, string Default);

/// <summary>Every block role the user may override, grouped as they appear in the UI.</summary>
public static class BlockRoles
{
    public const string Terrain = "terrain", Trees = "trees", Roads = "roads", Rails = "rails",
        Buildings = "buildings", Power = "power", Geology = "geology", Extras = "extras";

    public static readonly IReadOnlyList<BlockRole> All =
    [
        new("grass", "Grass surface", Terrain, Blocks.GrassBlock, "grass_block"),
        new("soil", "Soil under grass", Terrain, Blocks.Dirt, "dirt"),
        new("stone", "Rock (steep slopes and underground when geology is off)", Terrain, Blocks.Stone, "stone"),
        new("deepslate", "Deep underground (below Y 0)", Terrain, Blocks.Deepslate, "deepslate"),
        new("snow", "Snow and glacier surface", Terrain, Blocks.SnowBlock, "snow_block"),
        new("ice", "Glacier ice", Terrain, Blocks.PackedIce, "packed_ice"),
        new("sand", "Beaches and shallow beds", Terrain, Blocks.Sand, "sand"),
        new("scree", "Scree", Terrain, Blocks.Gravel, "gravel"),
        new("mud", "Wetlands", Terrain, Blocks.Mud, "mud"),
        new("clay", "Clay on lake and river beds", Terrain, Blocks.Clay, "clay"),
        new("forest_floor", "Forest floor at coarse scales", Terrain, Blocks.MossBlock, "moss_block"),
        new("water", "Water", Terrain, Blocks.Water, "water"),
        new("bedrock", "Bottom layer", Terrain, Blocks.Bedrock, "bedrock"),
        new("blue_ice", "Deep glacier ice (more than 40 blocks down)", Terrain, Blocks.BlueIce, "blue_ice"),
        new("farmland", "Cropland and allotments", Terrain, Blocks.Farmland, "farmland"),
        new("coarse_dirt", "Bare soil, landfills", Terrain, Blocks.CoarseDirt, "coarse_dirt"),
        new("podzol", "Cemeteries and dark ground", Terrain, Blocks.Podzol, "podzol"),

        new("wall", "Walls and dry-stone walls", Extras, Blocks.CobblestoneWall, "cobblestone_wall"),
        new("dam", "Dams, weirs and basin rims", Extras, Blocks.DamWall, "stone_bricks"),
        new("barrier", "Avalanche and rockfall barriers", Extras, Blocks.Barrier, "iron_bars"),
        new("bank_wall", "River bank revetments", Extras, Blocks.BankWall, "stone_bricks"),
        new("runway", "Runways and taxiways", Extras, Blocks.Runway, "gray_concrete"),
        new("platform", "Station platforms", Extras, Blocks.Platform, "smooth_stone"),
        new("parking", "Car parks and rest areas", Extras, Blocks.Parking, "light_gray_concrete"),
        new("lift_mast", "Cable car and ski lift masts", Extras, Blocks.LiftMast, "iron_bars"),
        new("running_track", "Running tracks", Extras, Blocks.RunningTrack, "red_terracotta"),
        new("sports_line", "Sports field lines", Extras, Blocks.SportsLine, "white_concrete"),
        new("jetty", "Jetties and piers", Extras, Blocks.Jetty, "oak_planks"),
        new("monument", "Monuments", Extras, Blocks.Monument, "chiseled_stone_bricks"),
        new("lantern", "Wayside shrine lanterns", Extras, Blocks.Lantern, "lantern"),
        new("quarry", "Quarries and gravel pits", Extras, Blocks.Quarry, "stone"),
        new("lamp_post", "Street lamp posts", Extras, Blocks.StreetLamp, "oak_fence"),
        new("tunnel_light", "Tunnel ceiling lights and mast lights", Extras, Blocks.Glowstone, "glowstone"),
        new("bridge_roof", "Covered bridge roofs", Extras, Blocks.RoofPlanks, "spruce_planks"),

        new("oak_log", "Oak trunk (below 1300 m)", Trees, Blocks.OakLog, "oak_log"),
        new("oak_leaves", "Oak leaves", Trees, Blocks.OakLeaves, "oak_leaves"),
        new("spruce_log", "Spruce trunk (above 1300 m)", Trees, Blocks.SpruceLog, "spruce_log"),
        new("spruce_leaves", "Spruce leaves", Trees, Blocks.SpruceLeaves, "spruce_leaves"),

        new("asphalt", "Motorways and main roads", Roads, Blocks.GrayConcrete, "gray_concrete"),
        new("asphalt_light", "Minor roads and streets", Roads, Blocks.LightGrayConcrete, "light_gray_concrete"),
        new("marking", "Lane markings", Roads, Blocks.RoadMarking, "white_concrete"),
        new("dirt_path", "Paths", Roads, Blocks.DirtPathBlock, "dirt_path"),
        new("road_gravel", "Gravel tracks", Roads, Blocks.RoadGravel, "gravel"),
        new("cobblestone", "Cobbled streets", Roads, Blocks.Cobblestone, "cobblestone"),
        new("paved", "Paved streets", Roads, Blocks.StoneBricks, "stone_bricks"),

        new("rail_bed", "Railway bed", Rails, Blocks.RailBed, "gravel"),

        new("wall", "House walls", Buildings, Blocks.WhiteConcrete, "white_concrete"),
        new("wall_light", "House walls, second style", Buildings, Blocks.WallLight, "light_gray_concrete"),
        new("wall_sandstone", "House walls, third style", Buildings, Blocks.SmoothSandstone, "smooth_sandstone"),
        new("roof", "House roofs", Buildings, Blocks.Bricks, "bricks"),
        new("roof_industrial", "Industrial roofs", Buildings, Blocks.IndustrialRoof, "gray_concrete"),
        new("floor", "Floors", Buildings, Blocks.SmoothStone, "smooth_stone"),
        new("window", "Windows", Buildings, Blocks.Glass, "glass"),
        new("church_wall", "Church walls", Buildings, Blocks.ChurchWall, "stone_bricks"),
        new("church_roof", "Church roofs and spires", Buildings, StructureBlocks.DeepslateTiles, "deepslate_tiles"),
        new("roof_dark", "Dark roofs seen in the orthophoto", Buildings, Blocks.DarkRoof, "deepslate_tiles"),
        new("roof_light", "Grey roofs seen in the orthophoto", Buildings, Blocks.LightRoof, "light_gray_concrete"),
        new("roof_white", "White roofs seen in the orthophoto", Buildings, Blocks.WhiteRoof, "smooth_quartz"),
        new("tank", "Storage tanks", Buildings, Blocks.IronBlock, "iron_block"),

        new("pylon", "Pylons", Power, StructureBlocks.IronBars, "iron_bars"),
        new("pole", "Wooden poles", Power, StructureBlocks.OakFence, "oak_fence"),
        new("turbine", "Wind turbines", Power, StructureBlocks.SmoothQuartz, "smooth_quartz"),

        // GK500 lithology main groups, in legend order.
        new("geo1", "Clays, silts, sands (unconsolidated)", Geology, Blocks.GeologyBase + 0, "clay"),
        new("geo2", "Sands, gravels, boulders (unconsolidated)", Geology, Blocks.GeologyBase + 1, "gravel"),
        new("geo3", "Blocks and boulders (moraines)", Geology, Blocks.GeologyBase + 2, "cobblestone"),
        new("geo4", "Marls, partly sandstone", Geology, Blocks.GeologyBase + 3, "tuff"),
        new("geo5", "Sandstones, partly marl", Geology, Blocks.GeologyBase + 4, "sandstone"),
        new("geo6", "Conglomerates, breccias", Geology, Blocks.GeologyBase + 5, "packed_mud"),
        new("geo7", "Claystones, shales", Geology, Blocks.GeologyBase + 6, "deepslate"),
        new("geo8", "Limestones, partly marl", Geology, Blocks.GeologyBase + 7, "stone"),
        new("geo9", "Dolomites", Geology, Blocks.GeologyBase + 8, "diorite"),
        new("geo10", "Radiolarites, cherts", Geology, Blocks.GeologyBase + 9, "terracotta"),
        new("geo11", "Clays, evaporites, rauhwacke", Geology, Blocks.GeologyBase + 10, "white_terracotta"),
        new("geo12", "Granites, syenites", Geology, Blocks.GeologyBase + 11, "granite"),
        new("geo13", "Porphyrites, quartz porphyries", Geology, Blocks.GeologyBase + 12, "polished_granite"),
        new("geo14", "Volcanic rocks", Geology, Blocks.GeologyBase + 13, "basalt"),
        new("geo15", "Marl shales, calc-phyllites", Geology, Blocks.GeologyBase + 14, "polished_tuff"),
        new("geo16", "Dolomite and limestone marbles", Geology, Blocks.GeologyBase + 15, "calcite"),
        new("geo17", "Quartzites", Geology, Blocks.GeologyBase + 16, "smooth_quartz"),
        new("geo18", "Quartz phyllites", Geology, Blocks.GeologyBase + 17, "polished_andesite"),
        new("geo19", "Mica schists, gneisses", Geology, Blocks.GeologyBase + 18, "polished_deepslate"),
        new("geo20", "Gneisses", Geology, Blocks.GeologyBase + 19, "andesite"),
        new("geo21", "Amphibolites", Geology, Blocks.GeologyBase + 20, "cobbled_deepslate"),
        new("geo22", "Basic rocks (gabbros, diabases)", Geology, Blocks.GeologyBase + 21, "blackstone"),
        new("geo23", "Ultrabasic rocks (serpentinites)", Geology, Blocks.GeologyBase + 22, "polished_blackstone"),
    ];

    private static readonly string[] Colours =
    [
        "white", "light_gray", "gray", "black", "brown", "red", "orange", "yellow", "lime", "green", "cyan",
        "light_blue", "blue", "purple", "magenta", "pink",
    ];

    /// <summary>Vanilla blocks offered for every role (full blocks plus the few non-cubes used by default).</summary>
    public static readonly IReadOnlyList<string> Choices = BuildChoices();

    private static string[] BuildChoices()
    {
        var list = new List<string>
        {
            "stone", "cobblestone", "mossy_cobblestone", "smooth_stone", "stone_bricks", "mossy_stone_bricks", "cracked_stone_bricks", "chiseled_stone_bricks",
            "granite", "polished_granite", "diorite", "polished_diorite", "andesite", "polished_andesite",
            "deepslate", "cobbled_deepslate", "polished_deepslate", "deepslate_bricks", "deepslate_tiles", "chiseled_deepslate",
            "tuff", "polished_tuff", "tuff_bricks", "chiseled_tuff", "calcite", "dripstone_block", "basalt", "smooth_basalt", "polished_basalt",
            "blackstone", "polished_blackstone", "polished_blackstone_bricks", "obsidian", "crying_obsidian", "netherrack", "end_stone", "end_stone_bricks",
            "sandstone", "smooth_sandstone", "cut_sandstone", "chiseled_sandstone", "red_sandstone", "smooth_red_sandstone", "cut_red_sandstone",
            "bricks", "mud_bricks", "packed_mud", "mud", "clay", "terracotta",
            "quartz_block", "smooth_quartz", "quartz_bricks", "chiseled_quartz_block", "quartz_pillar",
            "prismarine", "prismarine_bricks", "dark_prismarine", "sea_lantern", "glowstone", "shroomlight", "ochre_froglight", "verdant_froglight", "pearlescent_froglight",
            "glass", "tinted_glass", "iron_block", "gold_block", "copper_block", "exposed_copper", "weathered_copper", "oxidized_copper",
            "cut_copper", "exposed_cut_copper", "weathered_cut_copper", "oxidized_cut_copper", "coal_block", "lapis_block", "emerald_block", "diamond_block",
            "netherite_block", "amethyst_block", "raw_iron_block", "raw_copper_block", "raw_gold_block", "redstone_block",
            "dirt", "coarse_dirt", "rooted_dirt", "dirt_path", "grass_block", "podzol", "mycelium", "moss_block", "pale_moss_block", "gravel", "sand", "red_sand",
            "snow_block", "powder_snow", "packed_ice", "blue_ice", "ice", "water", "lava", "bedrock",
            "hay_block", "bone_block", "honeycomb_block", "honey_block", "slime_block", "sponge", "wet_sponge", "sculk", "magma_block",
            "soul_sand", "soul_soil", "warped_nylium", "crimson_nylium", "nether_bricks", "red_nether_bricks", "purpur_block", "bookshelf", "note_block",
            "iron_bars", "oak_fence", "spruce_fence", "birch_fence", "dark_oak_fence", "nether_brick_fence",
            "cobblestone_wall", "mossy_cobblestone_wall", "stone_brick_wall", "andesite_wall", "granite_wall", "diorite_wall", "deepslate_brick_wall",
            "lantern", "soul_lantern", "farmland", "chiseled_stone_bricks", "chiseled_deepslate", "chiseled_sandstone",
        };
        foreach (var w in new[] { "oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "cherry", "pale_oak", "bamboo", "crimson", "warped" })
        {
            list.Add(w is "bamboo" ? "bamboo_block" : w is "crimson" or "warped" ? $"{w}_stem" : $"{w}_log");
            list.Add(w is "bamboo" ? "stripped_bamboo_block" : w is "crimson" or "warped" ? $"stripped_{w}_stem" : $"stripped_{w}_log");
            if (w is not ("bamboo" or "crimson" or "warped")) list.Add($"{w}_wood");
            list.Add($"{w}_planks");
        }
        foreach (var l in new[] { "oak", "spruce", "birch", "jungle", "acacia", "dark_oak", "mangrove", "cherry", "pale_oak", "azalea", "flowering_azalea" })
            list.Add($"{l}_leaves");
        foreach (var c in Colours)
        {
            list.Add($"{c}_concrete");
            list.Add($"{c}_concrete_powder");
            list.Add($"{c}_terracotta");
            list.Add($"{c}_glazed_terracotta");
            list.Add($"{c}_wool");
            list.Add($"{c}_stained_glass");
        }
        return list.Distinct().ToArray();
    }

    private static readonly HashSet<string> ChoiceSet = new(Choices);

    public static BlockRole? Find(string key) => All.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Normalises a block id ("Stone", "minecraft:stone") to its bare vanilla name, or null when unknown.</summary>
    public static string? Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string n = name.Trim().ToLowerInvariant();
        if (n.StartsWith("minecraft:")) n = n["minecraft:".Length..];
        return ChoiceSet.Contains(n) ? n : null;
    }
}

/// <summary>The block names (and state properties) behind every generator block id for one generation.</summary>
public sealed class BlockPalette
{
    public string[] Names { get; }
    public IReadOnlyDictionary<byte, (string Key, string Value)[]> Properties { get; }
    /// <summary>Role overrides that were applied (role key -> bare block name).</summary>
    public IReadOnlyDictionary<string, string> Overrides { get; }

    private BlockPalette(string[] names, Dictionary<byte, (string, string)[]> props, Dictionary<string, string> overrides)
    {
        Names = names;
        Properties = props;
        Overrides = overrides;
    }

    public static readonly BlockPalette Default = Create(null, out _);

    /// <summary>
    /// Builds the palette for the given role overrides. Unknown roles or block names are skipped
    /// and reported in <paramref name="warnings"/>.
    /// </summary>
    public static BlockPalette Create(IReadOnlyDictionary<string, string>? overrides, out List<string> warnings)
    {
        warnings = [];
        var names = new string[Blocks.Names.Length + Blocks.ExtraNames.Length];
        if (names.Length != Blocks.LastId + 1) throw new InvalidOperationException($"Block name table has {names.Length} entries but the last id is {Blocks.LastId}.");
        Blocks.Names.CopyTo(names, 0);
        Blocks.ExtraNames.CopyTo(names, Blocks.Names.Length);
        var props = new Dictionary<byte, (string, string)[]>();
        foreach (var kv in Blocks.Properties) props[kv.Key] = kv.Value;
        foreach (var kv in Blocks.ExtraProperties) props[kv.Key] = kv.Value;
        var applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                var role = BlockRoles.Find(key);
                if (role is null) { warnings.Add($"Unknown block role '{key}' ignored."); continue; }
                var block = BlockRoles.Normalise(value);
                if (block is null) { warnings.Add($"Unknown block '{value}' for {role.Label} ignored."); continue; }
                if (block == role.Default) continue;
                names[role.Id] = "minecraft:" + block;
                // Leaves decay unless persistent; every other selectable block has no required state.
                if (block.EndsWith("_leaves", StringComparison.Ordinal)) props[role.Id] = [("persistent", "true")];
                else props.Remove(role.Id);
                applied[role.Key] = block;
            }
        }
        return new BlockPalette(names, props, applied);
    }
}
