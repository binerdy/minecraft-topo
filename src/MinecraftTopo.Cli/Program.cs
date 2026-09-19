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
var tlm = new MinecraftTopo.Core.Tlm.TlmDatasets(downloader, paths);
var progress = new Progress<ProgressInfo>(p => Console.WriteLine($"  [{p.Phase}] {p.Percent,5:0.0}%  {p.Message}"));
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    if (opts.TryGetValue("verify", out var mcaPath))
    {
        return VerifyRegion(mcaPath!, opts.GetValueOrDefault("chunk"));
    }
    if (opts.TryGetValue("gpkg", out var gpkgPath) && gpkgPath is not null)
    {
        using var g = new MinecraftTopo.Core.Gpkg.GeoPackage(Path.GetFullPath(gpkgPath));
        if (opts.TryGetValue("layer", out var layerName) && layerName is not null)
        {
            var bbox = opts.TryGetValue("bbox", out var bb) && bb is not null ? ParseRect(bb) : new Lv95Rect(2480000, 1070000, 2840000, 1300000);
            string attr = opts.GetValueOrDefault("attr") ?? "objektart";
            var counts = new Dictionary<string, int>();
            var geomKinds = new Dictionary<string, int>();
            int n = 0;
            foreach (var f in g.Query(layerName, bbox, attr))
            {
                n++;
                string v = f.Text(attr);
                counts[v] = counts.GetValueOrDefault(v) + 1;
                string gk = f.Geometry.GetType().Name;
                geomKinds[gk] = geomKinds.GetValueOrDefault(gk) + 1;
                if (n >= 200000) break;
            }
            Console.WriteLine($"{layerName}: {n} features; geometry {string.Join(", ", geomKinds.Select(k => $"{k.Key} {k.Value}"))}");
            Console.WriteLine($"columns: {string.Join(", ", g.Columns(layerName))}");
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(60)) Console.WriteLine($"  {kv.Value,8}  {kv.Key}");
        }
        else
        {
            foreach (var l in g.Layers.OrderBy(x => x)) Console.WriteLine($"{l}: {string.Join(", ", g.Columns(l))}");
        }
        return 0;
    }
    if (opts.TryGetValue("png-colors", out var pngPath) && pngPath is not null)
    {
        var img = MinecraftTopo.Core.Imaging.PngReader.Decode(File.ReadAllBytes(pngPath));
        var counts = new Dictionary<(byte, byte, byte, byte), int>();
        for (int i = 0; i < img.Pixels.Length; i += 4)
        {
            var k = (img.Pixels[i], img.Pixels[i + 1], img.Pixels[i + 2], img.Pixels[i + 3]);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        Console.WriteLine($"{img.Width}x{img.Height}, {counts.Count} distinct colours");
        foreach (var kv in counts.OrderByDescending(k => k.Value).Take(int.Parse(opts.GetValueOrDefault("top") ?? "25")))
            Console.WriteLine($"  {kv.Value,8}  ({kv.Key.Item1},{kv.Key.Item2},{kv.Key.Item3}) a={kv.Key.Item4}");
        return 0;
    }
    if (opts.ContainsKey("list-blocks"))
    {
        foreach (var group in MinecraftTopo.Core.Anvil.BlockRoles.All.GroupBy(r => r.Group))
        {
            Console.WriteLine(group.Key);
            foreach (var r in group) Console.WriteLine($"  {r.Key,-16} {r.Default,-22} {r.Label}");
        }
        Console.WriteLine($"\nChoices ({MinecraftTopo.Core.Anvil.BlockRoles.Choices.Count}): {string.Join(", ", MinecraftTopo.Core.Anvil.BlockRoles.Choices)}");
        return 0;
    }

    if (opts.TryGetValue("tile", out var tileSpec))
    {
        // --tile z,x,y --png out.png
        var parts = tileSpec!.Split(',');
        var renderer = new OverviewTileRenderer(dhm, paths, downloader);
        var png = await renderer.GetTileAsync(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), cts.Token, opts.ContainsKey("roads"));
        string outPng = opts.GetValueOrDefault("png") ?? $"tile_{tileSpec.Replace(',', '_')}.png";
        await File.WriteAllBytesAsync(outPng, png, cts.Token);
        Console.WriteLine($"Wrote {outPng} ({png.Length} bytes)");
        return 0;
    }

    if (opts.TryGetValue("map", out var worldDir))
    {
        // --map <world folder> [--png out.png]: top-down image of the generated world, one pixel per block.
        string outPng = opts.GetValueOrDefault("png") ?? "worldmap.png";
        (int, int, int, int)? crop = null;
        if (opts.GetValueOrDefault("crop") is { } cropSpec)
        {
            var c = cropSpec.Split(',').Select(int.Parse).ToArray();
            crop = (c[0], c[1], c[2], c[3]);
        }
        WorldMap.Render(worldDir!, outPng, crop, (int)ParseDouble(opts.GetValueOrDefault("scale"), 1));
        Console.WriteLine($"Wrote {outPng}");
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
        BuildingModel = (opts.GetValueOrDefault("building-model") ?? "swisstopo").ToLowerInvariant() is "osm" or "footprints" ? BuildingModelKind.Footprints : BuildingModelKind.SwissBuildings3d,
        LandCover = (opts.GetValueOrDefault("cover") ?? "tlm3d").ToLowerInvariant() switch { "tlm3d" => LandCoverKind.Tlm3d, "regio" or "tlmregio" => LandCoverKind.TlmRegio, _ => LandCoverKind.Vec25 },
        Blocks = ParseBlocks(opts.GetValueOrDefault("blocks")),
        GameMode = Enum.TryParse<GameMode>(opts.GetValueOrDefault("mode") ?? "creative", true, out var gm) ? gm : GameMode.Creative,
        Difficulty = Enum.TryParse<Difficulty>(opts.GetValueOrDefault("difficulty") ?? "peaceful", true, out var df) ? df : Difficulty.Peaceful,
        Spawn = opts.TryGetValue("spawn", out var spawnSpec) && spawnSpec is not null ? ParsePoint(spawnSpec) : null,
        Terrain = new TerrainOptions
        {
            BaseY = (int)ParseDouble(opts.GetValueOrDefault("base-y"), -60),
            WorldHeight = (opts.GetValueOrDefault("height") ?? (opts.ContainsKey("tall") ? "tall" : "auto")).ToLowerInvariant() switch { "tall" => WorldHeight.Tall, "standard" => WorldHeight.Standard, _ => ParseDouble(opts.GetValueOrDefault("mpb"), 1) <= 2 ? WorldHeight.Tall : WorldHeight.Standard },
            VerticalScale = opts.TryGetValue("vscale", out var vs) && vs is not null && vs != "auto" ? ParseDouble(vs, 1) : null,
            WaterLevel = opts.TryGetValue("water", out var wl) && wl is not null ? ParseDouble(wl, 0) : null,
            WaterBodies = !opts.ContainsKey("no-lakes"),
            Trees = !opts.ContainsKey("no-trees"),
            Vegetation = !opts.ContainsKey("no-vegetation"),
            Resources = !opts.ContainsKey("no-resources"),
            Roads = opts.ContainsKey("roads"),
            Rails = opts.ContainsKey("rails"),
            Buildings = opts.ContainsKey("buildings"),
            PowerLines = opts.ContainsKey("power"),
            Villagers = opts.ContainsKey("villagers"),
            StreetSigns = opts.ContainsKey("signs"),
            Geology = (opts.GetValueOrDefault("geology") ?? (opts.ContainsKey("no-geology") ? "none" : "gk500")).ToLowerInvariant() switch { "none" => GeologySource.None, "geocover" => GeologySource.GeoCover, _ => GeologySource.Gk500 },
            GlacierYear = (int)ParseDouble(opts.GetValueOrDefault("glaciers"), 0) switch { 1850 => MinecraftTopo.Core.Glaciers.GlacierYear.Y1850, 1973 => MinecraftTopo.Core.Glaciers.GlacierYear.Y1973, 2010 => MinecraftTopo.Core.Glaciers.GlacierYear.Y2010, _ => MinecraftTopo.Core.Glaciers.GlacierYear.Today },
            IceToBed = !opts.ContainsKey("no-ice"),
            LakeFloors = !opts.ContainsKey("no-lake-floors"),
            VegetationHeights = opts.ContainsKey("canopy"),
            SurfaceStyle = (opts.GetValueOrDefault("surface") ?? "none").ToLowerInvariant() switch { "photo" => SurfaceStyle.Photo, "photoblocks" => SurfaceStyle.PhotoBlocks, "siegfried" => SurfaceStyle.Siegfried, "dufour" => SurfaceStyle.Dufour, "map" or "nationalmap" => SurfaceStyle.NationalMap, _ => SurfaceStyle.None },
            PlaceNames = !opts.ContainsKey("no-names"),
            Extras = !opts.ContainsKey("no-extras"),
            Jura3d = opts.ContainsKey("jura3d"),
            Wildlife = !opts.ContainsKey("no-wildlife"),
            Crops = !opts.ContainsKey("no-crops"),
            StreetLights = !opts.ContainsKey("no-lights"),
            RoofColours = !opts.ContainsKey("no-roof-colours"),
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
        DataSources.Swisstopo(downloader, paths, tlm));
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
            string dump = chunkRoot.Dump(maxListItems: 300);
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

static Dictionary<string, string>? ParseBlocks(string? spec)
{
    if (string.IsNullOrWhiteSpace(spec)) return null;
    var d = new Dictionary<string, string>();
    foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        int eq = part.IndexOf('=');
        if (eq > 0) d[part[..eq].Trim()] = part[(eq + 1)..].Trim();
    }
    return d;
}

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
          --base-y <y>            default -60
          --height auto|standard|tall   world height; auto (default) = tall up to 2 m per block. Tall = Y -2032..2031 through a data pack
          --no-lights             no lantern posts along streets in settlements
          --no-roof-colours       do not pick roof blocks from the orthophoto
          --water <m>             flood below this real elevation
          --building-model swisstopo|footprints   building shapes: swissBUILDINGS3D 3.0 (default) or landscape-model footprints
          --cover tlm3d|regio|vec25   land cover source: swissTLM3D (default), swissTLMRegio or the VECTOR25 map layer
          --roads --rails --buildings --power --villagers --signs   landscape-model infrastructure (needs --cover tlm3d or regio)
          --no-lakes              do not place lakes and rivers from swisstopo water surfaces
          --no-trees              do not plant trees in swisstopo forest areas
          --no-vegetation         no grass, ferns and flowers on grass blocks
          --no-resources          no ores, bee nests, berries, mushrooms, pumpkins, clay or seagrass
          --geology gk500|geocover|none   rock types underground (default gk500; geocover = 1:25k sheets where published)
          --glaciers 1850|1973|2010   build the glaciers of that inventory year (default: today)
          --no-ice                do not fill today's glaciers with ice down to the modelled bed
          --no-lake-floors        flat lake beds instead of swissBATHY3D depths
          --canopy                tree heights and crowns from swissSURFACE3D (about 20 MB per km²)
          --surface photo|photoblocks|siegfried|dufour|map   colour the ground from the orthophoto or a map
          --no-names              no peak, pass, field and place name signs from swissNAMES3D
          --no-extras             no walls, dams, lifts, sports fields, runways and small objects from swissTLM3D
          --jura3d                underground formation boundaries from the swissJURA3D model (108 MB one-time)
          --no-wildlife           no cows, sheep, goats, foxes, wolves, frogs, salmon and so on
          --no-crops              no ripe crops on cropland
          --mode creative|survival|adventure|hardcore   game mode (default creative)
          --difficulty peaceful|easy|normal|hard        default peaceful
          --blocks role=block,...  block choices, e.g. asphalt=black_concrete,geo8=calcite (--list-blocks shows roles)
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
