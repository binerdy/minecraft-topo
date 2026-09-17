using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Elevation;

/// <summary>Elevation source backed by the in-memory country model (200 m resolution).</summary>
public sealed class Dhm200Source : IElevationSource
{
    private readonly Dhm200Model _model;

    public Dhm200Source(Dhm200Model model)
    {
        _model = model;
    }

    public string Name => "swisstopo DHM25 / 200 m";
    public double NativeResolution => 200;

    public async Task FillAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var model = await _model.GetAsync(progress, ct);
        progress?.Report(new ProgressInfo("sample", 0, "Sampling DHM200"));
        await Task.Run(() =>
        {
            Parallel.For(0, grid.Height, new ParallelOptions { CancellationToken = ct }, z =>
            {
                double n = grid.CellCenterN(z);
                for (int x = 0; x < grid.Width; x++)
                {
                    double e = grid.CellCenterE(x);
                    grid[x, z] = model.ContainsPoint(e, n) ? model.SampleBilinear(e, n) : float.NaN;
                }
            });
        }, ct);
        progress?.Report(new ProgressInfo("sample", 100, "Sampling done"));
    }

    public Task<(int Files, long Bytes)> EstimateDownloadAsync(Lv95Rect rect, CancellationToken ct) =>
        Task.FromResult((0, 0L));
}
