using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Imaging;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Glaciers;

/// <summary>Which glacier state to build. Today uses the elevation model as surveyed.</summary>
public enum GlacierYear
{
    Today = 0,
    Y2010 = 2010,
    Y1973 = 1973,
    /// <summary>Little Ice Age maximum around 1850.</summary>
    Y1850 = 1850,
}

/// <summary>Glacier extents and ice thickness per grid cell.</summary>
public sealed class GlacierModel
{
    /// <summary>Latest inventory that still had ice on the cell: 0 none, 1 = 1850 only, 2 = 1973, 3 = 2010, 4 = 2016.</summary>
    public required byte[] Extent { get; init; }
    /// <summary>Present ice thickness in metres (0 where unknown or ice-free).</summary>
    public required float[] Thickness { get; init; }
    public int Cells1850 { get; init; }
    public int CellsToday { get; init; }

    public static int ClassFor(GlacierYear year) => year switch
    {
        GlacierYear.Y1850 => 1,
        GlacierYear.Y1973 => 2,
        GlacierYear.Y2010 => 3,
        _ => 4,
    };
}

/// <summary>
/// Glacier inventories (GLAMOS, published by swisstopo): the extents of 1850, 1973, 2010 and 2016
/// and the modelled ice thickness, read from the geo.admin.ch WMS layers.
/// </summary>
public sealed class GlacierSource
{
    public const string ExtentLayer = "ch.swisstopo.geologie-gletscherausdehnung";
    public const string ThicknessLayer = "ch.swisstopo.geologie-gletschermaechtigkeit";
    private const double MinPixelMetres = 10;

    // The extent layer draws the inventories stacked: 1850 at the bottom, then 1973, 2010, 2016 and
    // the 2016 debris cover on top. A pixel's colour is therefore the latest inventory with ice.
    private static readonly (byte R, byte G, byte B)[] ExtentColours =
    [
        (0, 77, 168),     // 1850
        (0, 120, 255),    // 1973
        (115, 223, 255),  // 2010
        (190, 255, 232),  // 2016
        (205, 137, 102),  // 2016 debris-covered
    ];

    // Thickness classes: <= 25, 26-50, 51-100, 101-200, 201-300, 301-500, > 500 m
    private static readonly (byte R, byte G, byte B)[] ThicknessColours =
    [
        (190, 220, 255), (140, 210, 245), (100, 190, 230), (60, 160, 210), (30, 120, 180), (20, 80, 150), (10, 50, 100),
    ];
    private static readonly float[] ThicknessMid = [15, 38, 75, 150, 250, 400, 600];

    private readonly WmsMask _wms;

    public GlacierSource(Downloader downloader, AppPaths paths) => _wms = new WmsMask(downloader, paths.GlacierCacheDir);

    private static byte ClassifyExtent(byte r, byte g, byte b, byte a)
    {
        if (a < 100) return 0;
        int i = WmsMask.Nearest(ExtentColours, r, g, b, 60);
        return i < 0 ? (byte)0 : i == 4 ? (byte)4 : (byte)(i + 1);
    }

    private static byte ClassifyThickness(byte r, byte g, byte b, byte a)
    {
        if (a < 100) return 0;
        int i = WmsMask.Nearest(ThicknessColours, r, g, b, 40);
        return i < 0 ? (byte)0 : (byte)(i + 1);
    }

    /// <summary>Returns null when no inventory has ice inside the grid.</summary>
    public async Task<GlacierModel?> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var (ext, pw, step) = await _wms.FetchPixelsAsync(ExtentLayer, grid, MinPixelMetres, true, "glaciers", "glacier extents 1850-2016", ClassifyExtent, progress, ct);
        int ph = ext.Length / pw;
        // Outline pixels (dark, anti-aliased) become 0; fill them from neighbours so extents stay closed.
        WmsMask.FillZeros(ext, pw, ph, 4, passes: 1);
        int n = grid.Width * grid.Height;
        var extent = new byte[n];
        int cells1850 = 0, cellsToday = 0;
        for (int z = 0; z < grid.Height; z++)
        {
            int prow = (z / step) * pw, row = z * grid.Width;
            for (int x = 0; x < grid.Width; x++)
            {
                byte c = ext[prow + x / step];
                extent[row + x] = c;
                if (c > 0) cells1850++;
                if (c == 4) cellsToday++;
            }
        }
        if (cells1850 == 0)
        {
            progress?.Report(new ProgressInfo("glaciers", 100, "No glaciers in this area (not even in 1850)"));
            return null;
        }

        var thickness = new float[n];
        if (cellsToday > 0)
        {
            var (th, tpw, tstep) = await _wms.FetchPixelsAsync(ThicknessLayer, grid, MinPixelMetres, true, "glaciers", "ice thickness", ClassifyThickness, progress, ct);
            int tph = th.Length / tpw;
            WmsMask.FillZeros(th, tpw, tph, 7, passes: 1);
            for (int z = 0; z < grid.Height; z++)
            {
                int prow = (z / tstep) * tpw, row = z * grid.Width;
                for (int x = 0; x < grid.Width; x++)
                {
                    byte c = th[prow + x / tstep];
                    if (c > 0) thickness[row + x] = ThicknessMid[c - 1];
                }
            }
        }
        progress?.Report(new ProgressInfo("glaciers", 100,
            $"Glaciers: {100.0 * cells1850 / n:0.#}% of the area had ice in 1850, {100.0 * cellsToday / n:0.#}% in 2016"));
        return new GlacierModel { Extent = extent, Thickness = thickness, Cells1850 = cells1850, CellsToday = cellsToday };
    }
}

/// <summary>The ice to build: a per-cell mask with the bed elevation under the ice.</summary>
public sealed class IceModel
{
    public required bool[] Ice { get; init; }
    /// <summary>Elevation of the ground under the ice (m).</summary>
    public required float[] Bed { get; init; }
    public int Cells { get; init; }
    public float MaxThickness { get; init; }
}

public static class GlacierPlanner
{
    /// <summary>Glacier profile: ice thickness from the distance to the margin (Nye, yield stress ~1 bar).</summary>
    private static double Thickness(double distanceMetres) => Math.Min(900, 4.7 * Math.Sqrt(distanceMetres));

    /// <summary>
    /// Raises the surface to the chosen year's glaciers and returns the ice mask with the bed.
    /// For <see cref="GlacierYear.Today"/> the surface stays as surveyed; only the bed under the
    /// present glaciers is derived from the thickness model so ice reaches down to the rock.
    /// </summary>
    public static IceModel Apply(HeightGrid grid, GlacierModel model, GlacierYear year, bool[]? coverGlacier, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        int w = grid.Width, h = grid.Height, n = w * h;
        int wanted = GlacierModel.ClassFor(year);
        var ice = new bool[n];
        var bed = new float[n];
        var data = grid.Data;

        // Present glaciers: bed = surface - modelled thickness.
        for (int i = 0; i < n; i++)
        {
            bool present = model.Extent[i] == 4 || (coverGlacier is not null && coverGlacier[i]);
            bed[i] = present ? data[i] - model.Thickness[i] : data[i];
            if (present) ice[i] = true;
        }

        int cells = 0;
        float maxThick = 0;
        if (year != GlacierYear.Today)
        {
            // Historic extent: distance to the ice margin drives the thickness of the vanished ice.
            var inExtent = new bool[n];
            for (int i = 0; i < n; i++) inExtent[i] = model.Extent[i] >= wanted && model.Extent[i] != 0;
            var dist = DistanceToMargin(inExtent, w, h, grid.CellSize, ct);
            for (int i = 0; i < n; i++)
            {
                if (!inExtent[i]) continue;
                float surface = (float)Math.Max(data[i], bed[i] + Thickness(dist[i]));
                float thick = surface - bed[i];
                if (thick > maxThick) maxThick = thick;
                data[i] = surface;
                ice[i] = true;
            }
        }
        for (int i = 0; i < n; i++) if (ice[i]) cells++;
        progress?.Report(new ProgressInfo("glaciers", 100,
            year == GlacierYear.Today
                ? $"Ice down to the bed on {cells:N0} glacier columns"
                : $"Glaciers of {(int)year}: {cells:N0} ice columns, up to {maxThick:0} m thick over today's surface"));
        return new IceModel { Ice = ice, Bed = bed, Cells = cells, MaxThickness = maxThick };
    }

    /// <summary>Two-pass chamfer distance (metres) from every masked cell to the nearest unmasked cell; grid edges do not count as margins.</summary>
    private static float[] DistanceToMargin(bool[] mask, int w, int h, double cellSize, CancellationToken ct)
    {
        const float Inf = 1e9f;
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = mask[i] ? Inf : 0;
        float a = (float)cellSize, b = (float)(cellSize * 1.41421356);
        for (int z = 0; z < h; z++)
        {
            ct.ThrowIfCancellationRequested();
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (d[i] == 0) continue;
                float v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + a);
                if (z > 0)
                {
                    v = Math.Min(v, d[i - w] + a);
                    if (x > 0) v = Math.Min(v, d[i - w - 1] + b);
                    if (x < w - 1) v = Math.Min(v, d[i - w + 1] + b);
                }
                d[i] = v;
            }
        }
        for (int z = h - 1; z >= 0; z--)
        {
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x;
                if (d[i] == 0) continue;
                float v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + a);
                if (z < h - 1)
                {
                    v = Math.Min(v, d[i + w] + a);
                    if (x < w - 1) v = Math.Min(v, d[i + w + 1] + b);
                    if (x > 0) v = Math.Min(v, d[i + w - 1] + b);
                }
                d[i] = v;
            }
        }
        // Cells that never saw a margin (ice fills the whole area) get a large but finite distance.
        for (int i = 0; i < d.Length; i++) if (d[i] >= Inf) d[i] = 40000;
        return d;
    }
}
