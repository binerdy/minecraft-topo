using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core;

public enum ElevationSourceKind
{
    /// <summary>swissALTI3D at 2 m or 0.5 m; falls back to the country model at coarse scales.</summary>
    Auto,
    SwissAlti3d,
    Dhm200,
    Synthetic,
}

public enum LandCoverKind
{
    /// <summary>swisstopo VECTOR25 primary surfaces via WMS (any area size).</summary>
    Vec25,
    /// <summary>swissTLM3D GeoPackage (official, detailed; 4.8 GB one-time download).</summary>
    Tlm3d,
    /// <summary>swissTLMRegio GeoPackage (official, 1:200 000; 160 MB one-time download).</summary>
    TlmRegio,
}

public enum BuildingModelKind
{
    /// <summary>swissBUILDINGS3D 3.0: measured heights and real roof shapes (any land cover source).</summary>
    SwissBuildings3d,
    /// <summary>Footprints from the land cover source extruded by storeys.</summary>
    Footprints,
}

/// <summary>Everything needed to generate one world.</summary>
public sealed record GenerationRequest
{
    public required Lv95Rect Area { get; init; }
    public required string WorldName { get; init; }
    /// <summary>Folder that will contain the world (i.e. the world folder itself, not the saves dir).</summary>
    public required string OutputDir { get; init; }
    public double MetresPerBlock { get; init; } = 1;
    public ElevationSourceKind Source { get; init; } = ElevationSourceKind.Auto;
    /// <summary>swissALTI3D resolution when that source is used: 2 or 0.5.</summary>
    public double Alti3dResolution { get; init; } = 2;
    public TerrainOptions Terrain { get; init; } = new();
    /// <summary>Where water and forest outlines come from.</summary>
    public LandCoverKind LandCover { get; init; } = LandCoverKind.Vec25;
    /// <summary>Where building shapes come from when buildings are enabled.</summary>
    public BuildingModelKind BuildingModel { get; init; } = BuildingModelKind.SwissBuildings3d;
    /// <summary>Delete an existing world of the same name before generating.</summary>
    public bool ReplaceExisting { get; init; }

    /// <summary>Block role overrides (see <see cref="Anvil.BlockRoles"/>): role key -> vanilla block name.</summary>
    public IReadOnlyDictionary<string, string>? Blocks { get; init; }

    /// <summary>World spawn in LV95 metres; null = centre of the area. Clamped into the area.</summary>
    public Lv95Point? Spawn { get; init; }

    /// <summary>Spawn column in block coordinates (x east, z south) inside the generated grid.</summary>
    public (int X, int Z) SpawnBlock()
    {
        if (Spawn is not { } s) return (BlocksWide / 2, BlocksHigh / 2);
        int x = (int)Math.Floor((s.E - Area.MinE) / MetresPerBlock);
        int z = (int)Math.Floor((Area.MaxN - s.N) / MetresPerBlock);
        return (Math.Clamp(x, 0, BlocksWide - 1), Math.Clamp(z, 0, BlocksHigh - 1));
    }

    /// <summary>Metres-per-block at or above which "Auto" uses the 200 m country model.</summary>
    public const double AutoDhm200Threshold = 25;

    public ElevationSourceKind ResolveSource() =>
        Source == ElevationSourceKind.Auto
            ? (MetresPerBlock >= AutoDhm200Threshold ? ElevationSourceKind.Dhm200 : ElevationSourceKind.SwissAlti3d)
            : Source;

    public int BlocksWide => Math.Max(1, (int)Math.Ceiling(Area.Width / MetresPerBlock - 1e-9));
    public int BlocksHigh => Math.Max(1, (int)Math.Ceiling(Area.Height / MetresPerBlock - 1e-9));
    public long TotalBlocks => (long)BlocksWide * BlocksHigh;
    public int ChunksWide => (BlocksWide + 15) / 16;
    public int ChunksHigh => (BlocksHigh + 15) / 16;
    public long TotalChunks => (long)ChunksWide * ChunksHigh;
    public int RegionsWide => (ChunksWide + 31) / 32;
    public int RegionsHigh => (ChunksHigh + 31) / 32;
    public long TotalRegions => (long)RegionsWide * RegionsHigh;
}

public sealed record GenerationResult(
    string OutputDir,
    long Blocks,
    long Chunks,
    long Regions,
    double VerticalScale,
    float MinElevation,
    float MaxElevation,
    int MinY,
    int MaxY,
    int SpawnX,
    int SpawnY,
    int SpawnZ,
    int WaterCells,
    int TreeCount,
    int FilledCells,
    bool WroteLevelDat,
    bool WroteWorldGenSettings,
    TimeSpan Elapsed);
