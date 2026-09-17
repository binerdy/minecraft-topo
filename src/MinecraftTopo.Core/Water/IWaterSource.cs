using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Water;

/// <summary>Per-cell land cover flags aligned with a <see cref="HeightGrid"/> (row-major like the grid).</summary>
public sealed record LandCover(bool[] Water, bool[] Forest)
{
    public int WaterCount => Water.Count(b => b);
    public int ForestCount => Forest.Count(b => b);
}

/// <summary>Provides water (lakes, rivers) and forest masks for a grid.</summary>
public interface ILandCoverSource
{
    string Name { get; }

    Task<LandCover> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct);
}
