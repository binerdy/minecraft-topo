using System.IO.Compression;
using System.Text.Json;
using MinecraftTopo.Core.Elevation;

namespace MinecraftTopo.Core.Tlm;

public enum TlmKind
{
    /// <summary>swissTLMRegio, 1:200 000, about 160 MB download.</summary>
    Regio,
    /// <summary>swissTLM3D, the detailed landscape model, about 4.8 GB download.</summary>
    Tlm3d,
}

/// <summary>
/// Manages the one-time download and extraction of the swisstopo landscape model GeoPackages
/// (©swisstopo). The newest release is found through the geo.admin.ch STAC API.
/// </summary>
public sealed class TlmDatasets
{
    private readonly Downloader _downloader;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<TlmKind, ProgressInfo> _status = new();

    public TlmDatasets(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _root = Path.Combine(paths.CacheDir, "tlm");
        Directory.CreateDirectory(Dir(TlmKind.Regio));
        Directory.CreateDirectory(Dir(TlmKind.Tlm3d));
    }

    public static string Collection(TlmKind kind) => kind == TlmKind.Regio ? "ch.swisstopo.swisstlmregio" : "ch.swisstopo.swisstlm3d";
    public static string DisplayName(TlmKind kind) => kind == TlmKind.Regio ? "swissTLMRegio" : "swissTLM3D";
    public static long ApproxBytes(TlmKind kind) => kind == TlmKind.Regio ? 165_000_000L : 4_800_000_000L;

    private string Dir(TlmKind kind) => Path.Combine(_root, kind == TlmKind.Regio ? "regio" : "tlm3d");

    /// <summary>Path of the extracted GeoPackage, or null when it is not available yet.</summary>
    public string? Find(TlmKind kind) =>
        Directory.GetFiles(Dir(kind), "*.gpkg")
            .Where(f => !Path.GetFileName(f).Contains("BOUNDARIES", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();

    public bool IsReady(TlmKind kind) => Find(kind) is not null;

    /// <summary>The swissTLMRegio boundaries GeoPackage (municipalities, districts, cantons), or null until Regio is downloaded.</summary>
    public string? FindBoundaries() =>
        Directory.GetFiles(Dir(TlmKind.Regio), "*.gpkg")
            .FirstOrDefault(f => Path.GetFileName(f).Contains("BOUNDARIES", StringComparison.OrdinalIgnoreCase));

    public ProgressInfo Status(TlmKind kind)
    {
        lock (_status) return _status.TryGetValue(kind, out var s) ? s : new ProgressInfo(IsReady(kind) ? "ready" : "idle", IsReady(kind) ? 100 : 0, IsReady(kind) ? "Ready" : "Not downloaded");
    }

    private void Report(TlmKind kind, ProgressInfo info, IProgress<ProgressInfo>? progress)
    {
        lock (_status) _status[kind] = info;
        progress?.Report(info);
    }

    /// <summary>Returns the GeoPackage path, downloading and extracting the newest release when missing.</summary>
    public async Task<string> EnsureAsync(TlmKind kind, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (Find(kind) is { } existing) return existing;
        await _gate.WaitAsync(ct);
        try
        {
            if (Find(kind) is { } again) return again;
            string name = DisplayName(kind);
            Report(kind, new ProgressInfo("tlm", 0, $"Finding the newest {name} release"), progress);
            var (id, url) = await NewestGpkgAsync(kind, ct);
            string zipPath = Path.Combine(Dir(kind), id + ".gpkg.zip");
            var bytes = new Progress<(long Received, long? Total)>(p =>
            {
                double pct = p.Total is { } t && t > 0 ? 95.0 * p.Received / t : 0;
                Report(kind, new ProgressInfo("tlm", pct, $"Downloading {name} {id}: {p.Received / 1_048_576} of {(p.Total ?? ApproxBytes(kind)) / 1_048_576} MB (one-time)"), progress);
            });
            await _downloader.GetFileAsync(url, zipPath, bytes, ct);
            Report(kind, new ProgressInfo("tlm", 96, $"Extracting {name}"), progress);
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entries = zip.Entries.Where(e => e.Name.EndsWith(".gpkg", StringComparison.OrdinalIgnoreCase)).ToList();
                if (entries.Count == 0) throw new InvalidDataException($"{name} archive contains no GeoPackage.");
                // Regio ships the product and a separate BOUNDARIES GeoPackage; keep both.
                foreach (var entry in entries)
                {
                    string target = Path.Combine(Dir(kind), entry.Name);
                    entry.ExtractToFile(target + ".part", overwrite: true);
                    File.Move(target + ".part", target, overwrite: true);
                }
            }, ct);
            try { File.Delete(zipPath); } catch (IOException) { }
            Report(kind, new ProgressInfo("ready", 100, $"{name} ready"), progress);
            return Find(kind) ?? throw new InvalidOperationException($"{name} extraction failed.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report(kind, new ProgressInfo("error", 0, ex.Message), progress);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(string Id, string Url)> NewestGpkgAsync(TlmKind kind, CancellationToken ct)
    {
        string json = await _downloader.GetStringAsync($"https://data.geo.admin.ch/api/stac/v0.9/collections/{Collection(kind)}/items?limit=100", ct);
        using var doc = JsonDocument.Parse(json);
        string? bestId = null, bestUrl = null;
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            string id = f.GetProperty("id").GetString() ?? "";
            if (!f.TryGetProperty("assets", out var assets)) continue;
            foreach (var a in assets.EnumerateObject())
            {
                if (!a.Name.EndsWith(".gpkg.zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (bestId is null || string.CompareOrdinal(id, bestId) > 0) { bestId = id; bestUrl = a.Value.GetProperty("href").GetString(); }
            }
        }
        if (bestId is null || bestUrl is null) throw new InvalidOperationException($"No GeoPackage release found for {DisplayName(kind)}.");
        return (bestId, bestUrl);
    }
}
