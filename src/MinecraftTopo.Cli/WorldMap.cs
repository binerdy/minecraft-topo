using System.Buffers.Binary;
using System.IO.Compression;
using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Nbt;

/// <summary>Renders a top-down picture of a generated world (highest non-air block per column) for checking results.</summary>
public static class WorldMap
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
    };

    /// <param name="crop">Optional block-coordinate window x0,z0,x1,z1 rendered at <paramref name="scale"/> pixels per block.</param>
    public static void Render(string worldDir, string outPng, (int X0, int Z0, int X1, int Z1)? crop = null, int scale = 1)
    {
        string regionDir = WorldWriter.OverworldRegionDir(worldDir);
        var files = Directory.GetFiles(regionDir, "r.*.mca");
        if (files.Length == 0) throw new InvalidOperationException("No region files found in " + regionDir);

        var regions = files.Select(f =>
        {
            var p = Path.GetFileNameWithoutExtension(f).Split('.');
            return (Path: f, Rx: int.Parse(p[1]), Rz: int.Parse(p[2]));
        }).ToList();
        int minRx = regions.Min(r => r.Rx), maxRx = regions.Max(r => r.Rx), minRz = regions.Min(r => r.Rz), maxRz = regions.Max(r => r.Rz);
        int width = (maxRx - minRx + 1) * 512, height = (maxRz - minRz + 1) * 512;
        if ((long)width * height > 64_000_000) throw new InvalidOperationException("World too large to render in one image.");
        var rgba = new byte[width * height * 4];
        var heights = new short[width * height];
        var histogram = new Dictionary<string, int>();

        foreach (var region in regions)
        {
            using var fs = File.OpenRead(region.Path);
            var header = new byte[8192];
            fs.ReadExactly(header);
            for (int i = 0; i < 1024; i++)
            {
                int loc = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(i * 4));
                if (loc == 0) continue;
                fs.Position = (long)(loc >> 8) * 4096;
                var lb = new byte[5];
                fs.ReadExactly(lb);
                var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(lb) - 1];
                fs.ReadExactly(payload);
                using var zs = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress);
                var root = new NbtReader(zs).ReadRoot();
                int cx = (int)root["xPos"]!.Value!, cz = (int)root["zPos"]!.Value!;
                int ox = (cx - minRx * 32) * 16, oz = (cz - minRz * 32) * 16;
                var topName = new string?[256];
                var topY = new short[256];
                foreach (var section in root["sections"]!.Children)
                {
                    var states = section["block_states"];
                    if (states is null) continue;
                    var palette = states["palette"]!.Children.Select(c => c.Type == TagType.String ? (string)c.Value! : (string)c["id"]!.Value!).ToArray();
                    if (palette.Length == 1 && palette[0] == "minecraft:air") continue;
                    int sy = (sbyte)section["Y"]!.Value!;
                    var data = states["data"]?.Value as long[];
                    int bits = Math.Max(4, System.Numerics.BitOperations.Log2((uint)Math.Max(1, palette.Length - 1)) + 1);
                    int perLong = 64 / bits;
                    long mask = (1L << bits) - 1;
                    for (int ly = 15; ly >= 0; ly--)
                    {
                        for (int idx = 0; idx < 256; idx++)
                        {
                            int bi = ly * 256 + idx;
                            string name = data is null ? palette[0] : palette[(int)((data[bi / perLong] >> ((bi % perLong) * bits)) & mask)];
                            if (name == "minecraft:air") continue;
                            int y = sy * 16 + ly;
                            if (y > topY[idx] || topName[idx] is null) { topY[idx] = (short)y; topName[idx] = name; }
                        }
                    }
                }
                for (int idx = 0; idx < 256; idx++)
                {
                    if (topName[idx] is null) continue;
                    histogram[topName[idx]!] = histogram.GetValueOrDefault(topName[idx]!) + 1;
                    int px = ox + (idx & 15), pz = oz + (idx >> 4);
                    var (r, g, b) = Colours.TryGetValue(topName[idx]!, out var c) ? c
                        : topName[idx]!.Contains("_ore") ? ((byte)200, (byte)200, (byte)90) : ((byte)255, (byte)0, (byte)255);
                    int o = (pz * width + px) * 4;
                    rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
                    heights[pz * width + px] = topY[idx];
                }
            }
        }

        // Light relief shading from height differences to the west neighbour.
        for (int z = 0; z < height; z++)
        {
            for (int x = 1; x < width; x++)
            {
                int o = (z * width + x) * 4;
                if (rgba[o + 3] == 0) continue;
                int d = heights[z * width + x] - heights[z * width + x - 1];
                double f = Math.Clamp(1 + d * 0.08, 0.6, 1.3);
                rgba[o] = (byte)Math.Clamp(rgba[o] * f, 0, 255);
                rgba[o + 1] = (byte)Math.Clamp(rgba[o + 1] * f, 0, 255);
                rgba[o + 2] = (byte)Math.Clamp(rgba[o + 2] * f, 0, 255);
            }
        }
        if (crop is { } win)
        {
            int cx0 = Math.Clamp(win.X0 - minRx * 512, 0, width - 1), cz0 = Math.Clamp(win.Z0 - minRz * 512, 0, height - 1);
            int cx1 = Math.Clamp(win.X1 - minRx * 512, cx0 + 1, width), cz1 = Math.Clamp(win.Z1 - minRz * 512, cz0 + 1, height);
            int cw = (cx1 - cx0) * scale, ch = (cz1 - cz0) * scale;
            var outPx = new byte[cw * ch * 4];
            for (int z = 0; z < ch; z++)
                for (int x = 0; x < cw; x++)
                {
                    int src = ((cz0 + z / scale) * width + cx0 + x / scale) * 4, dst = (z * cw + x) * 4;
                    Buffer.BlockCopy(rgba, src, outPx, dst, 4);
                }
            File.WriteAllBytes(outPng, PngWriter.Encode(cw, ch, outPx));
            Console.WriteLine($"cropped {cx1 - cx0} x {cz1 - cz0} blocks at {scale}x");
            int midZ = (cz0 + cz1) / 2;
            Console.WriteLine("top heights along the middle row: " + string.Join(" ", Enumerable.Range(cx0, Math.Min(cx1 - cx0, 120)).Select(x => heights[midZ * width + x])));
            return;
        }
        File.WriteAllBytes(outPng, PngWriter.Encode(width, height, rgba));
        foreach (var (name, n) in histogram.OrderByDescending(kv => kv.Value).Take(40))
            Console.WriteLine($"{n,10:N0}  {name}");
    }
}
