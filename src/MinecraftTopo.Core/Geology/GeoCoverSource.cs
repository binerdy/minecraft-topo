using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Gpkg;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Geology;

/// <summary>
/// GeoCover (©swisstopo): the 1:25 000 geological vector map, one GeoPackage per map sheet
/// (about 9 MB each, downloaded when needed). Bedrock and unconsolidated deposits are mapped
/// onto the 23 GK500 lithology groups so the same block roles apply.
/// </summary>
public sealed class GeoCoverSource
{
    public const string StacItemsUrl = "https://data.geo.admin.ch/api/stac/v0.9/collections/ch.swisstopo.geologie-geocover/items";
    public const long ApproxSheetBytes = 9_000_000;

    private readonly Downloader _downloader;
    private readonly string _dir;

    public GeoCoverSource(Downloader downloader, AppPaths paths)
    {
        _downloader = downloader;
        _dir = paths.GeoCoverCacheDir;
    }

    public sealed record SheetInfo(string Id, string Url, Lv95Rect Bounds);

    /// <summary>Published sheets intersecting the rectangle (the full-coverage item is skipped).</summary>
    public async Task<IReadOnlyList<SheetInfo>> FindSheetsAsync(Lv95Rect rect, CancellationToken ct)
    {
        var (west, south, east, north) = rect.ToWgs84Bbox();
        string bbox = string.Create(CultureInfo.InvariantCulture, $"{west:0.######},{south:0.######},{east:0.######},{north:0.######}");
        string json = await _downloader.GetStringAsync($"{StacItemsUrl}?bbox={bbox}&limit=100", ct);
        using var doc = JsonDocument.Parse(json);
        var sheets = new List<SheetInfo>();
        if (!doc.RootElement.TryGetProperty("features", out var features)) return sheets;
        foreach (var f in features.EnumerateArray())
        {
            string id = f.GetProperty("id").GetString() ?? "";
            if (!id.Contains('_')) continue; // "geologie-geocover" = whole country (1.7 GB)
            if (!f.TryGetProperty("assets", out var assets)) continue;
            string? href = null;
            foreach (var a in assets.EnumerateObject())
            {
                if (a.Name.EndsWith(".gpkg.zip", StringComparison.OrdinalIgnoreCase)) { href = a.Value.GetProperty("href").GetString(); break; }
            }
            if (href is null) continue;
            var bounds = rect;
            if (f.TryGetProperty("bbox", out var bb) && bb.GetArrayLength() == 4)
            {
                var sw = Lv95.FromWgs84(bb[1].GetDouble(), bb[0].GetDouble());
                var ne = Lv95.FromWgs84(bb[3].GetDouble(), bb[2].GetDouble());
                bounds = new Lv95Rect(sw.E, sw.N, ne.E, ne.N);
            }
            sheets.Add(new SheetInfo(id, href, bounds));
        }
        return sheets;
    }

    /// <summary>Bedrock class and deposit class per cell (0 where the sheet has nothing), or null without sheets.</summary>
    public async Task<(byte[] Bedrock, byte[] Deposit, int Sheets)?> GetAsync(HeightGrid grid, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        var rect = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        progress?.Report(new ProgressInfo("geology", 0, "Looking up GeoCover sheets"));
        var sheets = await FindSheetsAsync(rect, ct);
        if (sheets.Count == 0)
        {
            progress?.Report(new ProgressInfo("geology", 50, "No GeoCover sheet published for this area; using GK500"));
            return null;
        }
        int n = grid.Width * grid.Height;
        var bedrock = new byte[n];
        var deposit = new byte[n];
        var raster = new GridRasterizer(grid);
        int done = 0;
        foreach (var sheet in sheets)
        {
            ct.ThrowIfCancellationRequested();
            string sheetDir = Path.Combine(_dir, sheet.Id);
            string? gpkg = Directory.Exists(sheetDir) ? Directory.GetFiles(sheetDir, "*.gpkg", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (gpkg is null)
            {
                string zipPath = Path.Combine(_dir, sheet.Id + ".gpkg.zip");
                progress?.Report(new ProgressInfo("geology", 100.0 * done / sheets.Count, $"Downloading GeoCover sheet {sheet.Id.Replace("geologie-geocover_", "")} (about 9 MB, one-time)"));
                await _downloader.GetFileAsync(sheet.Url, zipPath, null, ct);
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(sheetDir);
                    using var zip = ZipFile.OpenRead(zipPath);
                    // GPKG/de/<sheet>.gpkg (the German edition; fr/it carry the same geometry)
                    var entry = zip.Entries.Where(e => e.Name.EndsWith(".gpkg", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.FullName.Contains("/de/") || e.FullName.Contains("\\de\\") ? 0 : 1).First();
                    string target = Path.Combine(sheetDir, entry.Name);
                    entry.ExtractToFile(target + ".part", overwrite: true);
                    File.Move(target + ".part", target, overwrite: true);
                }, ct);
                try { File.Delete(zipPath); } catch (IOException) { }
                gpkg = Directory.GetFiles(sheetDir, "*.gpkg", SearchOption.AllDirectories).First();
            }
            progress?.Report(new ProgressInfo("geology", 100.0 * done / sheets.Count, $"Reading GeoCover sheet {sheet.Id.Replace("geologie-geocover_", "")}"));
            await Task.Run(() => ReadSheet(gpkg, grid, raster, bedrock, deposit, ct), ct);
            done++;
        }
        var counts = new int[Anvil.Blocks.GeologyClasses + 1];
        foreach (var c in bedrock) counts[c]++;
        progress?.Report(new ProgressInfo("geology", 100, Gk500Source.Summary(counts, n, $"GeoCover, {sheets.Count} sheet{(sheets.Count == 1 ? "" : "s")}")));
        return (bedrock, deposit, sheets.Count);
    }

    private static void ReadSheet(string gpkgPath, HeightGrid grid, GridRasterizer raster, byte[] bedrock, byte[] deposit, CancellationToken ct)
    {
        using var gpkg = new GeoPackage(gpkgPath);
        var bbox = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN);
        List<(double X, double Z)> Pts(IReadOnlyList<(double X, double Y, double Z)> pts) =>
            pts.Select(p => ((p.X - grid.OriginE) / grid.CellSize, (grid.OriginN - p.Y) / grid.CellSize)).ToList();
        IEnumerable<PolygonGeometry> Polygons(Geometry g) => g switch
        {
            PolygonGeometry p => [p],
            MultiGeometry m => m.Parts.SelectMany(Polygons),
            _ => [],
        };

        if (gpkg.FindLayer("Bedrock_PLG") is { } bedLayer)
        {
            foreach (var f in gpkg.Query(bedLayer, bbox, "LITHO_MAIN", "LITHO_SEC", "LITSTRAT"))
            {
                ct.ThrowIfCancellationRequested();
                byte cls = BedrockClass(f.Text("LITHO_MAIN"), f.Text("LITHO_SEC"), f.Text("LITSTRAT"));
                if (cls == 0) continue;
                foreach (var poly in Polygons(f.Geometry)) raster.FillRingsWith(poly.Rings.Select(Pts).ToList(), cell => bedrock[cell] = cls);
            }
        }
        if (gpkg.FindLayer("Unconsolidated_Deposits_PLG") is { } depLayer)
        {
            foreach (var f in gpkg.Query(depLayer, bbox, "RUNC_LITHO", "RUNC_LITSTRAT", "RUNC_MAT_TYPE"))
            {
                ct.ThrowIfCancellationRequested();
                byte cls = DepositClass(f.Text("RUNC_LITHO"), f.Text("RUNC_LITSTRAT"));
                if (cls == 0) continue;
                foreach (var poly in Polygons(f.Geometry)) raster.FillRingsWith(poly.Rings.Select(Pts).ToList(), cell => deposit[cell] = cls);
            }
        }
    }

    /// <summary>GK500 group for a GeoCover bedrock lithology (German terms).</summary>
    public static byte BedrockClass(string main, string secondary, string litstrat)
    {
        string s = (main + " " + litstrat).ToLowerInvariant();
        if (s.Contains("ultramaf") || s.Contains("serpentin") || s.Contains("peridot")) return 23;
        if (s.Contains("basisch") || s.Contains("gabbro") || s.Contains("diabas") || s.Contains("ophiolith")) return 22;
        if (s.Contains("amphibolit") || s.Contains("eklogit")) return 21;
        if (s.Contains("marmor")) return 16;
        if (s.Contains("quarzit")) return 17;
        if (s.Contains("phyllit") && s.Contains("quarz")) return 18;
        if (s.Contains("glimmerschiefer") || s.Contains("granatschiefer") || s.Contains("migmatit") || s.Contains("schiefer: glimmer")) return 19;
        if (s.Contains("gneis")) return 20;
        if (s.Contains("porphyr")) return 13;
        if (s.Contains("vulkanit") || s.Contains("basalt") || s.Contains("andesit") || s.Contains("rhyolith") || s.Contains("tuff")) return 14;
        if (s.Contains("granit") || s.Contains("syenit") || s.Contains("granodiorit") || s.Contains("aplit") || s.Contains("diorit") || s.Contains("tonalit")) return 12;
        if (s.Contains("radiolarit") || s.Contains("hornstein") || s.Contains("kieselkalk")) return 10;
        if (s.Contains("gips") || s.Contains("anhydrit") || s.Contains("evaporit") || s.Contains("rauwacke") || s.Contains("rauhwacke") || s.Contains("salz")) return 11;
        if (s.Contains("dolomit")) return 9;
        if (s.Contains("kalkphyllit") || s.Contains("mergelschiefer") || s.Contains("bündnerschiefer") || s.Contains("buendnerschiefer") || s.Contains("kalkschiefer") || (s.Contains("schiefer") && s.Contains("kalk"))) return 15;
        if (s.Contains("konglomerat") || s.Contains("brekzie") || s.Contains("nagelfluh")) return 6;
        if (s.Contains("tonstein") || s.Contains("schiefer") || s.Contains("tonschiefer") || s.Contains("siltstein")) return 7;
        if (s.Contains("mergel") && s.Contains("sandstein")) return 4;
        if (s.Contains("sandstein")) return 5;
        if (s.Contains("mergel")) return 4;
        if (s.Contains("kalk")) return 8;
        if (s.Contains("phyllit")) return 18;
        return 0;
    }

    /// <summary>GK500 group for an unconsolidated deposit.</summary>
    public static byte DepositClass(string litho, string litstrat)
    {
        string s = (litho + " " + litstrat).ToLowerInvariant();
        if (s.Contains("sumpf") || s.Contains("torf") || s.Contains("seeboden") || s.Contains("löss") || s.Contains("loess") || s.Contains("ton") || s.Contains("silt")) return 1;
        if (s.Contains("moräne") || s.Contains("moraene") || s.Contains("till") || s.Contains("blockgletscher") || s.Contains("sturz") || s.Contains("bergsturz") || s.Contains("sackung") || s.Contains("hangschutt") || s.Contains("blockschutt")) return 3;
        if (s.Contains("bachschutt") || s.Contains("schutt") || s.Contains("alluvion") || s.Contains("fluviatil") || s.Contains("kies") || s.Contains("sand") || s.Contains("lockergestein") || s.Contains("schwemm")) return 2;
        return s.Length > 1 ? (byte)2 : (byte)0;
    }
}
