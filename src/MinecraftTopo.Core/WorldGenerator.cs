using System.Diagnostics;
using MinecraftTopo.Core.Anvil;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core;

/// <summary>Runs the whole pipeline: elevation -> block grid -> classification -> region files -> world files.</summary>
public sealed class WorldGenerator
{
    /// <summary>Hard cap to protect memory; the UI warns well before this.</summary>
    public const long MaxBlocks = 400_000_000;

    private readonly Func<GenerationRequest, IElevationSource> _sourceFactory;
    private readonly Func<GenerationRequest, ILandCoverSource?> _coverFactory;
    private readonly Func<GenerationRequest, Buildings.SwissBuildings3dSource?> _buildingFactory;
    private readonly Func<GenerationRequest, Geology.Gk500Source?> _geologyFactory;

    /// <param name="coverFactory">Returns the land-cover (water, forest) source for a request, or null for none.</param>
    /// <param name="buildingFactory">Returns the measured building model source, or null when unavailable.</param>
    /// <param name="geologyFactory">Returns the GK500 geology source, or null when unavailable.</param>
    public WorldGenerator(Func<GenerationRequest, IElevationSource> sourceFactory,
        Func<GenerationRequest, ILandCoverSource?>? coverFactory = null,
        Func<GenerationRequest, Buildings.SwissBuildings3dSource?>? buildingFactory = null,
        Func<GenerationRequest, Geology.Gk500Source?>? geologyFactory = null)
    {
        _sourceFactory = sourceFactory;
        _coverFactory = coverFactory ?? (_ => null);
        _buildingFactory = buildingFactory ?? (_ => null);
        _geologyFactory = geologyFactory ?? (_ => null);
    }

    public async Task<GenerationResult> GenerateAsync(GenerationRequest req, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Validate(req);

        var source = _sourceFactory(req);
        progress?.Report(new ProgressInfo("prepare", 0, $"Elevation source: {source.Name}; {req.BlocksWide} x {req.BlocksHigh} blocks"));

        // 1. Block grid aligned to the LV95 selection. Row 0 = north edge.
        var grid = new HeightGrid(req.Area.MinE, req.Area.MaxN, req.MetresPerBlock, req.BlocksWide, req.BlocksHigh);

        // 2. Elevations.
        await source.FillAsync(grid, progress, ct);
        int filled = grid.CountNaN() > 0 ? grid.FillNaN() : 0;
        if (filled > 0) progress?.Report(new ProgressInfo("sample", 100, $"{filled} cells without data were filled from neighbours"));

        // 3. Lakes, rivers and forests.
        LandCover? cover = null;
        var t = req.Terrain;
        bool wantsInfrastructure = t.Roads || t.Rails || t.Buildings || t.PowerLines;
        var coverSource = t.WaterBodies || t.Trees || wantsInfrastructure ? _coverFactory(req) : null;
        if (coverSource is not null)
        {
            if (wantsInfrastructure && !coverSource.SupportsInfrastructure)
            {
                progress?.Report(new ProgressInfo("landcover", 0, $"{coverSource.Name} has no roads, railways or buildings; switch the land cover to swissTLM3D or swissTLMRegio for those."));
            }
            var options = coverSource.SupportsInfrastructure ? new LandCoverOptions(t.Roads, t.Rails, t.Buildings, t.PowerLines, t.StreetSigns) : new LandCoverOptions();
            cover = await coverSource.GetAsync(grid, options, progress, ct);
        }

        // 3b. Measured building model (swissBUILDINGS3D) replaces footprint box buildings when selected.
        if (t.Buildings && req.BuildingModel == BuildingModelKind.SwissBuildings3d && req.ResolveSource() != ElevationSourceKind.Synthetic)
        {
            var buildingSource = _buildingFactory(req);
            if (buildingSource is not null)
            {
                var model = await buildingSource.GetAsync(grid, progress, ct);
                cover = (cover ?? LandCover.Empty(grid.Width * grid.Height)).WithBuildingModel(model);
            }
        }

        // 3c. Rock types from the geological map.
        byte[]? geology = null;
        if (t.Geology && req.ResolveSource() != ElevationSourceKind.Synthetic && _geologyFactory(req) is { } geologySource)
        {
            geology = await geologySource.GetAsync(grid, progress, ct);
        }

        // 4. Classification.
        long seed = unchecked((long)0x4D43544F504F5F00L ^ req.WorldName.GetHashCode());
        progress?.Report(new ProgressInfo("classify", 0, "Classifying terrain"));
        var terrain = await Task.Run(() => SurfaceClassifier.Classify(grid, req.Terrain, req.MetresPerBlock, cover, seed, ct), ct);
        terrain.Geology = geology;
        terrain.Palette = BlockPalette.Create(req.Blocks, out var paletteWarnings);
        foreach (var warning in paletteWarnings) progress?.Report(new ProgressInfo("classify", 0, warning));
        if (terrain.Palette.Overrides.Count > 0)
        {
            string choices = string.Join(", ", terrain.Palette.Overrides.Select(kv => BlockRoles.Find(kv.Key)!.Label + " = " + kv.Value));
            progress?.Report(new ProgressInfo("classify", 0, "Block choices: " + choices));
        }
        progress?.Report(new ProgressInfo("classify", 100,
            $"Elevation {terrain.MinElevation:0} - {terrain.MaxElevation:0} m -> Y {terrain.MinY} - {terrain.MaxY}, vertical scale {terrain.VerticalScale:0.###}" +
            (terrain.WaterCells > 0 ? $", {terrain.WaterCells:N0} water columns" : "") +
            (terrain.Trees.Count > 0 ? $", {terrain.Trees.Count:N0} trees" : "") +
            (terrain.BuildingCount > 0 ? $", {terrain.BuildingCount:N0} buildings" : "") +
            (terrain.Structures.Count > 0 ? $", {terrain.Structures.Count:N0} pylons/turbines" : "") +
            (terrain.Villagers.Count > 0 ? $", {terrain.Villagers.Count:N0} villagers" : "") +
            (terrain.Signs.Count > 0 ? $", {terrain.Signs.Count:N0} street signs" : "")));

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

        // 5b. Entities (villagers) live in their own region files.
        if (terrain.Villagers.Count > 0)
        {
            progress?.Report(new ProgressInfo("write", 100, $"Writing {terrain.Villagers.Count:N0} villagers"));
            EntityWriter.WriteVillagers(worldDir, terrain.Villagers, req.WorldName.GetHashCode());
        }

        // 6. World-level files.
        var (spawnX, spawnZ) = req.SpawnBlock();
        int spawnY = terrain.ColumnTop(spawnX, spawnZ) + 1;
        bool wroteLevel = WorldWriter.WriteLevelDatIfMissing(worldDir, req.WorldName, spawnX, spawnY, spawnZ, seed);
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
        if (req.Terrain.BaseY < TerrainOptions.WorldMinY + 1 || req.Terrain.BaseY > TerrainOptions.WorldMaxY - 8) throw new ArgumentException("Base Y is outside the world height.");
        if (req.Terrain.VerticalScale is { } vs && vs <= 0) throw new ArgumentException("Vertical scale must be positive.");
    }
}
