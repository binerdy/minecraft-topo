using System.Collections.Concurrent;
using System.IO.Compression;
using MinecraftTopo.Core;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Server.Jobs;

public sealed class Job
{
    public required string Id { get; init; }
    public required JobRequestDto Request { get; init; }
    public required GenerationRequest Generation { get; init; }
    public JobState State { get; set; } = JobState.Queued;
    public ProgressInfo Progress { get; set; } = new("queued", 0, "Waiting for a free worker");
    public List<string> Log { get; } = [];
    public GenerationResult? Result { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? ZipPath { get; set; }
    public CancellationTokenSource Cts { get; } = new();
    public readonly object Sync = new();

    /// <summary>Live map of the classified terrain (PNG) and what a later spawn change needs.</summary>
    public byte[]? MapPng { get; set; }
    public int MapCellsPerPixel { get; set; } = 1;
    /// <summary>Snapshots per stage in the order they were made; the last one is the newest.</summary>
    public List<(string Stage, byte[] Png)> Snapshots { get; } = [];
    /// <summary>One byte per chunk (row-major, ChunksWide per row), 1 = written.</summary>
    public byte[]? ChunkMask { get; set; }
    public short[]? Tops { get; set; }
    public byte[]? Walkable { get; set; }
    public int? SpawnX { get; set; }
    public int? SpawnZ { get; set; }
    public int? SpawnY { get; set; }
    /// <summary>Spawn chosen on the map while the job still runs; applied when level.dat is written.</summary>
    public (int X, int Z)? PendingSpawn { get; set; }
    public bool Played { get; set; }

    public bool IsTerminal => State is JobState.Done or JobState.Failed or JobState.Cancelled;

    public JobDto ToDto()
    {
        lock (Sync)
        {
            return new JobDto(Id, State, Request.WorldName, Request.OutputMode, Progress, Log.ToArray(), Result, Error,
                CreatedAt, FinishedAt, State == JobState.Done && ZipPath is not null ? $"/api/jobs/{Id}/download" : null,
                MapPng is not null ? $"/api/jobs/{Id}/map.png?v={Snapshots.Count}" : null, MapCellsPerPixel, Generation.BlocksWide, Generation.BlocksHigh,
                SpawnX, SpawnY, SpawnZ, MapPng is not null && Tops is not null && !Played && State is JobState.Running or JobState.Done,
                Snapshots.Select(s => s.Stage).ToArray(), Snapshots.Count > 0 ? Snapshots[^1].Stage : null,
                Generation.ChunksWide, Generation.ChunksHigh, ChunkMask is null ? null : Convert.ToBase64String(ChunkMask));
        }
    }
}

/// <summary>Owns generation jobs: one runs at a time, the rest wait in order.</summary>
public sealed class JobManager : IDisposable
{
    private const int MaxLogLines = 400;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    private readonly WorldGenerator _generator;
    private readonly AppPaths _paths;
    private readonly ILogger<JobManager> _logger;
    private readonly ConcurrentDictionary<string, Job> _jobs = new();
    private readonly SemaphoreSlim _worker = new(1, 1);
    private readonly Timer _cleanup;

    public JobManager(WorldGenerator generator, AppPaths paths, ILogger<JobManager> logger)
    {
        _generator = generator;
        _paths = paths;
        _logger = logger;
        _cleanup = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public IEnumerable<JobDto> All() => _jobs.Values.OrderByDescending(j => j.CreatedAt).Select(j => j.ToDto());

    public Job? Get(string id) => _jobs.GetValueOrDefault(id);

    /// <summary>Validates and queues a job. Throws <see cref="ArgumentException"/> on bad input.</summary>
    public Job Create(JobRequestDto dto)
    {
        string id = Guid.NewGuid().ToString("N")[..12];
        string worldName = dto.WorldName.Trim();
        if (worldName.Length == 0) throw new ArgumentException("World name is required.");
        if (worldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || worldName is "." or "..")
            throw new ArgumentException("World name contains characters that are not allowed in a folder name.");

        string outputDir;
        if (dto.OutputMode == OutputMode.Zip)
        {
            outputDir = Path.Combine(_paths.JobsDir, id, worldName);
        }
        else
        {
            string saves = (dto.SavesDir ?? AppPaths.DefaultMinecraftSavesDir() ?? "").Trim();
            if (saves.Length == 0 || !Path.IsPathRooted(saves)) throw new ArgumentException("Saves folder must be an absolute path.");
            saves = Path.GetFullPath(saves);
            if (!Directory.Exists(saves)) throw new ArgumentException($"Saves folder does not exist: {saves}");
            outputDir = Path.Combine(saves, worldName);
        }

        var request = new GenerationRequest
        {
            Area = Lv95Rect.FromCorners(dto.Area.MinE, dto.Area.MinN, dto.Area.MaxE, dto.Area.MaxN),
            WorldName = worldName,
            OutputDir = outputDir,
            MetresPerBlock = dto.MetresPerBlock,
            Source = dto.Source,
            Alti3dResolution = dto.Alti3dResolution,
            ReplaceExisting = dto.ReplaceExisting,
            LandCover = dto.LandCover,
            BuildingModel = dto.BuildingModel,
            Blocks = dto.Blocks,
            GameMode = Enum.TryParse<GameMode>(dto.GameMode, true, out var gm) ? gm : GameMode.Creative,
            Difficulty = Enum.TryParse<Difficulty>(dto.Difficulty, true, out var df) ? df : Difficulty.Peaceful,
            Spawn =dto.SpawnE is { } se && dto.SpawnN is { } sn ? new Lv95Point(se, sn) : null,
            Terrain = new TerrainOptions
            {
                BaseY = dto.BaseY,
                WorldHeight = dto.WorldHeight.ToLowerInvariant() switch { "tall" => WorldHeight.Tall, "standard" => WorldHeight.Standard, _ => dto.MetresPerBlock <= 2 ? WorldHeight.Tall : WorldHeight.Standard },
                VerticalScale = dto.VerticalScale,
                WaterLevel = dto.WaterLevel,
                WaterBodies = dto.WaterBodies,
                Trees = dto.Trees,
                Vegetation = dto.Vegetation,
                Resources = dto.Resources,
                Roads = dto.Roads,
                Rails = dto.Rails,
                Buildings = dto.Buildings,
                PowerLines = dto.PowerLines,
                Villagers = dto.Villagers,
                StreetSigns = dto.StreetSigns,
                Geology = Enum.TryParse<GeologySource>(dto.Geology, true, out var geo) ? geo : GeologySource.Gk500,
                GlacierYear = dto.GlacierYear switch { 1850 => Core.Glaciers.GlacierYear.Y1850, 1973 => Core.Glaciers.GlacierYear.Y1973, 2010 => Core.Glaciers.GlacierYear.Y2010, _ => Core.Glaciers.GlacierYear.Today },
                IceToBed = dto.IceToBed,
                LakeFloors = dto.LakeFloors,
                VegetationHeights = dto.VegetationHeights,
                SurfaceStyle = Enum.TryParse<SurfaceStyle>(dto.SurfaceStyle, true, out var style) ? style : SurfaceStyle.None,
                PlaceNames = dto.PlaceNames,
                Extras = dto.Extras,
                Jura3d = dto.Jura3d,
                Wildlife = dto.Wildlife,
                Crops = dto.Crops,
                StreetLights = dto.StreetLights,
                RoofColours = dto.RoofColours,
                SnowLine = dto.SnowLine,
                SlopeStoneDegrees = dto.SlopeStoneDegrees,
            },
        };
        WorldGenerator.Validate(request);
        if (dto.Alti3dResolution != 2 && dto.Alti3dResolution != 0.5) throw new ArgumentException("swissALTI3D resolution must be 2 or 0.5.");
        if (!request.ReplaceExisting && Core.Anvil.WorldWriter.Exists(outputDir))
            throw new ArgumentException($"A world named '{worldName}' already exists. Choose another name or tick 'Replace an existing world'.");

        var job = new Job { Id = id, Request = dto with { WorldName = worldName }, Generation = request };
        _jobs[id] = job;
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    /// <summary>Moves the spawn to the nearest walkable cell; rewrites level.dat when the world is already written and untouched.</summary>
    public JobDto SetSpawn(string id, int x, int z)
    {
        var job = _jobs.GetValueOrDefault(id) ?? throw new KeyNotFoundException("Unknown job.");
        lock (job.Sync)
        {
            if (job.Walkable is null || job.Tops is null) throw new InvalidOperationException("The map is not ready yet.");
            int w = job.Generation.BlocksWide, h = job.Generation.BlocksHigh;
            var (sx, sz) = SpawnPlanner.Snap(job.Walkable, w, h, x, z, Math.Clamp((int)Math.Round(300 / job.Generation.MetresPerBlock), 3, 300));
            int sy = job.Tops[sz * w + sx] + 1;
            job.PendingSpawn = (sx, sz);
            job.SpawnX = sx; job.SpawnZ = sz; job.SpawnY = sy;
            if (job.State == JobState.Done && job.Result is { } r)
            {
                if (Core.Anvil.WorldWriter.WasPlayed(r.OutputDir))
                {
                    job.Played = true;
                    throw new InvalidOperationException("The world has already been opened in Minecraft; use /setworldspawn in the game instead.");
                }
                var g = job.Generation;
                Core.Anvil.WorldWriter.WriteLevelDatIfMissing(r.OutputDir, g.WorldName, sx, sy, sz, WorldGenerator.SeedFor(g.WorldName), g.GameMode, g.Difficulty,
                    g.Terrain.WorldHeight == WorldHeight.Tall, overwrite: true);
                job.Result = r with { SpawnX = sx, SpawnY = sy, SpawnZ = sz };
                if (job.ZipPath is not null && File.Exists(job.ZipPath))
                {
                    File.Delete(job.ZipPath);
                    ZipFile.CreateFromDirectory(r.OutputDir, job.ZipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
                }
                AppendLog(job, $"[spawn] Spawn point set to block {sx}, {sy}, {sz}");
            }
            else AppendLog(job, $"[spawn] Spawn point will be block {sx}, {sy}, {sz}");
        }
        return job.ToDto();
    }

    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job) || job.IsTerminal) return false;
        job.Cts.Cancel();
        return true;
    }

    private async Task RunAsync(Job job)
    {
        var ct = job.Cts.Token;
        try
        {
            await _worker.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Finish(job, JobState.Cancelled, "Cancelled while queued");
            return;
        }

        try
        {
            lock (job.Sync)
            {
                job.State = JobState.Running;
                job.Progress = new ProgressInfo("prepare", 0, "Starting");
                AppendLog(job, $"Started: {job.Generation.BlocksWide} x {job.Generation.BlocksHigh} blocks at {job.Generation.MetresPerBlock} m/block");
            }
            string? lastMessage = null;
            var progress = new Progress<ProgressInfo>(p =>
            {
                lock (job.Sync)
                {
                    job.Progress = p;
                    if (p.Message is not null && p.Message != lastMessage && !p.Message.StartsWith("Chunk ", StringComparison.Ordinal))
                    {
                        AppendLog(job, $"[{p.Phase}] {p.Message}");
                        lastMessage = p.Message;
                    }
                }
            });

            var result = await _generator.GenerateAsync(job.Generation, progress, ct,
                terrain =>
                {
                    var png = TerrainMapRenderer.RenderPng(terrain, 2048, out int cells);
                    var tops = new short[terrain.Width * terrain.Height];
                    for (int i = 0; i < tops.Length; i++) tops[i] = (short)Math.Max(terrain.TopY[i], terrain.WaterY[i] == ClassifiedTerrain.NoWater ? terrain.TopY[i] : terrain.WaterY[i]);
                    var walk = SpawnPlanner.WalkableMask(terrain);
                    var (sx, sz) = job.Generation.SpawnBlock();
                    (sx, sz) = SpawnPlanner.Snap(terrain, sx, sz, Math.Clamp((int)Math.Round(300 / job.Generation.MetresPerBlock), 3, 300));
                    lock (job.Sync)
                    {
                        job.MapPng = png;
                        job.MapCellsPerPixel = cells;
                        job.Tops = tops;
                        job.Walkable = walk;
                        if (job.PendingSpawn is null) { job.SpawnX = sx; job.SpawnZ = sz; job.SpawnY = tops[sz * terrain.Width + sx] + 1; }
                        AppendLog(job, "[map] Map ready; click on it to move the spawn point");
                    }
                },
                () => { lock (job.Sync) return job.PendingSpawn; },
                snap =>
                {
                    int cells = Math.Max(1, (int)Math.Ceiling(Math.Max(snap.Width, snap.Height) / 1024.0));
                    var img = snap.Render(cells);
                    var png = Core.Imaging.PngWriter.Encode(img.Width, img.Height, img.Pixels);
                    lock (job.Sync)
                    {
                        job.Snapshots.Add((snap.Stage, png));
                        job.MapPng = png;
                        job.MapCellsPerPixel = cells;
                        job.Progress = job.Progress with { Message = job.Progress.Message };
                    }
                },
                (cx, cz) =>
                {
                    var mask = job.ChunkMask;
                    if (mask is null)
                    {
                        lock (job.Sync) job.ChunkMask = mask = job.ChunkMask ?? new byte[job.Generation.ChunksWide * job.Generation.ChunksHigh];
                    }
                    mask[cz * job.Generation.ChunksWide + cx] = 1;
                });

            if (job.Request.OutputMode == OutputMode.Zip)
            {
                lock (job.Sync) { job.Progress = new ProgressInfo("zip", 0, "Compressing world folder"); AppendLog(job, "[zip] Compressing world folder"); }
                string zipPath = Path.Combine(_paths.JobsDir, job.Id + ".zip");
                await Task.Run(() =>
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    ZipFile.CreateFromDirectory(result.OutputDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: true);
                }, ct);
                job.ZipPath = zipPath;
            }

            lock (job.Sync) { job.Result = result; }
            Finish(job, JobState.Done, $"Done in {result.Elapsed.TotalSeconds:0.0} s");
        }
        catch (OperationCanceledException)
        {
            Finish(job, JobState.Cancelled, "Cancelled");
            TryCleanupPartialOutput(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Id} failed", job.Id);
            lock (job.Sync) { job.Error = ex.Message; }
            Finish(job, JobState.Failed, "Failed: " + ex.Message);
        }
        finally
        {
            _worker.Release();
        }
    }

    private static void Finish(Job job, JobState state, string message)
    {
        lock (job.Sync)
        {
            job.State = state;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.Progress = new ProgressInfo(state.ToString().ToLowerInvariant(), state == JobState.Done ? 100 : job.Progress.Percent, message);
            AppendLog(job, message);
        }
    }

    private static void AppendLog(Job job, string line)
    {
        job.Log.Add($"{DateTime.Now:HH:mm:ss} {line}");
        if (job.Log.Count > MaxLogLines) job.Log.RemoveRange(0, job.Log.Count - MaxLogLines);
    }

    private void TryCleanupPartialOutput(Job job)
    {
        // Only clean up folders the tool created itself inside its own jobs directory.
        if (job.Request.OutputMode != OutputMode.Zip) return;
        try
        {
            string dir = Path.Combine(_paths.JobsDir, job.Id);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        foreach (var job in _jobs.Values.Where(j => j.IsTerminal && j.FinishedAt < cutoff).ToList())
        {
            _jobs.TryRemove(job.Id, out _);
            try
            {
                if (job.ZipPath is not null && File.Exists(job.ZipPath)) File.Delete(job.ZipPath);
                string dir = Path.Combine(_paths.JobsDir, job.Id);
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not clean up job {Id}", job.Id);
            }
        }
    }

    public void Dispose()
    {
        _cleanup.Dispose();
        foreach (var job in _jobs.Values) job.Cts.Cancel();
    }
}
