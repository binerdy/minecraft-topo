namespace MinecraftTopo.Core.Anvil;

/// <summary>Approximate top-face colours of the blocks the generator writes, for maps and icons.</summary>
public static class BlockColours
{
    private static readonly Dictionary<string, (byte R, byte G, byte B)> Colours = new()
    {
        ["minecraft:grass_block"] = (91, 161, 57), ["minecraft:dirt"] = (134, 96, 67), ["minecraft:stone"] = (125, 125, 125),
        ["minecraft:sand"] = (219, 207, 163), ["minecraft:snow_block"] = (245, 248, 250), ["minecraft:water"] = (63, 118, 228),
        ["minecraft:gravel"] = (136, 126, 126), ["minecraft:clay"] = (160, 166, 179), ["minecraft:seagrass"] = (50, 110, 180),
        ["minecraft:oak_leaves"] = (46, 106, 36), ["minecraft:spruce_leaves"] = (36, 78, 40), ["minecraft:oak_log"] = (100, 80, 50),
        ["minecraft:spruce_log"] = (60, 40, 25), ["minecraft:short_grass"] = (100, 170, 60), ["minecraft:tall_grass"] = (95, 165, 55),
        ["minecraft:fern"] = (70, 130, 50), ["minecraft:dandelion"] = (240, 220, 60), ["minecraft:poppy"] = (220, 50, 40),
        ["minecraft:cornflower"] = (70, 90, 220), ["minecraft:oxeye_daisy"] = (240, 240, 230), ["minecraft:azure_bluet"] = (220, 225, 240),
        ["minecraft:bee_nest"] = (200, 160, 60), ["minecraft:sweet_berry_bush"] = (120, 40, 60), ["minecraft:pumpkin"] = (230, 130, 30),
        ["minecraft:brown_mushroom"] = (150, 110, 80), ["minecraft:red_mushroom"] = (200, 40, 40), ["minecraft:moss_block"] = (90, 130, 50),
        ["minecraft:gray_concrete"] = (55, 58, 62), ["minecraft:light_gray_concrete"] = (125, 125, 115), ["minecraft:white_concrete"] = (235, 238, 240),
        ["minecraft:cobblestone"] = (120, 118, 115), ["minecraft:stone_bricks"] = (130, 128, 125), ["minecraft:dirt_path"] = (150, 125, 80),
        ["minecraft:rail"] = (110, 90, 70), ["minecraft:bricks"] = (150, 85, 70), ["minecraft:smooth_sandstone"] = (220, 205, 160),
        ["minecraft:smooth_stone"] = (160, 160, 160), ["minecraft:glass"] = (200, 225, 235),
        ["minecraft:iron_bars"] = (90, 90, 95), ["minecraft:oak_fence"] = (120, 95, 60), ["minecraft:chain"] = (60, 60, 65), ["minecraft:smooth_quartz"] = (236, 233, 226), ["minecraft:deepslate_tiles"] = (60, 60, 65), ["minecraft:oak_sign"] = (170, 135, 80),
        ["minecraft:mud"] = (60, 57, 60), ["minecraft:packed_ice"] = (150, 190, 235), ["minecraft:azalea"] = (90, 140, 60), ["minecraft:deepslate"] = (80, 80, 85),
        ["minecraft:tuff"] = (108, 110, 100), ["minecraft:sandstone"] = (216, 203, 155), ["minecraft:packed_mud"] = (142, 106, 80), ["minecraft:diorite"] = (188, 188, 190),
        ["minecraft:terracotta"] = (152, 94, 68), ["minecraft:white_terracotta"] = (210, 178, 161), ["minecraft:granite"] = (150, 103, 86), ["minecraft:polished_granite"] = (155, 108, 90),
        ["minecraft:basalt"] = (80, 80, 85), ["minecraft:polished_tuff"] = (98, 102, 95), ["minecraft:calcite"] = (223, 224, 220), ["minecraft:polished_andesite"] = (132, 135, 134),
        ["minecraft:polished_deepslate"] = (72, 72, 74), ["minecraft:andesite"] = (136, 136, 137), ["minecraft:cobbled_deepslate"] = (77, 77, 80), ["minecraft:blackstone"] = (42, 36, 41), ["minecraft:polished_blackstone"] = (53, 48, 56),
        ["minecraft:farmland"] = (120, 80, 50), ["minecraft:coarse_dirt"] = (119, 85, 59), ["minecraft:cobblestone_wall"] = (110, 110, 110), ["minecraft:lantern"] = (240, 200, 90),
        ["minecraft:red_terracotta"] = (143, 61, 47), ["minecraft:oak_planks"] = (162, 130, 78), ["minecraft:chiseled_stone_bricks"] = (120, 120, 120), ["minecraft:podzol"] = (90, 63, 30), ["minecraft:blue_ice"] = (116, 168, 253),
        ["minecraft:wheat"] = (200, 180, 90), ["minecraft:carrots"] = (120, 160, 70), ["minecraft:potatoes"] = (110, 150, 70), ["minecraft:beetroots"] = (140, 90, 90), ["minecraft:snow"] = (240, 244, 248),
    };

    public static (byte R, byte G, byte B) Of(string name)
    {
        if (Colours.TryGetValue(name, out var c)) return c;
        if (name.Contains("_ore")) return (200, 200, 90);
        string bare = name.Replace("minecraft:", "");
        foreach (var p in Blocks.ColourPalette) if (p.Name == bare) return (p.R, p.G, p.B);
        return (255, 0, 255);
    }

    public static bool Known(string name) => Colours.ContainsKey(name);
}
