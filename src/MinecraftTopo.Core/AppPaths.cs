namespace MinecraftTopo.Core;

/// <summary>Well-known folders used by the application.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string CacheDir => Path.Combine(Root, "cache");
    public string Dhm200CacheDir => Path.Combine(CacheDir, "dhm200");
    public string Alti3dCacheDir => Path.Combine(CacheDir, "alti3d");
    // v3: tiles include water and forest; older folders are stale.
    public string OverviewTileCacheDir => Path.Combine(CacheDir, "overview-tiles-v3");
    public string WaterCacheDir => Path.Combine(CacheDir, "water");
    public string Buildings3dCacheDir => Path.Combine(CacheDir, "buildings3d");
    public string GeologyCacheDir => Path.Combine(CacheDir, "geology");
    public string JobsDir => Path.Combine(Root, "jobs");

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "MinecraftTopo");
        Directory.CreateDirectory(Dhm200CacheDir);
        Directory.CreateDirectory(Alti3dCacheDir);
        Directory.CreateDirectory(OverviewTileCacheDir);
        Directory.CreateDirectory(WaterCacheDir);
        Directory.CreateDirectory(Buildings3dCacheDir);
        Directory.CreateDirectory(GeologyCacheDir);
        Directory.CreateDirectory(JobsDir);
    }

    /// <summary>Default Minecraft Java Edition saves folder for the current user, if it exists.</summary>
    public static string? DefaultMinecraftSavesDir()
    {
        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrEmpty(appData))
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        if (string.IsNullOrEmpty(appData)) return null;
        string saves = Path.Combine(appData, ".minecraft", "saves");
        return saves;
    }

    public long CacheSizeBytes()
    {
        long total = 0;
        if (!Directory.Exists(CacheDir)) return 0;
        foreach (var f in Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; } catch (IOException) { }
        }
        return total;
    }

    public void ClearCache(bool includeDhm200)
    {
        foreach (var dir in new[] { Alti3dCacheDir, OverviewTileCacheDir, WaterCacheDir, Path.Combine(CacheDir, "overview-tiles"), Path.Combine(CacheDir, "overview-tiles-v2") })
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(Alti3dCacheDir);
        Directory.CreateDirectory(OverviewTileCacheDir);
        Directory.CreateDirectory(WaterCacheDir);
        if (includeDhm200 && Directory.Exists(Dhm200CacheDir))
        {
            Directory.Delete(Dhm200CacheDir, recursive: true);
            Directory.CreateDirectory(Dhm200CacheDir);
        }
    }
}
