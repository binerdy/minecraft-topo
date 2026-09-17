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
    public int BaseY { get; init; } = 0;
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
    /// <summary>Underground rock types from the GK500 geological map.</summary>
    public bool Geology { get; init; } = true;
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
    string? DownloadUrl);
