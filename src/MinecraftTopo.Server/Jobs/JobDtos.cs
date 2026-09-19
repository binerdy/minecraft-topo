using System.ComponentModel.DataAnnotations;
using MinecraftTopo.Core;

namespace MinecraftTopo.Server.Jobs;

public sealed record AreaDto(double MinE, double MinN, double MaxE, double MaxN);

public enum OutputMode
{
    Folder,
    Zip,
}

/// <summary>Body of POST /api/jobs.</summary>
public sealed record JobRequestDto
{
    [Required] public AreaDto Area { get; init; } = new(0, 0, 0, 0);
    [Required] public string WorldName { get; init; } = "";
    public double MetresPerBlock { get; init; } = 1;
    public ElevationSourceKind Source { get; init; } = ElevationSourceKind.Auto;
    public double Alti3dResolution { get; init; } = 2;
    public int BaseY { get; init; } = -60;
    /// <summary>"auto" (tall up to 2 m per block), "standard" (Y -64..319) or "tall" (Y -2032..2031 through a data pack).</summary>
    public string WorldHeight { get; init; } = "auto";
    public double? VerticalScale { get; init; }
    public double? WaterLevel { get; init; }
    public bool WaterBodies { get; init; } = true;
    public bool Trees { get; init; } = true;
    public bool Vegetation { get; init; } = true;
    public bool Resources { get; init; } = true;
    public LandCoverKind LandCover { get; init; } = LandCoverKind.Vec25;
    public BuildingModelKind BuildingModel { get; init; } = BuildingModelKind.SwissBuildings3d;
    public bool Roads { get; init; }
    public bool Rails { get; init; }
    public bool Buildings { get; init; }
    public bool PowerLines { get; init; }
    public bool Villagers { get; init; }
    public bool StreetSigns { get; init; }
    /// <summary>Underground rock types: "none", "gk500" or "geocover".</summary>
    public string Geology { get; init; } = "gk500";
    /// <summary>0 = today, else 2010, 1973 or 1850.</summary>
    public int GlacierYear { get; init; }
    public bool IceToBed { get; init; } = true;
    public bool LakeFloors { get; init; } = true;
    public bool VegetationHeights { get; init; }
    /// <summary>"none", "photo", "photoBlocks", "siegfried", "dufour" or "nationalMap".</summary>
    public string SurfaceStyle { get; init; } = "none";
    public bool PlaceNames { get; init; } = true;
    public bool Extras { get; init; } = true;
    public bool Jura3d { get; init; }
    public bool Wildlife { get; init; } = true;
    public bool Crops { get; init; } = true;
    public bool StreetLights { get; init; } = true;
    public bool RoofColours { get; init; } = true;
    /// <summary>"creative", "survival", "adventure" or "hardcore".</summary>
    public string GameMode { get; init; } = "creative";
    /// <summary>"peaceful", "easy", "normal" or "hard".</summary>
    public string Difficulty { get; init; } = "peaceful";
    /// <summary>Block role overrides: role key -> vanilla block name (see GET /api/blocks).</summary>
    public Dictionary<string, string>? Blocks { get; init; }
    public double SnowLine { get; init; } = 2500;
    public double SlopeStoneDegrees { get; init; } = 32;
    public OutputMode OutputMode { get; init; } = OutputMode.Folder;
    /// <summary>Saves folder (the world is created as a sub-folder named after the world). Folder mode only.</summary>
    public string? SavesDir { get; init; }
    /// <summary>Delete an existing world of the same name before generating.</summary>
    public bool ReplaceExisting { get; init; }
    /// <summary>Spawn point in LV95 metres; omit for the centre of the area.</summary>
    public double? SpawnE { get; init; }
    public double? SpawnN { get; init; }
}

public enum JobState
{
    Queued,
    Running,
    Done,
    Failed,
    Cancelled,
}

public sealed record JobDto(
    string Id,
    JobState State,
    string WorldName,
    OutputMode OutputMode,
    ProgressInfo Progress,
    IReadOnlyList<string> Log,
    GenerationResult? Result,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? DownloadUrl,
    string? MapUrl,
    int MapCellsPerPixel,
    int BlocksWide,
    int BlocksHigh,
    int? SpawnX,
    int? SpawnY,
    int? SpawnZ,
    bool SpawnEditable,
    IReadOnlyList<string> MapStages,
    string? MapStage,
    int ChunksWide,
    int ChunksHigh,
    /// <summary>Base64 of one byte per chunk, 1 = written; null before the first chunk.</summary>
    string? ChunkMask);

public sealed record SpawnRequestDto(int X, int Z);
