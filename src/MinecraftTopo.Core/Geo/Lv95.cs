namespace MinecraftTopo.Core.Geo;

/// <summary>A WGS84 position in decimal degrees.</summary>
public readonly record struct LatLon(double Lat, double Lon);

/// <summary>A position in the Swiss LV95 grid (EPSG:2056), metres.</summary>
public readonly record struct Lv95Point(double E, double N);

/// <summary>An axis-aligned rectangle in LV95 metres.</summary>
public readonly record struct Lv95Rect(double MinE, double MinN, double MaxE, double MaxN)
{
    public double Width => MaxE - MinE;
    public double Height => MaxN - MinN;
    public Lv95Point Center => new((MinE + MaxE) / 2, (MinN + MaxN) / 2);

    public static Lv95Rect FromCorners(double e1, double n1, double e2, double n2) =>
        new(Math.Min(e1, e2), Math.Min(n1, n2), Math.Max(e1, e2), Math.Max(n1, n2));

    public Lv95Rect Expand(double metres) =>
        new(MinE - metres, MinN - metres, MaxE + metres, MaxN + metres);

    public bool Contains(double e, double n) => e >= MinE && e <= MaxE && n >= MinN && n <= MaxN;

    public bool Intersects(Lv95Rect o) =>
        o.MinE < MaxE && o.MaxE > MinE && o.MinN < MaxN && o.MaxN > MinN;

    /// <summary>WGS84 bounding box (west, south, east, north) that contains this rectangle.</summary>
    public (double West, double South, double East, double North) ToWgs84Bbox()
    {
        var corners = new[]
        {
            Lv95.ToWgs84(MinE, MinN), Lv95.ToWgs84(MaxE, MinN),
            Lv95.ToWgs84(MinE, MaxN), Lv95.ToWgs84(MaxE, MaxN),
        };
        return (corners.Min(c => c.Lon), corners.Min(c => c.Lat), corners.Max(c => c.Lon), corners.Max(c => c.Lat));
    }
}

/// <summary>
/// Approximate WGS84 &lt;-&gt; LV95 conversion using the formulas published by swisstopo
/// ("Approximate formulas for the transformation between Swiss projection coordinates and WGS84").
/// Accuracy is about 1 m, which is ample for a 1 m block grid.
/// </summary>
public static class Lv95
{
    /// <summary>LV95 extent of Switzerland (with a little margin).</summary>
    public static readonly Lv95Rect SwitzerlandExtent = new(2_480_000, 1_070_000, 2_840_000, 1_300_000);

    public static Lv95Point FromWgs84(double lat, double lon)
    {
        double phi = (lat * 3600 - 169_028.66) / 10_000;
        double lam = (lon * 3600 - 26_782.5) / 10_000;
        double phi2 = phi * phi, lam2 = lam * lam;

        double e = 2_600_072.37
                   + 211_455.93 * lam
                   - 10_938.51 * lam * phi
                   - 0.36 * lam * phi2
                   - 44.54 * lam2 * lam;

        double n = 1_200_147.07
                   + 308_807.95 * phi
                   + 3_745.25 * lam2
                   + 76.63 * phi2
                   - 194.56 * lam2 * phi
                   + 119.79 * phi2 * phi;

        return new Lv95Point(e, n);
    }

    public static Lv95Point FromWgs84(LatLon p) => FromWgs84(p.Lat, p.Lon);

    public static LatLon ToWgs84(double e, double n)
    {
        double y = (e - 2_600_000) / 1_000_000;
        double x = (n - 1_200_000) / 1_000_000;
        double y2 = y * y, x2 = x * x;

        double lam = 2.6779094
                     + 4.728982 * y
                     + 0.791484 * y * x
                     + 0.1306 * y * x2
                     - 0.0436 * y2 * y;

        double phi = 16.9023892
                     + 3.238272 * x
                     - 0.270978 * y2
                     - 0.002528 * x2
                     - 0.0447 * y2 * x
                     - 0.0140 * x2 * x;

        return new LatLon(phi * 100 / 36, lam * 100 / 36);
    }

    public static LatLon ToWgs84(Lv95Point p) => ToWgs84(p.E, p.N);
}

/// <summary>Web Mercator (EPSG:3857) slippy-map tile maths.</summary>
public static class WebMercator
{
    public const int TileSize = 256;

    /// <summary>Longitude of the west edge of tile column x at zoom z.</summary>
    public static double TileXToLon(double x, int z) => x / (1 << z) * 360.0 - 180.0;

    /// <summary>Latitude of the north edge of tile row y at zoom z.</summary>
    public static double TileYToLat(double y, int z)
    {
        double n = Math.PI - 2.0 * Math.PI * y / (1 << z);
        return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
    }

    public static double LonToTileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);

    public static double LatToTileY(double lat, int z)
    {
        double rad = lat * Math.PI / 180.0;
        return (1.0 - Math.Log(Math.Tan(rad) + 1.0 / Math.Cos(rad)) / Math.PI) / 2.0 * (1 << z);
    }
}
