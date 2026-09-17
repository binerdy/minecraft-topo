using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>Deterministic rolling terrain for offline tests: no network needed.</summary>
public sealed class SyntheticSource : IElevationSource
{
    public string Name => "synthetic";
    public double NativeResolution => 1;

    public Task FillAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        progress?.Report(new ProgressInfo("sample", 0, "Generating synthetic terrain"));
        for (int z = 0; z < grid.Height; z++)
        {
            ct.ThrowIfCancellationRequested();
            double n = grid.CellCenterN(z);
            for (int x = 0; x < grid.Width; x++)
            {
                double e = grid.CellCenterE(x);
                double h = 400
                           + 60 * Math.Sin(e / 90.0) * Math.Cos(n / 130.0)
                           + 25 * Math.Sin((e + n) / 40.0)
                           + 0.15 * (e - grid.OriginE);
                grid[x, z] = (float)h;
            }
        }
        progress?.Report(new ProgressInfo("sample", 100, "Synthetic terrain ready"));
        return Task.CompletedTask;
    }

    public Task<(int Files, long Bytes)> EstimateDownloadAsync(Lv95Rect rect, CancellationToken ct) =>
        Task.FromResult((0, 0L));
}
