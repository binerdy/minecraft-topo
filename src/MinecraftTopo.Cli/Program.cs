using System.Globalization;
using System.IO.Compression;
using MinecraftTopo.Core;
using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Nbt;
using MinecraftTopo.Core.Terrain;

// Headless front-end over MinecraftTopo.Core for scripting and verification.

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var opts = ParseArgs(args);
if (opts.Count == 0 || opts.ContainsKey("help"))
{
    PrintHelp();
    return 0;
}

var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftTopo/1.0 (+https://github.com/minecraft-topo)");
http.Timeout = TimeSpan.FromMinutes(10);
var paths = new AppPaths(opts.GetValueOrDefault("cache"));
var downloader = new Downloader(http);
var dhm = new Dhm200Model(downloader, paths);
var progress = new Progress<ProgressInfo>(p => Console.WriteLine($"  [{p.Phase}] {p.Percent,5:0.0}%  {p.Message}"));
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    if (opts.TryGetValue("verify", out var mcaPath))
    {
        return VerifyRegion(mcaPath!, opts.GetValueOrDefault("chunk"));
    }

    if (opts.TryGetValue("tile", out var tileSpec))
    {
        // --tile z,x,y --png out.png
        var parts = tileSpec!.Split(',');
        var renderer = new OverviewTileRenderer(dhm, paths, downloader);
        var png = await renderer.GetTileAsync(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), cts.Token);
        string outPng = opts.GetValueOrDefault("png") ?? $"tile_{tileSpec.Replace(',', '_')}.png";
        await File.WriteAllBytesAsync(outPng, png, cts.Token);
        Console.WriteLine($"Wrote {outPng} ({png.Length} bytes)");
        return 0;
    }

    if (opts.TryGetValue("find-tiles", out var findSpec))
    {
        var rect = ParseRect(findSpec!);
        var src = new SwissAlti3dSource(downloader, paths, ParseDouble(opts.GetValueOrDefault("res"), 2));
        var tiles = await src.FindTilesAsync(rect, cts.Token);
        foreach (var t in tiles) Console.WriteLine($"{t.Key} {t.Year} {t.Url}");
        Console.WriteLine($"{tiles.Count} tiles");
        return 0;
    }

    if (!opts.TryGetValue("lv95", out var rectSpec))
    {
        Console.Error.WriteLine("Missing --lv95 e1,n1,e2,n2");
        return 2;
    }
    var area = ParseRect(rectSpec!);
    string name = opts.GetValueOrDefault("name") ?? "MinecraftTopo";
    string outDir = Path.GetFullPath(opts.GetValueOrDefault("out") ?? Path.Combine("out", name));
    var sourceKind = (opts.GetValueOrDefault("source") ?? "auto").ToLowerInvariant() switch
    {
        "synthetic" => ElevationSourceKind.Synthetic,
        "alti3d" or "swisstopo" => ElevationSourceKind.SwissAlti3d,
        "dhm200" => ElevationSourceKind.Dhm200,
        _ => ElevationSourceKind.Auto,
    };
    var request = new GenerationRequest
    {
        Area = area,
        WorldName = name,
        OutputDir = outDir,
        MetresPerBlock = ParseDouble(opts.GetValueOrDefault("mpb"), 1),
        Source = sourceKind,
        Alti3dResolution = ParseDouble(opts.GetValueOrDefault("res"), 2),
        ReplaceExisting = opts.ContainsKey("overwrite") || opts.ContainsKey("replace"),
        Spawn = opts.TryGetValue("spawn", out var spawnSpec) && spawnSpec is not null ? ParsePoint(spawnSpec) : null,
        Terrain = new TerrainOptions
        {
            BaseY = (int)ParseDouble(opts.GetValueOrDefault("base-y"), 0),
            VerticalScale = opts.TryGetValue("vscale", out var vs) && vs is not null && vs != "auto" ? ParseDouble(vs, 1) : null,
            WaterLevel = opts.TryGetValue("water", out var wl) && wl is not null ? ParseDouble(wl, 0) : null,
            WaterBodies = !opts.ContainsKey("no-lakes"),
            Trees = !opts.ContainsKey("no-trees"),
            Vegetation = !opts.ContainsKey("no-vegetation"),
            Resources = !opts.ContainsKey("no-resources"),
            SnowLine = ParseDouble(opts.GetValueOrDefault("snow"), 2500),
            SlopeStoneDegrees = ParseDouble(opts.GetValueOrDefault("slope"), 32),
        },
    };

    Console.WriteLine($"Area {area.MinE:0},{area.MinN:0} - {area.MaxE:0},{area.MaxN:0} ({area.Width:0} x {area.Height:0} m)");
    Console.WriteLine($"{request.BlocksWide} x {request.BlocksHigh} blocks, {request.TotalChunks} chunks, {request.TotalRegions} regions, source {request.ResolveSource()}");

    var generator = new WorldGenerator(
        r => r.ResolveSource() switch
        {
            ElevationSourceKind.Synthetic => new SyntheticSource(),
            ElevationSourceKind.Dhm200 => new Dhm200Source(dhm),
            _ => new SwissAlti3dSource(downloader, paths, r.Alti3dResolution),
        },
        r => r.ResolveSource() == ElevationSourceKind.Synthetic ? null : new MinecraftTopo.Core.Water.Vec25LandCoverSource(downloader, paths));
    var result = await generator.GenerateAsync(request, progress, cts.Token);
    Console.WriteLine();
    Console.WriteLine($"Done: {result.OutputDir}");
    Console.WriteLine($"  blocks {result.Blocks:N0}, chunks {result.Chunks:N0}, regions {result.Regions}");
    Console.WriteLine($"  elevation {result.MinElevation:0.0} - {result.MaxElevation:0.0} m, Y {result.MinY} - {result.MaxY}, vertical scale {result.VerticalScale:0.###}");
    Console.WriteLine($"  spawn block {result.SpawnX}, {result.SpawnY}, {result.SpawnZ}, water columns {result.WaterCells:N0}, trees {result.TreeCount:N0}");
    Console.WriteLine($"  filled cells {result.FilledCells}, wrote level.dat {result.WroteLevelDat}, wrote world_gen_settings {result.WroteWorldGenSettings}, {result.Elapsed.TotalSeconds:0.0} s");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    if (opts.ContainsKey("debug")) Console.Error.WriteLine(ex);
    return 1;
}

static int VerifyRegion(string path, string? chunkSpec)
{
    var info = new FileInfo(path);
    if (path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
    {
        // level.dat / world_gen_settings.dat: gzip-compressed NBT
        using var gz = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        Console.WriteLine($"{info.Name}: {info.Length} bytes (gzip NBT)");
        Console.Write(new NbtReader(gz).ReadRoot().Dump(maxListItems: 12));
        return 0;
    }
    Console.WriteLine($"{info.Name}: {info.Length} bytes, sector aligned: {info.Length % RegionFile.SectorSize == 0}");
    using var fs = File.OpenRead(path);
    var header = new byte[RegionFile.SectorSize * 2];
    fs.ReadExactly(header);
    int present = 0, firstIndex = -1;
    for (int i = 0; i < 1024; i++)
    {
        int loc = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(i * 4));
        if (loc != 0) { present++; if (firstIndex < 0) firstIndex = i; }
    }
    Console.WriteLine($"chunks present: {present}");
    if (present == 0) return 0;

    // --find <block name fragment>: scan every chunk for a palette entry containing the text.
    var argv = Environment.GetCommandLineArgs();
    int findAt = Array.IndexOf(argv, "--find");
    if (findAt >= 0 && findAt + 1 < argv.Length)
    {
        string needle = argv[findAt + 1];
        int hits = 0;
        for (int i = 0; i < 1024 && hits < 5; i++)
        {
            int loc = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(i * 4));
            if (loc == 0) continue;
            fs.Position = (long)(loc >> 8) * RegionFile.SectorSize;
            var lb = new byte[5];
            fs.ReadExactly(lb);
            var pl = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lb) - 1];
            fs.ReadExactly(pl);
            using var zs = new ZLibStream(new MemoryStream(pl), CompressionMode.Decompress);
            var chunkRoot = new NbtReader(zs).ReadRoot();
            string dump = chunkRoot.Dump(maxListItems: 100);
            if (!dump.Contains(needle, StringComparison.Ordinal)) continue;
            hits++;
            int entities = chunkRoot["block_entities"]?.Children.Count ?? 0;
            Console.WriteLine($"chunk local {i % 32},{i / 32} (xPos {chunkRoot["xPos"]?.Value}, zPos {chunkRoot["zPos"]?.Value}) contains '{needle}', block_entities: {entities}");
            foreach (var line in dump.Split('\n').Where(l => l.Contains(needle, StringComparison.Ordinal)).Take(3)) Console.WriteLine("   " + line.Trim());
            if (entities > 0) Console.Write(chunkRoot["block_entities"]!.Dump(maxListItems: 2));
        }
        Console.WriteLine(hits == 0 ? $"no chunk contains '{needle}'" : $"{hits} chunk(s) shown");
        return 0;
    }

    int index = firstIndex;
    if (chunkSpec is not null)
    {
        var p = chunkSpec.Split(',');
        index = int.Parse(p[1]) * 32 + int.Parse(p[0]);
    }
    int location = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(index * 4));
    int offset = location >> 8, sectors = location & 0xFF;
    fs.Position = (long)offset * RegionFile.SectorSize;
    var lenBuf = new byte[5];
    fs.ReadExactly(lenBuf);
    int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lenBuf);
    Console.WriteLine($"chunk index {index} (local {index % 32},{index / 32}): sector {offset}, {sectors} sectors, payload {length} bytes, compression {lenBuf[4]}");
    var payload = new byte[length - 1];
    fs.ReadExactly(payload);
    using var z = new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress);
    var root = new NbtReader(z).ReadRoot();
    if (Environment.GetCommandLineArgs().Contains("--dump"))
    {
        Console.Write(root.Dump(maxListItems: 64));
        return 0;
    }
    var sections = root["sections"];
    Console.WriteLine($"DataVersion {root["DataVersion"]?.Value}, xPos {root["xPos"]?.Value}, zPos {root["zPos"]?.Value}, Status {root["Status"]?.Value}, sections {sections?.Children.Count}");
    if (sections is not null)
    {
        foreach (var section in sections.Children)
        {
            var palette = section["block_states"]?["palette"];
            var names = palette?.Children.Select(c => c.Type == TagType.String ? (string)c.Value! : (string?)(c["id"] ?? c["Name"])?.Value ?? "?").ToList() ?? [];
            var data = section["block_states"]?["data"];
            if (names.Count == 1 && names[0] == "minecraft:air") continue;
            Console.WriteLine($"  Y={section["Y"]?.Value,3}: palette [{string.Join(", ", names.Select(n => n.Replace("minecraft:", "")))}]" +
                              (data is not null ? $" data long[{((long[])data.Value!).Length}]" : "") +
                              $" biome {section["biomes"]?["palette"]?.Children.FirstOrDefault()?.Value}");
        }
    }
    return 0;
}

static Lv95Rect ParseRect(string spec)
{
    var p = spec.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    if (p.Length != 4) throw new ArgumentException("Expected e1,n1,e2,n2");
    return Lv95Rect.FromCorners(p[0], p[1], p[2], p[3]);
}

static Lv95Point ParsePoint(string spec)
{
    var p = spec.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    if (p.Length != 2) throw new ArgumentException("Expected e,n");
    return new Lv95Point(p[0], p[1]);
}

static double ParseDouble(string? s, double fallback) =>
    s is null ? fallback : double.Parse(s, CultureInfo.InvariantCulture);

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        string key = args[i][2..];
        string? value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
        d[key] = value;
    }
    return d;
}

static void PrintHelp()
{
    Console.WriteLine("""
        MinecraftTopo CLI

        Generate:
          --lv95 e1,n1,e2,n2      area in LV95 metres (required)
          --out <world folder>    default .\out\<name>
          --name <world name>     default MinecraftTopo
          --source auto|alti3d|dhm200|synthetic
          --res 2|0.5             swissALTI3D resolution
          --mpb <metres/block>    default 1
          --vscale auto|<n>       vertical scale (blocks per metre)
          --base-y <y>            default 0
          --water <m>             flood below this real elevation
          --no-lakes              do not place lakes and rivers from swisstopo water surfaces
          --no-trees              do not plant trees in swisstopo forest areas
          --no-vegetation         no grass, ferns and flowers on grass blocks
          --no-resources          no ores, bee nests, berries, mushrooms, pumpkins, clay or seagrass
          --snow <m>              snow line, default 2500
          --slope <deg>           stone above this slope, default 32
          --spawn e,n             spawn point in LV95 metres (default: centre of the area)
          --replace               delete an existing world of the same name first
          --cache <dir>           app data folder (default %LOCALAPPDATA%\MinecraftTopo)

        Other:
          --verify <r.x.z.mca> [--chunk lx,lz]   parse a region file and dump one chunk
          --verify <level.dat>                   dump a gzip NBT file (level.dat, world_gen_settings.dat)
          --tile z,x,y [--png out.png]           render an overview map tile
          --find-tiles e1,n1,e2,n2               list swissALTI3D tiles for an area
        """);
}
