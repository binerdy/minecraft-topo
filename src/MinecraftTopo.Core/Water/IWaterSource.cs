using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Water;

/// <summary>Road surface materials, resolved from the road class and surface attributes.</summary>
public enum RoadMaterial : byte
{
    None = 0,
    Asphalt = 1,       // gray concrete
    LightAsphalt = 2,  // light gray concrete
    DirtPath = 3,
    Gravel = 4,
    Cobblestone = 5,
    StoneBricks = 6,
    /// <summary>Outdoor stairways: stone brick stairs facing uphill.</summary>
    Stairs = 7,
}

/// <summary>Bits stored per cell in <see cref="LandCover.DeckFlags"/> for roads, railways and platforms that leave the ground.</summary>
public static class DeckFlag
{
    public const byte Bridge = 1;
    public const byte Tunnel = 2;
    /// <summary>A pier stands under this deck cell.</summary>
    public const byte Pier = 4;
    /// <summary>Covered wooden bridge.</summary>
    public const byte Covered = 8;
    /// <summary>Edge of the deck: railing.</summary>
    public const byte Railing = 16;
    /// <summary>Tunnel ceiling light above this cell.</summary>
    public const byte Light = 32;
    /// <summary>A free-standing deck without road or rail (ski jump inrun).</summary>
    public const byte Platform = 64;
}

/// <summary>Building type from the landscape model, per cell in <see cref="LandCover.BuildingKind"/>.</summary>
public enum BuildingKind : byte
{
    House = 0,
    /// <summary>Open building, canopy, petrol station roof: posts and a roof, no walls.</summary>
    Open = 1,
    Tank = 2,
    Greenhouse = 3,
    Construction = 4,
    Tower = 5,
    Chimney = 6,
    HighRise = 7,
    Stadium = 8,
    Observatory = 9,
    CarPark = 10,
    BigWall = 11,
    /// <summary>Enclosed walkway between buildings.</summary>
    Walkway = 12,
    Reservoir = 13,
}

/// <summary>Bits stored per cell in <see cref="LandCover.RoadFlags"/>.</summary>
public static class RoadFlag
{
    public const byte Marking = 1; // white lane or edge line on top
    public const byte Bridge = 2;
}

/// <summary>Bits stored per cell in <see cref="LandCover.Rail"/>.</summary>
public static class RailFlag
{
    public const byte Bed = 1;    // gravel bed
    public const byte Track = 2;  // the rail itself (4-connected centre line)
    public const byte Bridge = 4;
}

/// <summary>Bits stored per cell in <see cref="LandCover.BuildingFlags"/>.</summary>
public static class BuildingFlag
{
    public const byte Industrial = 1; // flat gray roof instead of bricks
    public const byte Edge = 2;       // footprint boundary cell: wall
    public const byte Church = 4;     // stone walls, slate roof, tower with spire
}

/// <summary>Which optional infrastructure classes to fetch and rasterise.</summary>
public sealed record LandCoverOptions(bool Roads = false, bool Rails = false, bool Buildings = false, bool Power = false, bool Signs = false, bool Extras = false, bool Lights = false);

/// <summary>A standing sign with up to four lines of text at grid cell (X, Z), facing <see cref="Rotation"/> (0-15, 0 = south).</summary>
public sealed record SignSpec(int X, int Z, byte Rotation, string[] Lines);

/// <summary>Surface classes beyond water and forest, from swisstopo land cover (per cell in <see cref="LandCover.Cover"/>).</summary>
public enum CoverClass : byte
{
    None = 0,
    Rock = 1,       // bare rock: stone
    Scree = 2,      // loose rock: gravel
    Glacier = 3,    // packed ice under snow
    Wetland = 4,    // mud with reeds
    Orchard = 5,    // grass with sparse oak trees in rows
    Vineyard = 6,   // grass with rows of bushes
    Settlement = 7, // no change, informational
}

/// <summary>A point of interest at a grid cell, e.g. a church that styles the building under it.</summary>
public sealed record PointOfInterest(int X, int Z, string Kind, string Name);

public enum StructureKind : byte
{
    /// <summary>High-voltage pylon (node of a power=line way).</summary>
    Tower,
    /// <summary>Wooden pole (node of a power=minor_line way).</summary>
    Pole,
    /// <summary>Wind turbine (power=generator with generator:source=wind).</summary>
    WindTurbine,
    /// <summary>Spire and cross on top of a church tower (BaseY = top of the tower).</summary>
    ChurchSpire,
    /// <summary>Radio or mobile antenna mast; Size = height in metres.</summary>
    Antenna,
    /// <summary>Bus shelter: two posts and a roof.</summary>
    BusShelter,
    /// <summary>Cable car, chair lift or ski lift mast; Size = height in metres.</summary>
    LiftMast,
    /// <summary>Summit cross.</summary>
    SummitCross,
    /// <summary>Village fountain: stone basin with water.</summary>
    Fountain,
    /// <summary>Monument or wayside shrine: a small pillar.</summary>
    Monument,
    /// <summary>Wayside shrine (Bildstock): post with a lantern.</summary>
    Shrine,
    /// <summary>Boundary stone or survey pyramid.</summary>
    Marker,
}

/// <summary>
/// A point structure at grid cell (X, Z). <see cref="DirX"/>/<see cref="DirZ"/> is the line direction
/// for pylons; <see cref="Size"/> is the footprint width for spires; <see cref="BaseY"/> is an absolute
/// base level, or int.MinValue to stand on the ground.
/// </summary>
public readonly record struct Structure(StructureKind Kind, int X, int Z, double DirX, double DirZ, int Size = 1, int BaseY = int.MinValue);

/// <summary>An overhead wire between two structures, in fractional grid coordinates.</summary>
public readonly record struct Wire(bool HighVoltage, double X0, double Z0, double X1, double Z1, int HeightMetres = 0);

/// <summary>Per-cell land cover aligned with a <see cref="HeightGrid"/> (row-major like the grid).</summary>
public sealed class LandCover
{
    public required bool[] Water { get; init; }
    public required bool[] Forest { get; init; }

    /// <summary>Road material per cell (0 = none), the widest road winning at junctions.</summary>
    public byte[]? Road { get; init; }
    public byte[]? RoadFlags { get; init; }
    /// <summary>Railway bits per cell, see <see cref="RailFlag"/>.</summary>
    public byte[]? Rail { get; init; }
    /// <summary>Building id per cell (0 = none); ids are only unique within one land cover.</summary>
    public int[]? BuildingId { get; init; }
    /// <summary>Building height in storeys per cell (footprints).</summary>
    public byte[]? BuildingLevels { get; init; }
    public byte[]? BuildingFlags { get; init; }
    /// <summary>Measured roof height per cell in metres above sea level (swissBUILDINGS3D), NaN where no building.</summary>
    public float[]? RoofHeight { get; init; }
    /// <summary>Measured floor height per cell in metres above sea level, NaN where unknown.</summary>
    public float[]? FloorHeight { get; init; }
    /// <summary>Top-block override per cell from extras (runways, platforms, dams, allotments), 0 = none.</summary>
    public byte[]? Surface { get; init; }
    /// <summary>Block standing on the ground per cell (walls, fences, jetties), 0 = none.</summary>
    public byte[]? Wall { get; init; }
    /// <summary>Surveyed height (m a.s.l.) of a road, railway or platform where it is a bridge, tunnel or free-standing deck; NaN elsewhere.</summary>
    public float[]? Deck { get; init; }
    public byte[]? DeckFlags { get; init; }
    /// <summary>Building type per cell (see <see cref="Water.BuildingKind"/>).</summary>
    public byte[]? BuildingKind { get; init; }

    /// <summary>
    /// Returns a copy whose buildings come from a measured model. Footprint building flags (church,
    /// industrial) are kept for cells where both agree on there being a building.
    /// </summary>
    public LandCover WithBuildingModel(Buildings.BuildingModel model)
    {
        int n = Water.Length;
        var flags = new byte[n];
        byte[]? kinds = null;
        if (BuildingId is not null && BuildingFlags is not null)
        {
            kinds = BuildingKind is null ? null : new byte[n];
            for (int i = 0; i < n; i++)
                if (model.Id[i] != 0 && BuildingId[i] != 0)
                {
                    flags[i] = (byte)(BuildingFlags[i] & (BuildingFlag.Church | BuildingFlag.Industrial));
                    if (kinds is not null) kinds[i] = BuildingKind![i];
                }
        }
        return new LandCover
        {
            Water = Water, Forest = Forest, Road = Road, RoadFlags = RoadFlags, Rail = Rail,
            BuildingId = model.Id, BuildingLevels = null, BuildingFlags = flags, RoofHeight = model.RoofHeight, FloorHeight = model.FloorHeight,
            Structures = Structures, Wires = Wires, Signs = Signs, Cover = Cover, Points = Points, SingleTrees = SingleTrees,
            Surface = Surface, Wall = Wall, Deck = Deck, DeckFlags = DeckFlags, BuildingKind = kinds,
        };
    }

    public static LandCover Empty(int cells) => new() { Water = new bool[cells], Forest = new bool[cells] };
    /// <summary>Pylons, poles and wind turbines.</summary>
    public List<Structure> Structures { get; init; } = [];
    /// <summary>Overhead wires between consecutive pylons or poles.</summary>
    public List<Wire> Wires { get; init; } = [];
    /// <summary>Street name signs.</summary>
    public List<SignSpec> Signs { get; init; } = [];
    /// <summary>Extra surface classes per cell (see <see cref="CoverClass"/>), null when the source has none.</summary>
    public byte[]? Cover { get; init; }
    /// <summary>Points of interest such as churches.</summary>
    public List<PointOfInterest> Points { get; init; } = [];
    /// <summary>Individually mapped trees outside forests (grid cells).</summary>
    public List<(int X, int Z)> SingleTrees { get; init; } = [];

    public int WaterCount => Water.Count(b => b);
    public int ForestCount => Forest.Count(b => b);
    public int RoadCount => Road?.Count(b => b != 0) ?? 0;
    public int RailCount => Rail?.Count(b => (b & RailFlag.Track) != 0) ?? 0;
    public int BuildingCellCount => BuildingId?.Count(b => b != 0) ?? 0;
}

/// <summary>Provides water, forest and optionally road, rail and building masks for a grid.</summary>
public interface ILandCoverSource
{
    string Name { get; }

    /// <summary>True when the source can deliver roads, rails and buildings.</summary>
    bool SupportsInfrastructure { get; }

    Task<LandCover> GetAsync(HeightGrid grid, LandCoverOptions options, IProgress<ProgressInfo>? progress, CancellationToken ct);
}
