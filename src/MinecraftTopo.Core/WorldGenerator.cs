using System.Diagnostics;
using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geology;
using MinecraftTopo.Core.Glaciers;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Names;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core;

/// <summary>Everything the generator can pull in besides elevation and land cover; null members are unavailable.</summary>
public sealed class DataSources
{
    public Func<GenerationRequest, ILandCoverSource?>? Cover { get; init; }
    public Func<GenerationRequest, Buildings.SwissBuildings3dSource?>? Buildings { get; init; }
    public Func<GenerationRequest, Gk500Source?>? Gk500 { get; init; }
    public Func<GenerationRequest, GeoCoverSource?>? GeoCover { get; init; }
    public Func<GenerationRequest, GlacierSource?>? Glaciers { get; init; }
    public Func<GenerationRequest, SwissBathy3dSource?>? Bathymetry { get; init; }
    public Func<GenerationRequest, IElevationSource?>? SurfaceModel { get; init; }
    public Func<GenerationRequest, SurfaceColourSource?>? Imagery { get; init; }
    public Func<GenerationRequest, SwissNamesSource?>? Names { get; init; }
    public Func<GenerationRequest, SwissJura3dSource?>? Jura3d { get; init; }

    /// <summary>The standard set over the swisstopo services.</summary>
    public static DataSources Swisstopo(Downloader downloader, AppPaths paths, Tlm.TlmDatasets tlm) => new()
    {
        Cover = req => req.ResolveSource() == ElevationSourceKind.Synthetic ? null
            : req.LandCover == LandCoverKind.Tlm3d ? new Tlm.TlmLandCoverSource(tlm, Tlm.TlmKind.Tlm3d)
            : req.LandCover == LandCoverKind.TlmRegio ? new Tlm.TlmLandCoverSource(tlm, Tlm.TlmKind.Regio)
            : new Vec25LandCoverSource(downloader, paths),
        Buildings = _ => new Buildings.SwissBuildings3dSource(downloader, paths),
        Gk500 = _ => new Gk500Source(downloader, paths),
        GeoCover = _ => new GeoCoverSource(downloader, paths),
        Glaciers = _ => new GlacierSource(downloader, paths),
        Bathymetry = _ => new SwissBathy3dSource(downloader, paths),
        SurfaceModel = _ => SwissAlti3dSource.Surface3d(downloader, paths),
        Imagery = _ => new SurfaceColourSource(downloader, paths),
        Names = _ => new SwissNamesSource(downloader, paths),
        Jura3d = _ => new SwissJura3dSource(downloader, paths),
    };
}

/// <summary>Runs the whole pipeline: elevation -> block grid -> classification -> region files -> world files.</summary>
public sealed class WorldGenerator
{
    /// <summary>Hard cap to protect memory; the UI warns well before this.</summary>
    public const long MaxBlocks = 400_000_000;

    private readonly Func<GenerationRequest, IElevationSource> _sourceFactory;
    private readonly DataSources _sources;

    public WorldGenerator(Func<GenerationRequest, IElevationSource> sourceFactory, DataSources? sources = null)
    {
        _sourceFactory = sourceFactory;
        _sources = sources ?? new DataSources();
    }

    public static long SeedFor(string worldName) => unchecked((long)0x4D43544F504F5F00L ^ worldName.GetHashCode());

    /// <param name="onTerrain">Called once the terrain is classified and every layer is applied, before the region files are written.</param>
    /// <param name="spawnOverride">Asked right before level.dat is written; a block position replaces the requested spawn.</param>
    /// <param name="onSnapshot">Offered a map after every stage (relief, water, forest, roads, ... , world) while the generator works.</param>
    /// <param name="onChunkWritten">Called for every chunk written (from several threads).</param>
    public async Task<GenerationResult> GenerateAsync(GenerationRequest req, IProgress<ProgressInfo>? progress, CancellationToken ct,
        Action<ClassifiedTerrain>? onTerrain = null, Func<(int X, int Z)?>? spawnOverride = null,
        Action<MapSnapshotRequest>? onSnapshot = null, Action<int, int>? onChunkWritten = null)
    {
        var sw = Stopwatch.StartNew();
        Validate(req);

        var source = _sourceFactory(req);
        bool real = req.ResolveSource() != ElevationSourceKind.Synthetic;
        progress?.Report(new ProgressInfo("prepare", 0, $"Elevation source: {source.Name}; {req.BlocksWide} x {req.BlocksHigh} blocks"));

        // 1. Block grid aligned to the LV95 selection. Row 0 = north edge.
        var grid = new HeightGrid(req.Area.MinE, req.Area.MaxN, req.MetresPerBlock, req.BlocksWide, req.BlocksHigh);

        // 2. Elevations.
        await source.FillAsync(grid, progress, ct);
        int filled = grid.CountNaN() > 0 ? grid.FillNaN() : 0;
        if (filled > 0) progress?.Report(new ProgressInfo("sample", 100, $"{filled} cells without data were filled from neighbours"));
        void Snapshot(string stage, PreviewLayers layers) =>
            onSnapshot?.Invoke(new MapSnapshotRequest(stage, grid.Width, grid.Height, cells => PreviewRenderer.Render(grid, layers, cells, req.Terrain.SnowLine)));
        Snapshot("relief", new PreviewLayers());

        // 3. Lakes, rivers, forests and infrastructure.
        LandCover? cover = null;
        var t = req.Terrain;
        bool wantsInfrastructure = t.Roads || t.Rails || t.Buildings || t.PowerLines || t.Extras;
        var coverSource = t.WaterBodies || t.Trees || wantsInfrastructure ? _sources.Cover?.Invoke(req) : null;
        if (coverSource is not null)
        {
            if ((t.Roads || t.Rails || t.Buildings || t.PowerLines) && !coverSource.SupportsInfrastructure)
            {
                progress?.Report(new ProgressInfo("landcover", 0, $"{coverSource.Name} has no roads, railways or buildings; switch the land cover to swissTLM3D or swissTLMRegio for those."));
            }
            var options = coverSource.SupportsInfrastructure ? new LandCoverOptions(t.Roads, t.Rails, t.Buildings, t.PowerLines, t.StreetSigns, t.Extras, t.StreetLights && t.Roads && t.Buildings) : new LandCoverOptions();
            cover = await coverSource.GetAsync(grid, options, progress, ct);
            Snapshot("water", new PreviewLayers { Water = cover.Water });
            if (cover.Cover is not null) Snapshot("land cover", new PreviewLayers { Water = cover.Water, Cover = cover.Cover });
            Snapshot("forest", new PreviewLayers { Water = cover.Water, Cover = cover.Cover, Forest = cover.Forest });
            if (cover.Road is not null) Snapshot("roads", new PreviewLayers { Water = cover.Water, Cover = cover.Cover, Forest = cover.Forest, Road = cover.Road });
            if (cover.Rail is not null) Snapshot("railways", new PreviewLayers { Water = cover.Water, Cover = cover.Cover, Forest = cover.Forest, Road = cover.Road, Rail = cover.Rail });
            if (cover.BuildingId is not null) Snapshot("buildings", new PreviewLayers { Water = cover.Water, Cover = cover.Cover, Forest = cover.Forest, Road = cover.Road, Rail = cover.Rail, BuildingId = cover.BuildingId });
        }
        PreviewLayers Known() => new() { Water = cover?.Water, Cover = cover?.Cover, Forest = cover?.Forest, Road = cover?.Road, Rail = cover?.Rail, BuildingId = cover?.BuildingId };

        // 3b. Measured building model (swissBUILDINGS3D) replaces footprint box buildings when selected.
        if (t.Buildings && req.BuildingModel == BuildingModelKind.SwissBuildings3d && real && _sources.Buildings?.Invoke(req) is { } buildingSource)
        {
            var model = await buildingSource.GetAsync(grid, progress, ct);
            cover = (cover ?? LandCover.Empty(grid.Width * grid.Height)).WithBuildingModel(model);
            Snapshot("measured buildings", Known());
        }

        // 3c. Vegetation heights from the surface model (before any change to the terrain grid).
        float[]? canopy = null;
        if (t.VegetationHeights && t.Trees && real && req.MetresPerBlock <= 4 && _sources.SurfaceModel?.Invoke(req) is { } dsmSource)
        {
            var dsm = new HeightGrid(grid.OriginE, grid.OriginN, grid.CellSize, grid.Width, grid.Height);
            await dsmSource.FillAsync(dsm, progress, ct);
            canopy = new float[grid.Width * grid.Height];
            int crowns = 0;
            for (int i = 0; i < canopy.Length; i++)
            {
                float c = dsm.Data[i] - grid.Data[i];
                canopy[i] = float.IsNaN(c) ? 0 : Math.Max(0, c);
                if (canopy[i] >= 4) crowns++;
            }
            progress?.Report(new ProgressInfo("surface", 100, $"Surface model read: {100.0 * crowns / canopy.Length:0.#}% of the area is under vegetation taller than 4 m"));
        }

        // 3d. Lake floors.
        float[]? lakeBed = null;
        if (t.LakeFloors && t.WaterBodies && real && cover is not null && cover.WaterCount > 0 && _sources.Bathymetry?.Invoke(req) is { } bathy)
        {
            lakeBed = await bathy.GetAsync(grid, progress, ct);
        }

        // 3e. Glaciers: raise the surface to a historic extent and/or model the ice down to the bed.
        IceModel? ice = null;
        if ((t.GlacierYear != GlacierYear.Today || t.IceToBed) && real && _sources.Glaciers?.Invoke(req) is { } glacierSource)
        {
            var model = await glacierSource.GetAsync(grid, progress, ct);
            if (model is not null)
            {
                bool[]? coverGlacier = null;
                if (cover?.Cover is { } cc)
                {
                    coverGlacier = new bool[cc.Length];
                    for (int i = 0; i < cc.Length; i++) coverGlacier[i] = cc[i] == (byte)CoverClass.Glacier;
                }
                ice = GlacierPlanner.Apply(grid, model, t.GlacierYear, coverGlacier, progress, ct);
                var k = Known();
                Snapshot("glaciers", new PreviewLayers { Water = k.Water, Cover = k.Cover, Forest = k.Forest, Road = k.Road, Rail = k.Rail, BuildingId = k.BuildingId, Ice = ice.Ice });
            }
        }

        // 3f. Rock types.
        byte[]? geology = null, deposit = null;
        if (t.Geology != GeologySource.None && real)
        {
            if (t.Geology == GeologySource.GeoCover && _sources.GeoCover?.Invoke(req) is { } geoCover)
            {
                var gc = await geoCover.GetAsync(grid, progress, ct);
                if (gc is { } g) { geology = g.Bedrock; deposit = g.Deposit; }
            }
            if (_sources.Gk500?.Invoke(req) is { } gk500)
            {
                var coarse = await gk500.GetAsync(grid, progress, ct);
                if (geology is null) geology = coarse;
                else for (int i = 0; i < geology.Length; i++) if (geology[i] == 0) geology[i] = coarse[i];
            }
            if (geology is not null)
            {
                var k = Known();
                Snapshot("rock types", new PreviewLayers { Water = k.Water, Cover = k.Cover, Forest = k.Forest, Road = k.Road, Rail = k.Rail, BuildingId = k.BuildingId, Ice = ice?.Ice, Geology = geology });
            }
        }

        // 4. Classification.
        long seed = SeedFor(req.WorldName);
        progress?.Report(new ProgressInfo("classify", 0, "Classifying terrain"));
        var extras = new ClassifyExtras { LakeBed = lakeBed, Ice = ice, Canopy = canopy };
        var terrain = await Task.Run(() => SurfaceClassifier.Classify(grid, req.Terrain, req.MetresPerBlock, cover, seed, ct, extras), ct);
        terrain.Geology = geology;
        terrain.Deposit = deposit;
        terrain.Elevation = grid.Data;
        terrain.Crops = t.Crops;
        terrain.Palette = BlockPalette.Create(req.Blocks, out var paletteWarnings);
        foreach (var warning in paletteWarnings) progress?.Report(new ProgressInfo("classify", 0, warning));
        if (terrain.Palette.Overrides.Count > 0)
        {
            string choices = string.Join(", ", terrain.Palette.Overrides.Select(kv => BlockRoles.Find(kv.Key)!.Label + " = " + kv.Value));
            progress?.Report(new ProgressInfo("classify", 0, "Block choices: " + choices));
        }

        // 4a. Formation tops from the 3D geological model of the Jura.
        if (t.Jura3d && t.Geology != GeologySource.None && real && _sources.Jura3d?.Invoke(req) is { } jura)
        {
            var horizons = await jura.GetAsync(grid, progress, ct);
            if (horizons is not null)
            {
                terrain.Horizons = horizons.Select(hz =>
                {
                    var ys = new short[hz.Elevation.Length];
                    for (int i = 0; i < ys.Length; i++) ys[i] = float.IsNaN(hz.Elevation[i]) ? ClassifiedTerrain.NoWater : (short)terrain.ToY(hz.Elevation[i]);
                    return (hz.LithologyClass, ys);
                }).ToArray();
            }
        }

        // 4b. Ground colours from imagery or maps (extras from the landscape model win).
        if (t.SurfaceStyle != SurfaceStyle.None && real && _sources.Imagery?.Invoke(req) is { } imagery)
        {
            var top = await imagery.GetAsync(t.SurfaceStyle, grid, progress, ct);
            if (terrain.TopBlock is { } existing)
            {
                for (int i = 0; i < top.Length; i++) if (existing[i] != 0) top[i] = existing[i];
            }
            terrain.TopBlock = top;
            var k = Known();
            Snapshot("ground colours", new PreviewLayers { Water = k.Water, Cover = k.Cover, Forest = k.Forest, Road = k.Road, Rail = k.Rail, BuildingId = k.BuildingId, Ice = ice?.Ice, TopBlock = top, Names = terrain.Palette.Names });
        }

        // 4b2. Roof colours from the orthophoto.
        if (t.RoofColours && t.Buildings && real && cover?.BuildingId is { } bid && _sources.Imagery?.Invoke(req) is { } roofImagery)
        {
            terrain.RoofBlock = await roofImagery.GetRoofBlocksAsync(grid, bid, progress, ct);
        }

        // 4c. Name signs.
        if (t.PlaceNames && real && _sources.Names?.Invoke(req) is { } names)
        {
            var signs = await names.GetSignsAsync(grid, terrain, progress, ct);
            var index = terrain.Signs;
            foreach (var s in signs) index.Add(s, s.X, s.Z, s.X, s.Z);
        }

        progress?.Report(new ProgressInfo("classify", 100,
            $"Elevation {terrain.MinElevation:0} - {terrain.MaxElevation:0} m -> Y {terrain.MinY} - {terrain.MaxY}, vertical scale {terrain.VerticalScale:0.###}" +
            (terrain.WaterCells > 0 ? $", {terrain.WaterCells:N0} water columns" : "") +
            (ice is not null ? $", {ice.Cells:N0} ice columns" : "") +
            (terrain.Trees.Count > 0 ? $", {terrain.Trees.Count:N0} trees" : "") +
            (terrain.BuildingCount > 0 ? $", {terrain.BuildingCount:N0} buildings" : "") +
            (terrain.Structures.Count > 0 ? $", {terrain.Structures.Count:N0} structures" : "") +
            (terrain.Villagers.Count > 0 ? $", {terrain.Villagers.Count:N0} villagers" : "") +
            (terrain.Signs.Count > 0 ? $", {terrain.Signs.Count:N0} signs" : "")));
        if (terrain.BuildingDetail is { } det || terrain.DeckY is not null)
        {
            int doors = 0, ladders = 0, chimneys = 0, cellars = 0, bridges = 0, tunnels = 0;
            if (terrain.BuildingDetail is { } d)
                foreach (var v in d)
                {
                    if ((v & BuildingDetail.Door) != 0) doors++;
                    if ((v & BuildingDetail.Ladder) != 0) ladders++;
                    if ((v & BuildingDetail.Chimney) != 0) chimneys++;
                    if ((v & BuildingDetail.Cellar) != 0) cellars++;
                }
            if (terrain.DeckY is { } dy && terrain.DeckFlags is { } df)
                for (int i = 0; i < dy.Length; i++)
                    if (dy[i] != ClassifiedTerrain.NoWater) { if ((df[i] & DeckFlag.Tunnel) != 0) tunnels++; else bridges++; }
            progress?.Report(new ProgressInfo("classify", 100,
                $"Details: {doors:N0} doors, {ladders:N0} ladders, {chimneys:N0} chimneys, {cellars:N0} cellar columns, {terrain.Balconies.Count:N0} balconies, {bridges:N0} bridge columns, {tunnels:N0} tunnel columns"));
        }

        // 4d. Spawn on walkable ground; the caller may look at the finished terrain now.
        var (reqSpawnX, reqSpawnZ) = req.SpawnBlock();
        var snapped = SpawnPlanner.Snap(terrain, reqSpawnX, reqSpawnZ, Math.Clamp((int)Math.Round(300 / req.MetresPerBlock), 3, 300));
        if (snapped != (reqSpawnX, reqSpawnZ)) progress?.Report(new ProgressInfo("classify", 100, $"Spawn moved to walkable ground at block {snapped.X}, {snapped.Z}"));
        onTerrain?.Invoke(terrain);
        onSnapshot?.Invoke(new MapSnapshotRequest("world", terrain.Width, terrain.Height, cells => TerrainMapRenderer.Render(terrain, cells)));

        // 5. World folder + region files.
        string worldDir = req.OutputDir;
        if (WorldWriter.Exists(worldDir))
        {
            if (!req.ReplaceExisting)
            {
                throw new InvalidOperationException($"'{worldDir}' already contains a world. Choose another name or enable replacing it.");
            }
            progress?.Report(new ProgressInfo("write", 0, "Removing the existing world"));
            WorldWriter.RemoveWorldFiles(worldDir);
        }
        WorldWriter.EnsureLayout(worldDir);
        string regionDir = WorldWriter.OverworldRegionDir(worldDir);

        long totalChunks = req.TotalChunks;
        long chunksDone = 0;
        var regions = new List<(int rx, int rz)>();
        for (int rz = 0; rz < req.RegionsHigh; rz++)
            for (int rx = 0; rx < req.RegionsWide; rx++)
                regions.Add((rx, rz));

        progress?.Report(new ProgressInfo("write", 0, $"Writing {totalChunks} chunks in {regions.Count} region files", 0, (int)Math.Min(totalChunks, int.MaxValue)));
        await Task.Run(() =>
        {
            var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
            Parallel.ForEach(regions, po, r =>
            {
                var region = new RegionFile(r.rx, r.rz);
                for (int lz = 0; lz < 32; lz++)
                {
                    int cz = r.rz * 32 + lz;
                    if (cz >= req.ChunksHigh) break;
                    for (int lx = 0; lx < 32; lx++)
                    {
                        int cx = r.rx * 32 + lx;
                        if (cx >= req.ChunksWide) break;
                        ct.ThrowIfCancellationRequested();
                        region.SetChunk(lx, lz, ChunkBuilder.Build(cx, cz, terrain));
                        onChunkWritten?.Invoke(cx, cz);
                        long done = Interlocked.Increment(ref chunksDone);
                        if ((done & 63) == 0 || done == totalChunks)
                        {
                            progress?.Report(new ProgressInfo("write", 100.0 * done / totalChunks, $"Chunk {done}/{totalChunks}", (int)done, (int)totalChunks));
                        }
                    }
                }
                region.Save(Path.Combine(regionDir, RegionFile.FileName(r.rx, r.rz)));
            });
        }, ct);

        // 5b. Entities (villagers, animals, fish, boats, minecarts) live in their own region files.
        var mobs = terrain.Villagers.Select(v => new MobSpawn("minecraft:villager", v.X, v.Y, v.Z, v.Yaw, 0, v.Profession)).ToList();
        if (t.Wildlife)
        {
            var wild = await Task.Run(() => WildlifePlanner.Plan(terrain, grid.Data, req.MetresPerBlock, seed, ct), ct);
            mobs.AddRange(wild);
            if (cover is not null)
            {
                var vehicles = VehiclePlanner.Plan(terrain, cover.Points, req.MetresPerBlock, seed);
                mobs.AddRange(vehicles);
                if (vehicles.Count > 0) progress?.Report(new ProgressInfo("write", 100, $"{vehicles.Count(v => v.Id.EndsWith("boat"))} boats, {vehicles.Count(v => v.Id == "minecraft:minecart")} minecarts"));
            }
            progress?.Report(new ProgressInfo("write", 100, wild.Count > 0 ? "Wildlife: " + WildlifePlanner.Summary(wild) : "No wildlife placed"));
        }
        if (mobs.Count > 0)
        {
            progress?.Report(new ProgressInfo("write", 100, $"Writing {mobs.Count:N0} entities"));
            EntityWriter.WriteEntities(worldDir, mobs, req.WorldName.GetHashCode());
        }

        // 6. World-level files.
        var (spawnX, spawnZ) = spawnOverride?.Invoke() ?? snapped;
        spawnX = Math.Clamp(spawnX, 0, terrain.Width - 1);
        spawnZ = Math.Clamp(spawnZ, 0, terrain.Height - 1);
        int spawnY = terrain.ColumnTop(spawnX, spawnZ) + 1;
        try { WorldWriter.WriteIcon(worldDir, TerrainMapRenderer.RenderIcon(terrain)); } catch (Exception ex) { progress?.Report(new ProgressInfo("write", 100, "Icon not written: " + ex.Message)); }
        bool tall = t.WorldHeight == WorldHeight.Tall;
        if (tall)
        {
            // clouds above the highest peak, never below the vanilla 192
            WorldWriter.WriteTallWorldDataPack(worldDir, TerrainOptions.TallMinY, TerrainOptions.TallMaxY + 1 - TerrainOptions.TallMinY, Math.Max(192, terrain.MaxY + 80));
            progress?.Report(new ProgressInfo("write", 100, "Tall world: data pack written (Y -2032..2031)"));
        }
        bool wroteLevel = WorldWriter.WriteLevelDatIfMissing(worldDir, req.WorldName, spawnX, spawnY, spawnZ, seed, req.GameMode, req.Difficulty, tall);
        bool wroteGen = WorldWriter.WriteWorldGenSettingsIfMissing(worldDir, seed);

        sw.Stop();
        var result = new GenerationResult(worldDir, req.TotalBlocks, totalChunks, regions.Count, terrain.VerticalScale,
            terrain.MinElevation, terrain.MaxElevation, terrain.MinY, terrain.MaxY, spawnX, spawnY, spawnZ, terrain.WaterCells, terrain.Trees.Count, filled, wroteLevel, wroteGen, sw.Elapsed);
        progress?.Report(new ProgressInfo("done", 100,
            $"World written to {worldDir} in {sw.Elapsed.TotalSeconds:0.0} s; spawn at block {spawnX}, {spawnY}, {spawnZ}" +
            (wroteLevel ? "" : " (existing level.dat kept, spawn unchanged)")));
        return result;
    }

    public static void Validate(GenerationRequest req)
    {
        if (req.Area.Width <= 0 || req.Area.Height <= 0) throw new ArgumentException("The selected area is empty.");
        if (req.MetresPerBlock <= 0) throw new ArgumentException("Metres per block must be positive.");
        if (string.IsNullOrWhiteSpace(req.WorldName)) throw new ArgumentException("World name is required.");
        if (req.WorldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("World name contains characters that are not allowed in a folder name.");
        if (!Path.IsPathRooted(req.OutputDir)) throw new ArgumentException("Output folder must be an absolute path.");
        if (req.TotalBlocks > MaxBlocks) throw new ArgumentException($"The area is {req.TotalBlocks:N0} blocks; the maximum is {MaxBlocks:N0}. Increase metres per block or shrink the area.");
        if (req.Terrain.BaseY < req.Terrain.WorldMinY + 1 || req.Terrain.BaseY > req.Terrain.WorldMaxY - 8) throw new ArgumentException("Base Y is outside the world height.");
        if (req.Terrain.VerticalScale is { } vs && vs <= 0) throw new ArgumentException("Vertical scale must be positive.");
    }
}
