using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Water;

/// <summary>Scanline polygon fill and thick-line stroke on a cell grid.</summary>
public sealed class GridRasterizer
{
    private readonly int _w, _h;
    private readonly List<double> _crossings = new(64);

    public GridRasterizer(HeightGrid grid)
    {
        _w = grid.Width;
        _h = grid.Height;
    }

    public void FillRings(List<List<(double X, double Z)>> rings, bool[] mask) =>
        FillRingsWith(rings, cell => mask[cell] = true);

    /// <summary>Even-odd fill of one polygon (outer ring plus holes), calling <paramref name="paint"/> per cell.</summary>
    public void FillRingsWith(List<List<(double X, double Z)>> rings, Action<int> paint)
    {
        double minZ = double.MaxValue, maxZ = double.MinValue;
        foreach (var ring in rings)
            foreach (var p in ring) { if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z; }
        int z0 = Math.Max(0, (int)Math.Floor(minZ)), z1 = Math.Min(_h - 1, (int)Math.Ceiling(maxZ));
        for (int z = z0; z <= z1; z++)
        {
            double zc = z + 0.5;
            _crossings.Clear();
            foreach (var ring in rings)
            {
                int n = ring.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % n];
                    if ((p.Z <= zc) == (q.Z <= zc)) continue;
                    _crossings.Add(p.X + (zc - p.Z) * (q.X - p.X) / (q.Z - p.Z));
                }
            }
            if (_crossings.Count < 2) continue;
            _crossings.Sort();
            for (int i = 0; i + 1 < _crossings.Count; i += 2)
            {
                int xa = Math.Max(0, (int)Math.Ceiling(_crossings[i] - 0.5));
                int xb = Math.Min(_w - 1, (int)Math.Floor(_crossings[i + 1] - 0.5));
                for (int x = xa; x <= xb; x++) paint(z * _w + x);
            }
        }
    }

    /// <summary>
    /// Calls <paramref name="paint"/>(cell, distance from the centreline in cells, arc length in cells)
    /// for every cell whose centre lies within <paramref name="halfWidth"/> cells of the polyline.
    /// </summary>
    public void StrokeLine(List<(double X, double Z)> pts, double halfWidth, Action<int, double, double> paint)
    {
        double hw = Math.Max(halfWidth, 0.5);
        double hw2 = hw * hw;
        double arc = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, az) = pts[i];
            var (bx, bz) = pts[i + 1];
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - hw)), x1 = Math.Min(_w - 1, (int)Math.Ceiling(Math.Max(ax, bx) + hw));
            int z0 = Math.Max(0, (int)Math.Floor(Math.Min(az, bz) - hw)), z1 = Math.Min(_h - 1, (int)Math.Ceiling(Math.Max(az, bz) + hw));
            double dx = bx - ax, dz = bz - az, len2 = dx * dx + dz * dz, len = Math.Sqrt(len2);
            if (x0 <= x1 && z0 <= z1)
            {
                for (int z = z0; z <= z1; z++)
                {
                    double cz = z + 0.5;
                    for (int x = x0; x <= x1; x++)
                    {
                        double cx = x + 0.5;
                        double t = len2 > 0 ? Math.Clamp(((cx - ax) * dx + (cz - az) * dz) / len2, 0, 1) : 0;
                        double px = ax + t * dx - cx, pz = az + t * dz - cz;
                        double d2 = px * px + pz * pz;
                        if (d2 <= hw2) paint(z * _w + x, Math.Sqrt(d2), arc + t * len);
                    }
                }
            }
            arc += len;
        }
    }

    /// <summary>Marks a 4-connected one-cell-wide path along the polyline (what rails need to link up).</summary>
    public void StrokePath4(List<(double X, double Z)> pts, Action<int> paint)
    {
        int px = int.MinValue, pz = int.MinValue;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, az) = pts[i];
            var (bx, bz) = pts[i + 1];
            double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            int steps = Math.Max(1, (int)Math.Ceiling(len * 4));
            for (int s = 0; s <= steps; s++)
            {
                double t = (double)s / steps;
                int cx = (int)Math.Floor(ax + t * (bx - ax));
                int cz = (int)Math.Floor(az + t * (bz - az));
                if (cx == px && cz == pz) continue;
                if (px != int.MinValue && cx != px && cz != pz)
                {
                    Mark(cx, pz); // insert the corner so the path stays 4-connected
                }
                Mark(cx, cz);
                px = cx; pz = cz;
            }
        }

        void Mark(int x, int z)
        {
            if (x >= 0 && z >= 0 && x < _w && z < _h) paint(z * _w + x);
        }
    }
}

