using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using MinecraftTopo.Core.Geo;

namespace MinecraftTopo.Core.Gpkg;

/// <summary>A parsed geometry in LV95 metres (Z = height where present).</summary>
public abstract record Geometry;
public sealed record PointGeometry(double X, double Y, double Z) : Geometry;
public sealed record LineGeometry(IReadOnlyList<(double X, double Y, double Z)> Points) : Geometry;
public sealed record PolygonGeometry(IReadOnlyList<IReadOnlyList<(double X, double Y, double Z)>> Rings) : Geometry;
public sealed record MultiGeometry(IReadOnlyList<Geometry> Parts) : Geometry;

/// <summary>One feature: its geometry plus the requested attribute values (by column name, lower case).</summary>
public sealed record Feature(long Id, Geometry Geometry, IReadOnlyDictionary<string, object?> Attributes)
{
    public string Text(string column) => Attributes.TryGetValue(column.ToLowerInvariant(), out var v) && v is not null && v is not DBNull ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
    public double Number(string column, double fallback = 0) =>
        Attributes.TryGetValue(column.ToLowerInvariant(), out var v) && v is not null && v is not DBNull ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : fallback;
}

/// <summary>
/// Minimal OGC GeoPackage reader: lists layers, queries features by bounding box through the
/// R-tree index, and decodes the standard GeoPackage binary geometry (header + ISO/EWKB WKB).
/// </summary>
public sealed class GeoPackage : IDisposable
{
    private readonly SqliteConnection _con;
    private readonly Dictionary<string, (string GeomColumn, string PkColumn, string[] Columns, bool HasRtree)> _layers = new(StringComparer.OrdinalIgnoreCase);

    public GeoPackage(string path)
    {
        _con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        _con.Open();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT c.table_name, g.column_name FROM gpkg_contents c JOIN gpkg_geometry_columns g ON g.table_name = c.table_name WHERE c.data_type = 'features'";
        using var r = cmd.ExecuteReader();
        var tables = new List<(string, string)>();
        while (r.Read()) tables.Add((r.GetString(0), r.GetString(1)));
        r.Close();
        foreach (var (table, geomCol) in tables)
        {
            var (pk, cols) = Describe(table);
            _layers[table] = (geomCol, pk, cols, TableExists($"rtree_{table}_{geomCol}"));
        }
    }

    public IReadOnlyCollection<string> Layers => _layers.Keys;

    public bool HasLayer(string table) => _layers.ContainsKey(table);

    public string[] Columns(string table) => _layers.TryGetValue(table, out var l) ? l.Columns : [];

    /// <summary>Finds the first existing layer among candidates (schemas differ slightly between releases).</summary>
    public string? FindLayer(params string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (_layers.ContainsKey(c)) return _layers.Keys.First(k => k.Equals(c, StringComparison.OrdinalIgnoreCase));
            var match = _layers.Keys.FirstOrDefault(k => k.Contains(c, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }

    private (string Pk, string[] Columns) Describe(string table)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var r = cmd.ExecuteReader();
        string pk = "fid";
        var cols = new List<string>();
        while (r.Read())
        {
            string name = r.GetString(1);
            cols.Add(name);
            if (r.GetInt32(5) == 1) pk = name;
        }
        return (pk, cols.ToArray());
    }

    private bool TableExists(string name)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table','view') AND name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Features of a layer whose bounding box intersects <paramref name="bbox"/>, with the given attribute columns.</summary>
    public IEnumerable<Feature> Query(string table, Lv95Rect bbox, params string[] attributes)
    {
        if (!_layers.TryGetValue(table, out var layer)) yield break;
        var wanted = attributes.Where(a => layer.Columns.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray();
        string attrSql = wanted.Length == 0 ? "" : ", " + string.Join(", ", wanted.Select(a => $"t.\"{a}\""));
        using var cmd = _con.CreateCommand();
        if (layer.HasRtree)
        {
            cmd.CommandText = $"SELECT t.\"{layer.PkColumn}\", t.\"{layer.GeomColumn}\"{attrSql} FROM \"{table}\" t JOIN \"rtree_{table}_{layer.GeomColumn}\" r ON r.id = t.\"{layer.PkColumn}\" " +
                              "WHERE r.minx <= $maxx AND r.maxx >= $minx AND r.miny <= $maxy AND r.maxy >= $miny";
        }
        else
        {
            cmd.CommandText = $"SELECT t.\"{layer.PkColumn}\", t.\"{layer.GeomColumn}\"{attrSql} FROM \"{table}\" t";
        }
        cmd.Parameters.AddWithValue("$minx", bbox.MinE);
        cmd.Parameters.AddWithValue("$maxx", bbox.MaxE);
        cmd.Parameters.AddWithValue("$miny", bbox.MinN);
        cmd.Parameters.AddWithValue("$maxy", bbox.MaxN);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (r.IsDBNull(1)) continue;
            var blob = (byte[])r.GetValue(1);
            Geometry? geom;
            try { geom = GpkgGeometry.Parse(blob, out var env); if (env is { } e && !e.Intersects(bbox)) continue; }
            catch (InvalidDataException) { continue; }
            if (geom is null) continue;
            var attrs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < wanted.Length; i++) attrs[wanted[i].ToLowerInvariant()] = r.IsDBNull(2 + i) ? null : r.GetValue(2 + i);
            yield return new Feature(r.GetInt64(0), geom, attrs);
        }
    }

    public void Dispose() => _con.Dispose();
}

/// <summary>Decoder for the GeoPackage binary geometry format.</summary>
public static class GpkgGeometry
{
    public static Geometry? Parse(ReadOnlySpan<byte> blob, out Lv95Rect? envelope)
    {
        envelope = null;
        if (blob.Length < 8 || blob[0] != (byte)'G' || blob[1] != (byte)'P') throw new InvalidDataException("Not a GeoPackage geometry.");
        byte flags = blob[3];
        bool little = (flags & 1) != 0;
        int envType = (flags >> 1) & 7;
        bool empty = (flags & 0x10) != 0;
        int envLen = envType switch { 0 => 0, 1 => 32, 2 => 48, 3 => 48, 4 => 64, _ => throw new InvalidDataException("Bad envelope indicator.") };
        int pos = 8;
        if (envType >= 1)
        {
            double minx = ReadDouble(blob, pos, little), maxx = ReadDouble(blob, pos + 8, little), miny = ReadDouble(blob, pos + 16, little), maxy = ReadDouble(blob, pos + 24, little);
            envelope = new Lv95Rect(minx, miny, maxx, maxy);
        }
        pos += envLen;
        if (empty || pos >= blob.Length) return null;
        return ReadWkb(blob, ref pos);
    }

    private static Geometry? ReadWkb(ReadOnlySpan<byte> b, ref int pos)
    {
        bool little = b[pos] == 1;
        pos++;
        uint type = ReadUInt32(b, pos, little);
        pos += 4;
        bool hasZ = false, hasM = false;
        if ((type & 0x80000000) != 0) { hasZ = true; type &= ~0x80000000u; }
        if ((type & 0x40000000) != 0) { hasM = true; type &= ~0x40000000u; }
        if ((type & 0x20000000) != 0) { type &= ~0x20000000u; pos += 4; } // EWKB SRID
        if (type >= 3000) { hasZ = true; hasM = true; type -= 3000; }
        else if (type >= 2000) { hasM = true; type -= 2000; }
        else if (type >= 1000) { hasZ = true; type -= 1000; }
        int dims = 2 + (hasZ ? 1 : 0) + (hasM ? 1 : 0);

        switch (type)
        {
            case 1:
            {
                var p = ReadPoint(b, ref pos, little, dims, hasZ);
                return new PointGeometry(p.X, p.Y, p.Z);
            }
            case 2:
                return new LineGeometry(ReadRing(b, ref pos, little, dims, hasZ));
            case 3:
            {
                uint rings = ReadUInt32(b, pos, little); pos += 4;
                var list = new List<IReadOnlyList<(double, double, double)>>((int)rings);
                for (uint i = 0; i < rings; i++) list.Add(ReadRing(b, ref pos, little, dims, hasZ));
                return new PolygonGeometry(list);
            }
            case 4: case 5: case 6: case 7:
            {
                uint count = ReadUInt32(b, pos, little); pos += 4;
                var parts = new List<Geometry>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var g = ReadWkb(b, ref pos);
                    if (g is not null) parts.Add(g);
                }
                return new MultiGeometry(parts);
            }
            default:
                throw new InvalidDataException($"Unsupported WKB geometry type {type}.");
        }
    }

    private static List<(double X, double Y, double Z)> ReadRing(ReadOnlySpan<byte> b, ref int pos, bool little, int dims, bool hasZ)
    {
        uint n = ReadUInt32(b, pos, little); pos += 4;
        var pts = new List<(double, double, double)>((int)n);
        for (uint i = 0; i < n; i++) pts.Add(ReadPoint(b, ref pos, little, dims, hasZ));
        return pts;
    }

    private static (double X, double Y, double Z) ReadPoint(ReadOnlySpan<byte> b, ref int pos, bool little, int dims, bool hasZ)
    {
        double x = ReadDouble(b, pos, little), y = ReadDouble(b, pos + 8, little);
        double z = hasZ ? ReadDouble(b, pos + 16, little) : double.NaN;
        pos += dims * 8;
        return (x, y, z);
    }

    private static double ReadDouble(ReadOnlySpan<byte> b, int pos, bool little) =>
        little ? BinaryPrimitives.ReadDoubleLittleEndian(b.Slice(pos, 8)) : BinaryPrimitives.ReadDoubleBigEndian(b.Slice(pos, 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> b, int pos, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(pos, 4)) : BinaryPrimitives.ReadUInt32BigEndian(b.Slice(pos, 4));
}
