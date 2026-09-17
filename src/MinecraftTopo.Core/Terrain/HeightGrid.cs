namespace MinecraftTopo.Core.Terrain;

/// <summary>
/// A regular grid of elevations in LV95 metres. Row 0 is the northern-most row, column 0 the
/// western-most column. <see cref="OriginE"/>/<see cref="OriginN"/> is the north-west corner of
/// cell (0,0); the centre of cell (x,z) is at (OriginE + (x+0.5)*CellSize, OriginN - (z+0.5)*CellSize).
/// Missing values are stored as <see cref="float.NaN"/>.
/// </summary>
public sealed class HeightGrid
{
    public double OriginE { get; }
    public double OriginN { get; }
    public double CellSize { get; }
    public int Width { get; }
    public int Height { get; }
    public float[] Data { get; }

    public HeightGrid(double originE, double originN, double cellSize, int width, int height, float[]? data = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Grid must have a positive size.");
        OriginE = originE;
        OriginN = originN;
        CellSize = cellSize;
        Width = width;
        Height = height;
        Data = data ?? new float[checked((long)width * height) > int.MaxValue
            ? throw new ArgumentOutOfRangeException(nameof(width), "Grid is too large.")
            : width * height];
        if (Data.Length != width * height) throw new ArgumentException("Data length does not match grid size.", nameof(data));
    }

    public float this[int x, int z]
    {
        get => Data[z * Width + x];
        set => Data[z * Width + x] = value;
    }

    public double CellCenterE(int x) => OriginE + (x + 0.5) * CellSize;
    public double CellCenterN(int z) => OriginN - (z + 0.5) * CellSize;

    public double MinE => OriginE;
    public double MaxE => OriginE + Width * CellSize;
    public double MaxN => OriginN;
    public double MinN => OriginN - Height * CellSize;

    public bool ContainsPoint(double e, double n) => e >= MinE && e < MaxE && n > MinN && n <= MaxN;

    /// <summary>Index of the cell containing (e, n), or (-1,-1) when outside.</summary>
    public (int X, int Z) CellOf(double e, double n)
    {
        int x = (int)Math.Floor((e - OriginE) / CellSize);
        int z = (int)Math.Floor((OriginN - n) / CellSize);
        if (x < 0 || z < 0 || x >= Width || z >= Height) return (-1, -1);
        return (x, z);
    }

    /// <summary>
    /// Bilinear interpolation between cell centres. Coordinates outside the grid are clamped to the
    /// edge. Returns NaN when any of the four contributing samples is NaN.
    /// </summary>
    public float SampleBilinear(double e, double n)
    {
        double fx = (e - OriginE) / CellSize - 0.5;
        double fz = (OriginN - n) / CellSize - 0.5;
        fx = Math.Clamp(fx, 0, Width - 1);
        fz = Math.Clamp(fz, 0, Height - 1);
        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Math.Min(x0 + 1, Width - 1), z1 = Math.Min(z0 + 1, Height - 1);
        float tx = (float)(fx - x0), tz = (float)(fz - z0);

        float a = Data[z0 * Width + x0], b = Data[z0 * Width + x1];
        float c = Data[z1 * Width + x0], d = Data[z1 * Width + x1];
        float top = a + (b - a) * tx;
        float bottom = c + (d - c) * tx;
        return top + (bottom - top) * tz;
    }

    /// <summary>Nearest cell value, NaN outside.</summary>
    public float SampleNearest(double e, double n)
    {
        var (x, z) = CellOf(e, n);
        return x < 0 ? float.NaN : Data[z * Width + x];
    }

    /// <summary>Min/max over non-NaN values. Returns (NaN, NaN) when the grid has no valid cell.</summary>
    public (float Min, float Max) Range()
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var v in Data)
        {
            if (float.IsNaN(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        return float.IsPositiveInfinity(min) ? (float.NaN, float.NaN) : (min, max);
    }

    public int CountNaN()
    {
        int n = 0;
        foreach (var v in Data) if (float.IsNaN(v)) n++;
        return n;
    }

    /// <summary>
    /// Replaces NaN cells with the value of the nearest valid cell (multi-source breadth-first
    /// flood fill). Returns the number of cells filled. Throws when there is no valid cell at all.
    /// </summary>
    public int FillNaN()
    {
        int total = Width * Height;
        var queue = new Queue<int>();
        var visited = new bool[total];
        for (int i = 0; i < total; i++)
        {
            if (!float.IsNaN(Data[i])) { visited[i] = true; queue.Enqueue(i); }
        }
        if (queue.Count == 0) throw new InvalidOperationException("The height grid contains no valid elevation values.");
        if (queue.Count == total) return 0;

        int filled = 0;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % Width, z = i / Width;
            float v = Data[i];
            Visit(x - 1, z); Visit(x + 1, z); Visit(x, z - 1); Visit(x, z + 1);

            void Visit(int nx, int nz)
            {
                if (nx < 0 || nz < 0 || nx >= Width || nz >= Height) return;
                int j = nz * Width + nx;
                if (visited[j]) return;
                visited[j] = true;
                Data[j] = v;
                filled++;
                queue.Enqueue(j);
            }
        }
        return filled;
    }

    /// <summary>Slope in degrees at a cell using central differences (edge cells use one-sided differences).</summary>
    public float SlopeDegrees(int x, int z)
    {
        int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, Width - 1);
        int zu = Math.Max(z - 1, 0), zd = Math.Min(z + 1, Height - 1);
        double dx = (Data[z * Width + xr] - Data[z * Width + xl]) / ((xr - xl) * CellSize);
        double dz = (Data[zd * Width + x] - Data[zu * Width + x]) / ((zd - zu) * CellSize);
        return (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180.0 / Math.PI);
    }

    /// <summary>Gradient (dH/dE, dH/dN) at a cell in metres per metre.</summary>
    public (double DE, double DN) Gradient(int x, int z)
    {
        int xl = Math.Max(x - 1, 0), xr = Math.Min(x + 1, Width - 1);
        int zu = Math.Max(z - 1, 0), zd = Math.Min(z + 1, Height - 1);
        double de = (Data[z * Width + xr] - Data[z * Width + xl]) / ((xr - xl) * CellSize);
        // rows increase southwards, so dH/dN is the negative of the row difference
        double dn = -(Data[zd * Width + x] - Data[zu * Width + x]) / ((zd - zu) * CellSize);
        return (de, dn);
    }
}
