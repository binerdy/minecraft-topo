using System.Globalization;
using System.IO.Compression;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;

namespace MinecraftTopo.Core.Geology;

/// <summary>One geological horizon (the top of a formation) sampled over the block grid.</summary>
public sealed record Horizon(string Name, byte LithologyClass, float[] Elevation);

/// <summary>
/// swissJURA3D (©swisstopo): the 3D geological model of the Jura as GOCAD triangulated surfaces,
/// one per formation top. The surfaces under the selected area are rasterised into per-cell
/// elevations, which the chunk writer stacks into real underground layers. One-time download of
/// about 108 MB (224 MB extracted).
/// </summary>
public sealed class SwissJura3dSource
{
    public const string ArchiveUrl = "https://cms.geo.admin.ch/ogd/geology/jura3d/swissJURA3D_MA1_Horizon_and_Faults_GOCAD_20260318.zip";
    public const long ApproxBytes = 108_000_000;
    /// <summary>Extent of the published model part MA1 (Vaud Jura between Geneva and the Vallée de Joux).</summary>
    public static readonly Lv95Rect Extent = new(2_492_000, 1_131_000, 2_525_000, 1_170_000);

    private readonly Downloader _downloader;
    private readonly string _dir;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SwissJura3dSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _dir = paths.Jura3dCacheDir;
    }

    private string? HorizonFile => Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*Horizon*.ts").FirstOrDefault() : null;
    public bool IsReady => HorizonFile is not null;

    public async Task EnsureAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (IsReady) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (IsReady) return;
            string zipPath = Path.Combine(_dir, "swissJURA3D_horizons.zip");
            long shown = -1;
            var bytes = new Progress<(long Received, long? Total)>(p =>
            {
                long step = p.Received / 5_000_000;
                if (step == shown) return;
                shown = step;
                progress?.Report(new ProgressInfo("jura3d", p.Total is { } t && t > 0 ? 80.0 * p.Received / t : 0, $"Downloading swissJURA3D: {p.Received / 1_048_576} of {(p.Total ?? ApproxBytes) / 1_048_576} MB (one-time)"));
            });
            await _downloader.GetFileAsync(ArchiveUrl, zipPath, bytes, ct);
            progress?.Report(new ProgressInfo("jura3d", 85, "Extracting the swissJURA3D horizons"));
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Contains("Horizon", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException("The swissJURA3D archive has no horizon surfaces.");
                string target = Path.Combine(_dir, entry.Name);
                entry.ExtractToFile(target + ".part", overwrite: true);
                File.Move(target + ".part", target, overwrite: true);
            }, ct);
            try { File.Delete(zipPath); } catch (IOException) { }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Horizons that reach under the grid, top-down; null when the area is outside the model.</summary>
    public async Task<List<Horizon>?> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        if (!rect.Intersects(Extent))
        {
            progress?.Report(new ProgressInfo("jura3d", 100, "Outside the swissJURA3D model"));
            return null;
        }
        await EnsureAsync(progress, ct);
        progress?.Report(new ProgressInfo("jura3d", 90, "Reading swissJURA3D surfaces under the area"));
        var horizons = await Task.Run(() => Parse(HorizonFile!, grid, ct), ct);
        if (horizons.Count == 0)
        {
            progress?.Report(new ProgressInfo("jura3d", 100, "No swissJURA3D surface under this area"));
            return null;
        }
        progress?.Report(new ProgressInfo("jura3d", 100, $"swissJURA3D: {horizons.Count} formation tops under the area ({string.Join(", ", horizons.Take(6).Select(h => h.Name))}{(horizons.Count > 6 ? ", …" : "")})"));
        return horizons;
    }

    /// <summary>Streams the GOCAD file: vertices per surface, triangles rasterised where they touch the grid.</summary>
    private static List<Horizon> Parse(string path, HeightGrid grid, CancellationToken ct)
    {
        var result = new List<Horizon>();
        int w = grid.Width, h = grid.Height;
        double ox = grid.OriginE, oz = grid.OriginN, cell = grid.CellSize;
        double minE = grid.MinE - 50, maxE = grid.MaxE + 50, minN = grid.MinN - 50, maxN = grid.MaxN + 50;

        string name = "";
        var verts = new List<(float X, float Y, float Z)> { default }; // 1-based ids
        float[]? elev = null;
        int painted = 0;

        void Finish()
        {
            if (elev is not null && painted > 0)
            {
                result.Add(new Horizon(name, ClassFor(name), elev));
            }
            name = "";
            verts.Clear();
            verts.Add(default);
            elev = null;
            painted = 0;
        }

        using var reader = new StreamReader(path, System.Text.Encoding.UTF8, true, 1 << 20);
        string? line;
        long lineNo = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            if ((++lineNo & 0x3FFF) == 0) ct.ThrowIfCancellationRequested();
            if (line.Length < 4) continue;
            char c0 = line[0];
            if (c0 == 'P' || c0 == 'V')
            {
                // PVRTX id x y z ... | VRTX id x y z
                if (!(line.StartsWith("PVRTX ", StringComparison.Ordinal) || line.StartsWith("VRTX ", StringComparison.Ordinal))) continue;
                var s = line.AsSpan();
                int i = line.IndexOf(' ') + 1;
                if (!NextInt(s, ref i, out int id) || !Next(s, ref i, out double x) || !Next(s, ref i, out double y) || !Next(s, ref i, out double z)) continue;
                while (verts.Count <= id) verts.Add(default);
                verts[id] = ((float)(x - 2_000_000), (float)(y - 1_000_000), (float)z);
            }
            else if (c0 == 'T' && line.StartsWith("TRGL ", StringComparison.Ordinal))
            {
                var s = line.AsSpan();
                int i = 5;
                if (!NextInt(s, ref i, out int a) || !NextInt(s, ref i, out int b) || !NextInt(s, ref i, out int c)) continue;
                if (a >= verts.Count || b >= verts.Count || c >= verts.Count) continue;
                var va = verts[a]; var vb = verts[b]; var vc = verts[c];
                double ax = va.X + 2_000_000, ay = va.Y + 1_000_000, bx = vb.X + 2_000_000, by = vb.Y + 1_000_000, cx = vc.X + 2_000_000, cy = vc.Y + 1_000_000;
                if (Math.Max(ax, Math.Max(bx, cx)) < minE || Math.Min(ax, Math.Min(bx, cx)) > maxE || Math.Max(ay, Math.Max(by, cy)) < minN || Math.Min(ay, Math.Min(by, cy)) > maxN) continue;
                elev ??= Filled(w * h);
                painted += Rasterize(elev, w, h, (ax - ox) / cell, (oz - ay) / cell, va.Z, (bx - ox) / cell, (oz - by) / cell, vb.Z, (cx - ox) / cell, (oz - cy) / cell, vc.Z);
            }
            else if (c0 == 'A' && line.StartsWith("ATOM ", StringComparison.Ordinal))
            {
                var s = line.AsSpan();
                int i = 5;
                if (!NextInt(s, ref i, out int id) || !NextInt(s, ref i, out int refId)) continue;
                while (verts.Count <= id) verts.Add(default);
                if (refId < verts.Count) verts[id] = verts[refId];
            }
            else if (c0 == 'G' && line.StartsWith("GOCAD ", StringComparison.Ordinal))
            {
                Finish();
            }
            else if (c0 == 'n' && line.StartsWith("name:", StringComparison.Ordinal))
            {
                if (name.Length == 0) name = line[5..].Trim();
            }
            else if (c0 == 'E' && line.StartsWith("END", StringComparison.Ordinal) && line.Length <= 4)
            {
                Finish();
            }
        }
        Finish();
        // top-down by mean elevation
        return result.OrderByDescending(hz => hz.Elevation.Where(v => !float.IsNaN(v)).DefaultIfEmpty(float.NegativeInfinity).Average()).ToList();
    }

    private static float[] Filled(int n)
    {
        var a = new float[n];
        Array.Fill(a, float.NaN);
        return a;
    }

    /// <summary>Barycentric fill of a triangle given in cell coordinates; returns the cells painted.</summary>
    private static int Rasterize(float[] target, int w, int h, double x0, double z0, float e0, double x1, double z1, float e1, double x2, double z2, float e2)
    {
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), maxX = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minZ = Math.Max(0, (int)Math.Floor(Math.Min(z0, Math.Min(z1, z2)))), maxZ = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(z0, Math.Max(z1, z2))));
        if (minX > maxX || minZ > maxZ) return 0;
        double det = (x1 - x0) * (z2 - z0) - (x2 - x0) * (z1 - z0);
        if (Math.Abs(det) < 1e-12) return 0;
        int n = 0;
        for (int z = minZ; z <= maxZ; z++)
        {
            double pz = z + 0.5;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double l1 = ((x1 - px) * (z2 - pz) - (x2 - px) * (z1 - pz)) / det;
                double l2 = ((x2 - px) * (z0 - pz) - (x0 - px) * (z2 - pz)) / det;
                double l3 = 1 - l1 - l2;
                if (l1 < -1e-9 || l2 < -1e-9 || l3 < -1e-9) continue;
                target[z * w + x] = (float)(l1 * e0 + l2 * e1 + l3 * e2);
                n++;
            }
        }
        return n;
    }

    /// <summary>GK500 group of the formation below a "Top …" horizon, from its (French/German) name.</summary>
    public static byte ClassFor(string horizonName)
    {
        string s = horizonName.ToLowerInvariant();
        if (s.Contains("socle") || s.Contains("sockel") || s.Contains("basement") || s.Contains("cristallin") || s.Contains("kristallin")) return 12;
        if (s.Contains("perm") || s.Contains("carbon") || s.Contains("karbon") || s.Contains("rotliegend")) return 6;
        if (s.Contains("buntsandstein") || s.Contains("grès bigarré")) return 5;
        if (s.Contains("gips") || s.Contains("gypse") || s.Contains("anhydrit") || s.Contains("keuper") || s.Contains("evaporit") || s.Contains("sel") && s.Contains("zone")) return 11;
        if (s.Contains("muschelkalk") || s.Contains("dolomi") || s.Contains("trigonodus") || s.Contains("calcaire coquillier")) return 9;
        if (s.Contains("opalinus") || s.Contains("argile") || s.Contains("ton") && !s.Contains("stone") || s.Contains("marnes") && !s.Contains("calc")) return 7;
        if (s.Contains("marne") || s.Contains("mergel") || s.Contains("effingen") || s.Contains("wildegg") || s.Contains("bärschwil") || s.Contains("barschwil") || s.Contains("ifenthal") || s.Contains("staffelegg") || s.Contains("klettgau")) return 4;
        if (s.Contains("molasse") || s.Contains("usm") || s.Contains("omm") || s.Contains("osm") || s.Contains("sidérolithique") || s.Contains("siderolith") || s.Contains("sandstein") || s.Contains("grès")) return 5;
        if (s.Contains("hauptrogenstein") || s.Contains("calcaire") || s.Contains("kalk") || s.Contains("malm") || s.Contains("reuchenette") || s.Contains("villigen")
            || s.Contains("twannbach") || s.Contains("kimmeridg") || s.Contains("oolith") || s.Contains("dogger") || s.Contains("jura") || s.Contains("crétacé") || s.Contains("kreide") || s.Contains("urgonien") || s.Contains("hauterivien") || s.Contains("valanginien")) return 8;
        return 8;
    }

    private static bool Next(ReadOnlySpan<char> s, ref int i, out double v)
    {
        while (i < s.Length && s[i] == ' ') i++;
        int start = i;
        while (i < s.Length && s[i] != ' ') i++;
        return double.TryParse(s[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    private static bool NextInt(ReadOnlySpan<char> s, ref int i, out int v)
    {
        while (i < s.Length && s[i] == ' ') i++;
        int start = i;
        while (i < s.Length && s[i] != ' ') i++;
        return int.TryParse(s[start..i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    }
}
