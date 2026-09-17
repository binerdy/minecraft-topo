using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>Something that can fill a <see cref="HeightGrid"/> with real elevations.</summary>
public interface IElevationSource
{
    /// <summary>Human-readable name, shown in logs and the UI.</summary>
    string Name { get; }

    /// <summary>Native resolution of the source in metres per sample.</summary>
    double NativeResolution { get; }

    /// <summary>Fills every cell of <paramref name="grid"/>; cells without coverage are left NaN.</summary>
    Task FillAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct);

    /// <summary>Estimate of the number of remote files and bytes needed to cover a rectangle (0 when local).</summary>
    Task<(int Files, long Bytes)> EstimateDownloadAsync(Lv95Rect rect, CancellationToken ct);
}
