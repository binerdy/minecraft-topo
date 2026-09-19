using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Names;

/// <summary>
/// swissNAMES3D (©swisstopo): every official geographic name with a coordinate, as CSV. Peaks,
/// passes, huts, chapels, waterfalls, springs, fields, valleys, ridges, lakes, rivers, lifts and
/// stations become standing signs. One-time download of about 32 MB.
/// </summary>
public sealed class SwissNamesSource
{
    public const string StacItemsUrl = "https://data.geo.admin.ch/api/stac/v0.9/collections/ch.swisstopo.swissnames3d/items?limit=100";
    public const long ApproxBytes = 33_000_000;

    private readonly Downloader _downloader;
    private readonly string _dir;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SwissNamesSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _dir = paths.NamesCacheDir;
    }

    public bool IsReady => File.Exists(Path.Combine(_dir, "swissNAMES3D_PKT.csv"));

    /// <summary>A named feature to sign: grid cell, lines, and whether the sign should be moved onto land.</summary>
    public sealed record NamedPlace(int X, int Z, string Name, string Kind, double Elevation);

    /// <summary>Point-name object types that get a sign, with a short label shown under the name.</summary>
    private static readonly Dictionary<string, string> PointKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hauptgipfel"] = "peak", ["Gipfel"] = "peak", ["Alpiner Gipfel"] = "peak", ["Huegel"] = "hill", ["Haupthuegel"] = "hill", ["Felskopf"] = "rock",
        ["Pass"] = "pass", ["Strassenpass"] = "pass",
        ["Kapelle"] = "chapel", ["Sakrales Gebaeude"] = "church", ["Turm"] = "tower", ["Gebaeude"] = "", ["Offenes Gebaeude"] = "",
        ["Wasserfall"] = "waterfall", ["Quelle"] = "spring", ["Grotte, Hoehle"] = "cave", ["Aussichtspunkt"] = "viewpoint",
        ["Denkmal"] = "monument", ["Brunnen"] = "fountain", ["Bildstock"] = "shrine", ["Erratischer Block"] = "boulder", ["Felsblock"] = "boulder",
        ["Haltestelle Bahn"] = "station", ["Haltestelle Schiff"] = "pier", ["Uebrige Bahnen"] = "lift station",
        ["Flurname swisstopo"] = "", ["Lokalname swisstopo"] = "", ["Ortsteil"] = "", ["Ort"] = "", ["Quartier"] = "", ["Quartierteil"] = "",
    };

    private static readonly Dictionary<string, string> LineKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Fliessgewaesser"] = "river", ["Skilift"] = "ski lift", ["Sesselbahn"] = "chair lift", ["Luftseilbahn"] = "cable car", ["Gondelbahn"] = "gondola",
        ["Rodelbahn"] = "toboggan run", ["Skisprungschanze"] = "ski jump", ["Bobbahn"] = "bob run",
    };

    private static readonly Dictionary<string, string> AreaKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["See"] = "lake", ["Tal"] = "valley", ["Grat"] = "ridge", ["Graben"] = "gorge", ["Gebiet"] = "region", ["Landschaftsname"] = "region",
        ["Gletscher"] = "glacier", ["Schwimmbadareal"] = "pool", ["Campingplatzareal"] = "camping", ["Golfplatzareal"] = "golf course",
        ["Zooareal"] = "zoo", ["Historisches Areal"] = "historic site", ["Spitalareal"] = "hospital", ["Schul- und Hochschulareal"] = "school",
        ["Staumauer"] = "dam", ["Wehr"] = "weir", ["Kraftwerkareal"] = "power plant", ["Klosterareal"] = "monastery",
    };

    /// <summary>Downloads and extracts the newest CSV release when missing.</summary>
    public async Task EnsureAsync(IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (IsReady) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (IsReady) return;
            progress?.Report(new ProgressInfo("names", 0, "Finding the newest swissNAMES3D release"));
            string json = await _downloader.GetStringAsync(StacItemsUrl, ct);
            using var doc = JsonDocument.Parse(json);
            string? bestId = null, bestUrl = null;
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                string id = f.GetProperty("id").GetString() ?? "";
                if (!f.TryGetProperty("assets", out var assets)) continue;
                foreach (var a in assets.EnumerateObject())
                {
                    if (!a.Name.EndsWith(".csv.zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (bestId is null || string.CompareOrdinal(id, bestId) > 0) { bestId = id; bestUrl = a.Value.GetProperty("href").GetString(); }
                }
            }
            if (bestUrl is null) throw new InvalidOperationException("No swissNAMES3D CSV release found.");
            string zipPath = Path.Combine(_dir, bestId + ".csv.zip");
            long shown = -1;
            var bytes = new Progress<(long Received, long? Total)>(p =>
            {
                long step = p.Received / 2_000_000;
                if (step == shown) return;
                shown = step;
                progress?.Report(new ProgressInfo("names", p.Total is { } t && t > 0 ? 90.0 * p.Received / t : 0, $"Downloading swissNAMES3D {bestId}: {p.Received / 1_048_576} MB (one-time)"));
            });
            await _downloader.GetFileAsync(bestUrl, zipPath, bytes, ct);
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var e in zip.Entries)
                {
                    if (!e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = Path.Combine(_dir, e.Name);
                    e.ExtractToFile(target + ".part", overwrite: true);
                    File.Move(target + ".part", target, overwrite: true);
                }
            }, ct);
            try { File.Delete(zipPath); } catch (IOException) { }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Signs for every named feature inside the grid, placed on free land cells.</summary>
    public async Task<List<SignSpec>> GetSignsAsync(HeightGrid grid, ClassifiedTerrain terrain, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        await EnsureAsync(progress, ct);
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        var places = await Task.Run(() =>
        {
            var list = new List<NamedPlace>();
            Read(Path.Combine(_dir, "swissNAMES3D_PKT.csv"), rect, grid, PointKinds, list, ct);
            Read(Path.Combine(_dir, "swissNAMES3D_LIN.csv"), rect, grid, LineKinds, list, ct);
            Read(Path.Combine(_dir, "swissNAMES3D_PLY.csv"), rect, grid, AreaKinds, list, ct);
            return list;
        }, ct);

        var signs = new List<SignSpec>();
        var used = new HashSet<(int, int)>();
        int w = terrain.Width, h = terrain.Height;
        int searchRadius = Math.Max(3, (int)Math.Round(300 / grid.CellSize));
        foreach (var p in places)
        {
            ct.ThrowIfCancellationRequested();
            var cell = FindLand(terrain, p.X, p.Z, searchRadius, used);
            if (cell is null) continue;
            used.Add(cell.Value);
            signs.Add(new SignSpec(cell.Value.Item1, cell.Value.Item2, 0, Lines(p)));
        }
        progress?.Report(new ProgressInfo("names", 100, $"{signs.Count:N0} name signs from swissNAMES3D ({places.Count(p => p.Kind == "peak")} peaks, {places.Count(p => p.Kind == "pass")} passes, {places.Count(p => p.Kind == "river")} rivers)"));
        return signs;
    }

    private static string[] Lines(NamedPlace p)
    {
        var lines = Tlm.SignPlanner.SplitSignText(p.Name).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        string extra = p.Kind is "peak" or "hill" or "pass" or "rock" && p.Elevation > 0
            ? $"{p.Kind} {p.Elevation:0} m"
            : p.Kind;
        if (extra.Length > 0 && lines.Count < 4) lines.Add(extra.Length > 15 ? extra[..15] : extra);
        var result = new string[4];
        for (int i = 0; i < 4; i++) result[i] = i < lines.Count ? lines[i] : "";
        return result;
    }

    /// <summary>The cell itself when it is free land, else the nearest free land cell within the radius.</summary>
    private static (int, int)? FindLand(ClassifiedTerrain t, int x, int z, int radius, HashSet<(int, int)> used)
    {
        for (int r = 0; r <= radius; r++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                int stepX = Math.Abs(dz) == r ? 1 : 2 * r;
                for (int dx = -r; dx <= r; dx += Math.Max(1, stepX))
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= t.Width || cz >= t.Height) continue;
                    int i = cz * t.Width + cx;
                    if (t.Kind[i] == Surface.Water || t.WaterY[i] != ClassifiedTerrain.NoWater || t.IsOccupied(i) || used.Contains((cx, cz))) continue;
                    return (cx, cz);
                }
            }
        }
        return null;
    }

    private static void Read(string path, Lv95Rect rect, HeightGrid grid, Dictionary<string, string> kinds, List<NamedPlace> into, CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        using var reader = new StreamReader(path, Encoding.UTF8, true, 1 << 16);
        string? header = reader.ReadLine();
        if (header is null) return;
        var cols = header.TrimStart('﻿').Split(';');
        int iName = Array.IndexOf(cols, "NAME"), iKind = Array.IndexOf(cols, "OBJEKTART"), iE = Array.IndexOf(cols, "E"), iN = Array.IndexOf(cols, "N"), iZ = Array.IndexOf(cols, "Z");
        int iStatus = Array.IndexOf(cols, "STATUS"), iType = Array.IndexOf(cols, "NAMEN_TYP");
        if (iName < 0 || iKind < 0 || iE < 0 || iN < 0) return;
        string? line;
        int lineNo = 0;
        var seen = new HashSet<string>();
        while ((line = reader.ReadLine()) is not null)
        {
            if ((++lineNo & 0xFFF) == 0) ct.ThrowIfCancellationRequested();
            var f = line.Split(';');
            if (f.Length <= Math.Max(iE, iN)) continue;
            if (!double.TryParse(f[iE], NumberStyles.Float, CultureInfo.InvariantCulture, out double e) ||
                !double.TryParse(f[iN], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) continue;
            if (!rect.Contains(e, n)) continue;
            if (!kinds.TryGetValue(f[iKind], out var kind)) continue;
            // one sign per name in multilingual areas: skip secondary language rows
            if (iType >= 0 && f[iType].Contains("Endonym", StringComparison.OrdinalIgnoreCase) && !f[iType].Contains("erst", StringComparison.OrdinalIgnoreCase)) continue;
            string name = f[iName].Trim();
            if (name.Length == 0 || !seen.Add(name + "|" + f[iKind])) continue;
            double z = iZ >= 0 && double.TryParse(f[iZ], NumberStyles.Float, CultureInfo.InvariantCulture, out double zz) ? zz : 0;
            int x = (int)Math.Floor((e - grid.OriginE) / grid.CellSize), gz = (int)Math.Floor((grid.OriginN - n) / grid.CellSize);
            if (x < 0 || gz < 0 || x >= grid.Width || gz >= grid.Height) continue;
            into.Add(new NamedPlace(x, gz, name, kind, z));
        }
    }
}
