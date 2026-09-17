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

    public bool IsTerminal => State is JobState.Done or JobState.Failed or JobState.Cancelled;

    public JobDto ToDto()
    {
        lock (Sync)
        {
            return new JobDto(Id, State, Request.WorldName, Request.OutputMode, Progress, Log.ToArray(), Result, Error,
                CreatedAt, FinishedAt, State == JobState.Done && ZipPath is not null ? $"/api/jobs/{Id}/download" : null);
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
            Spawn =dto.SpawnE is { } se && dto.SpawnN is { } sn ? new Lv95Point(se, sn) : null,
            Terrain = new TerrainOptions
            {
                BaseY = dto.BaseY,
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
                Geology = dto.Geology,
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

            var result = await _generator.GenerateAsync(job.Generation, progress, ct);

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
