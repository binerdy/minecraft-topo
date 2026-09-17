using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Gpkg;

namespace MinecraftTopo.Core.Tlm;

public enum RegionLevel
{
    Municipality,
    District,
    Canton,
}

/// <summary>An administrative unit found under a map click.</summary>
public sealed record RegionInfo(
    string Name,
    RegionLevel Level,
    Lv95Rect Bounds,
    double AreaKm2,
    /// <summary>Outline rings (outer rings of every part, decimated for display) as [E, N] pairs.</summary>
    IReadOnlyList<IReadOnlyList<double[]>> Outline);

/// <summary>
/// Point-in-polygon lookup of municipalities, districts and cantons in the swissTLMRegio
/// boundaries GeoPackage (©swisstopo).
/// </summary>
public sealed class RegionLookup
{
    private readonly TlmDatasets _datasets;
    private const int MaxOutlinePoints = 2500;

    public RegionLookup(TlmDatasets datasets) => _datasets = datasets;

    public bool IsAvailable => _datasets.FindBoundaries() is not null;

    /// <summary>Returns the unit containing the point, or null when the point is outside every unit.</summary>
    public RegionInfo? Find(double e, double n, RegionLevel level)
    {
        string path = _datasets.FindBoundaries()
            ?? throw new InvalidOperationException("Region selection needs the swissTLMRegio dataset (160 MB one-time download). Download it under Terrain > Water and forest outlines, then try again.");
        using var gpkg = new GeoPackage(path);
        string? layer = level switch
        {
            RegionLevel.Municipality => gpkg.FindLayer("swisstlmregio_hoheitsgebiet", "hoheitsgebiet"),
            RegionLevel.District => gpkg.FindLayer("swisstlmregio_bezirksgebiet", "bezirksgebiet"),
            _ => gpkg.FindLayer("swisstlmregio_kantonsgebiet", "kantonsgebiet"),
        };
        if (layer is null) throw new InvalidOperationException($"The boundaries GeoPackage has no {level} layer.");

        var probe = new Lv95Rect(e - 0.5, n - 0.5, e + 0.5, n + 0.5);
        foreach (var f in gpkg.Query(layer, probe, "name", "NAME"))
        {
            var polygons = Polygons(f.Geometry).ToList();
            if (!polygons.Any(p => Contains(p, e, n))) continue;
            double minE = double.MaxValue, minN = double.MaxValue, maxE = double.MinValue, maxN = double.MinValue, area = 0;
            int totalPoints = 0;
            foreach (var p in polygons)
            {
                var outer = p.Rings[0];
                totalPoints += outer.Count;
                area += Math.Abs(RingArea(outer));
                for (int r = 1; r < p.Rings.Count; r++) area -= Math.Abs(RingArea(p.Rings[r]));
                foreach (var (x, y, _) in outer)
                {
                    if (x < minE) minE = x;
                    if (x > maxE) maxE = x;
                    if (y < minN) minN = y;
                    if (y > maxN) maxN = y;
                }
            }
            int stride = Math.Max(1, (totalPoints + MaxOutlinePoints - 1) / MaxOutlinePoints);
            var outline = new List<IReadOnlyList<double[]>>();
            foreach (var p in polygons)
            {
                var ring = p.Rings[0];
                var pts = new List<double[]>(ring.Count / stride + 2);
                for (int i = 0; i < ring.Count; i += stride) pts.Add([Math.Round(ring[i].X, 1), Math.Round(ring[i].Y, 1)]);
                if (pts.Count >= 3) outline.Add(pts);
            }
            string name = f.Text("name");
            if (name.Length == 0) name = f.Text("NAME");
            return new RegionInfo(name, level,
                new Lv95Rect(Math.Floor(minE), Math.Floor(minN), Math.Ceiling(maxE), Math.Ceiling(maxN)),
                area / 1e6, outline);
        }
        return null;
    }

    private static IEnumerable<PolygonGeometry> Polygons(Geometry g) => g switch
    {
        PolygonGeometry p => [p],
        MultiGeometry m => m.Parts.SelectMany(Polygons),
        _ => [],
    };

    /// <summary>Even-odd test over all rings: inside the outer ring and outside every hole.</summary>
    private static bool Contains(PolygonGeometry p, double x, double y)
    {
        bool inside = false;
        foreach (var ring in p.Rings)
        {
            if (RingContains(ring, x, y)) inside = !inside;
        }
        return inside;
    }

    private static bool RingContains(IReadOnlyList<(double X, double Y, double Z)> ring, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (xi, yi, _) = ring[i];
            var (xj, yj, _) = ring[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private static double RingArea(IReadOnlyList<(double X, double Y, double Z)> ring)
    {
        double a = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) a += (ring[j].X + ring[i].X) * (ring[j].Y - ring[i].Y);
        return a / 2;
    }
}
