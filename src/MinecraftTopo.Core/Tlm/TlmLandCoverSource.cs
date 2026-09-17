using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Gpkg;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Core.Water;

namespace MinecraftTopo.Core.Tlm;

/// <summary>
/// Land cover and infrastructure from swisstopo's landscape models (©swisstopo): swissTLMRegio
/// (1:200 000) or swissTLM3D (detailed). Everything is read from the GeoPackage through its
/// spatial index, so only the selected area is touched.
/// </summary>
public sealed class TlmLandCoverSource : ILandCoverSource
{
    private readonly TlmDatasets _datasets;
    private readonly TlmKind _kind;

    public TlmLandCoverSource(TlmDatasets datasets, TlmKind kind)
    {
        _datasets = datasets;
        _kind = kind;
    }

    public string Name => TlmDatasets.DisplayName(_kind);
    public bool SupportsInfrastructure => true;

    private readonly record struct RoadClass(int Rank, double WidthM, RoadMaterial Material, bool CentreLine, bool EdgeLines, bool SolidCentre);

    public async Task<LandCover> GetAsync(HeightGrid grid, LandCoverOptions o, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        string gpkgPath = await _datasets.EnsureAsync(_kind, progress, ct);
        progress?.Report(new ProgressInfo("landcover", 10, $"Reading {Name}"));
        var cover = await Task.Run(() => Read(gpkgPath, grid, o, ct), ct);
        progress?.Report(new ProgressInfo("landcover", 100,
            $"{Name}: water {cover.WaterCount:N0}, forest {cover.ForestCount:N0}" +
            (o.Roads ? $", road {cover.RoadCount:N0}" : "") +
            (o.Rails ? $", rail {cover.RailCount:N0}" : "") +
            (o.Buildings ? $", building {cover.BuildingCellCount:N0}" : "") +
            $" of {cover.Water.Length:N0} cells" +
            (o.Power ? $"; {cover.Structures.Count:N0} pylons" : "") +
            (o.Signs ? $"; {cover.Signs.Count:N0} signs" : "")));
        return cover;
    }

    private LandCover Read(string gpkgPath, HeightGrid grid, LandCoverOptions o, CancellationToken ct)
    {
        using var gpkg = new GeoPackage(gpkgPath);
        bool regio = _kind == TlmKind.Regio;
        int w = grid.Width, h = grid.Height, n = w * h;
        var bbox = new Lv95Rect(grid.MinE, grid.MinN, grid.MaxE, grid.MaxN).Expand(60);
        var raster = new GridRasterizer(grid);

        var water = new bool[n];
        var forest = new bool[n];
        var coverClass = new byte[n];
        byte[]? road = o.Roads ? new byte[n] : null, roadFlags = o.Roads ? new byte[n] : null, roadRank = o.Roads ? new byte[n] : null;
        byte[]? rail = o.Rails ? new byte[n] : null;
        int[]? buildingId = o.Buildings ? new int[n] : null;
        byte[]? buildingLevels = o.Buildings ? new byte[n] : null;
        byte[]? buildingFlags = o.Buildings ? new byte[n] : null;
        var structures = new List<Structure>();
        var wires = new List<Wire>();
        var signs = new List<SignSpec>();
        var points = new List<PointOfInterest>();
        bool markings = grid.CellSize <= 2;

        List<(double X, double Z)> Pts(IReadOnlyList<(double X, double Y, double Z)> pts) =>
            pts.Select(p => ((p.X - grid.OriginE) / grid.CellSize, (grid.OriginN - p.Y) / grid.CellSize)).ToList();

        IEnumerable<PolygonGeometry> Polygons(Geometry g) => g switch
        {
            PolygonGeometry p => [p],
            MultiGeometry m => m.Parts.SelectMany(Polygons),
            _ => [],
        };

        IEnumerable<LineGeometry> Lines(Geometry g) => g switch
        {
            LineGeometry l => [l],
            MultiGeometry m => m.Parts.SelectMany(Lines),
            _ => [],
        };

        void Fill(Geometry g, Action<int> paint)
        {
            foreach (var poly in Polygons(g)) raster.FillRingsWith(poly.Rings.Select(Pts).ToList(), paint);
        }

        // ---- land cover polygons ------------------------------------------------------------------
        string? lcLayer = gpkg.FindLayer(regio ? "tlmregio_landcover_landcover" : "tlm_bb_bodenbedeckung", "bodenbedeckung", "landcover");
        if (lcLayer is not null)
        {
            foreach (var f in gpkg.Query(lcLayer, bbox, "objval", "objektart"))
            {
                ct.ThrowIfCancellationRequested();
                string cls = (regio ? f.Text("objval") : f.Text("objektart")).ToLowerInvariant();
                CoverClass cc = CoverClass.None;
                bool isWater = false, isForest = false;
                switch (cls)
                {
                    case "wald": case "wald offen": case "gebueschwald": case "gehoelzflaeche": isForest = true; break;
                    case "see": case "stausee": case "stehende gewaesser": case "fliessgewaesser": isWater = true; break;
                    case "fels": case "fels locker": case "felsbloecke": case "felsbloecke locker": cc = CoverClass.Rock; break;
                    case "geroell": case "lockergestein": case "lockergestein locker": cc = CoverClass.Scree; break;
                    case "gletscher": case "schneefeld toteis": cc = CoverClass.Glacier; break;
                    case "sumpf": case "feuchtgebiet": cc = CoverClass.Wetland; break;
                    case "obstanlage": cc = CoverClass.Orchard; break;
                    case "reben": cc = CoverClass.Vineyard; break;
                    case "siedl": case "stadtzentr": cc = CoverClass.Settlement; break;
                    default: continue;
                }
                Fill(f.Geometry, cell =>
                {
                    if (isWater) water[cell] = true;
                    else if (isForest) forest[cell] = true;
                    else coverClass[cell] = (byte)cc;
                });
            }
        }

        // swissTLM3D keeps vineyards and orchards in the land-use layer
        if (!regio && gpkg.FindLayer("tlm_areale_nutzungsareal") is { } useLayer)
        {
            foreach (var f in gpkg.Query(useLayer, bbox, "objektart"))
            {
                string cls = f.Text("objektart").ToLowerInvariant();
                CoverClass cc = cls switch
                {
                    "reben" => CoverClass.Vineyard,
                    "obstanlage" or "baumschule" => CoverClass.Orchard,
                    _ => CoverClass.None,
                };
                if (cc == CoverClass.None) continue;
                Fill(f.Geometry, cell => { if (!water[cell] && !forest[cell]) coverClass[cell] = (byte)cc; });
            }
        }

        // ---- lakes and rivers ----------------------------------------------------------------------
        string? lakeLayer = gpkg.FindLayer(regio ? "tlmregio_hydrography_lake" : "tlm_gewaesser_stehendes_gewaesser_ply", "hydrography_lake");
        if (lakeLayer is not null)
            foreach (var f in gpkg.Query(lakeLayer, bbox))
                Fill(f.Geometry, cell => water[cell] = true);

        string? riverLayer = gpkg.FindLayer(regio ? "tlmregio_hydrography_flowingwater" : "tlm_gewaesser_fliessgewaesser", "flowingwater", "fliessgewaesser");
        if (riverLayer is not null)
        {
            foreach (var f in gpkg.Query(riverLayer, bbox, "objval", "objektart", "breite", "klasse", "verlauf", "gewaesser_breite"))
            {
                ct.ThrowIfCancellationRequested();
                string cls = (regio ? f.Text("objval") : f.Text("objektart")).ToLowerInvariant();
                if (cls.Contains("seeachse") || cls.EndsWith("_u") || cls.Contains("trockenrinne") || cls.Contains("druck") || cls.Contains("unterirdisch")
                    || f.Text("verlauf").Contains("unterirdisch", StringComparison.OrdinalIgnoreCase)) continue;
                double width = cls.Contains("bisse") || cls.Contains("suone") ? 1 : RiverWidth(f, regio);
                foreach (var line in Lines(f.Geometry))
                    raster.StrokeLine(Pts(line.Points), width / grid.CellSize / 2, (cell, _, _) => water[cell] = true);
            }
        }

        // ---- roads ---------------------------------------------------------------------------------
        if (road is not null)
        {
            string? roadLayer = gpkg.FindLayer(regio ? "tlmregio_transportation_road" : "tlm_strassen_strasse", "transportation_road", "strasse");
            if (roadLayer is not null)
            {
                foreach (var f in gpkg.Query(roadLayer, bbox, "objval", "objektart", "namn", "name", "strassenname", "construct", "kunstbaute", "belagsart", "wanderwege"))
                {
                    ct.ThrowIfCancellationRequested();
                    string construct = (regio ? f.Text("construct") : f.Text("kunstbaute")).ToLowerInvariant();
                    if (construct.Contains("tunnel") || construct.Contains("galerie") || construct.Contains("unterfuehrung")) continue;
                    bool bridge = construct.Contains("br");
                    var rc = regio ? RegioRoad(f.Text("objval")) : Tlm3dRoad(f.Text("objektart"), f.Text("belagsart"));
                    if (rc is null) continue;
                    var cls = rc.Value;
                    double hw = cls.WidthM / grid.CellSize / 2;
                    byte rank = (byte)cls.Rank, flagBridge = bridge ? RoadFlag.Bridge : (byte)0;
                    string name = regio ? f.Text("namn") : (f.Text("strassenname").Length > 0 ? f.Text("strassenname") : f.Text("name"));
                    foreach (var line in Lines(f.Geometry))
                    {
                        var pts = Pts(line.Points);
                        raster.StrokeLine(pts, hw, (cell, dist, s) =>
                        {
                            if (roadRank![cell] > rank) return;
                            roadRank[cell] = rank;
                            road[cell] = (byte)cls.Material;
                            byte flags = flagBridge;
                            if (markings)
                            {
                                double sMetres = s * grid.CellSize;
                                bool centre = cls.CentreLine && dist <= 0.5 && (cls.SolidCentre || sMetres % 8 < 4);
                                bool edge = cls.EdgeLines && dist > Math.Max(hw, 0.5) - 1;
                                if (centre || edge) flags |= RoadFlag.Marking;
                            }
                            roadFlags![cell] = flags;
                        });
                        if (o.Signs && name.Length > 0 && cls.Rank >= 2) SignPlanner.AlongRoad(pts, hw, name, grid, w, h, signs);
                    }
                }
            }
        }

        // ---- railways -------------------------------------------------------------------------------
        if (rail is not null)
        {
            string? railLayer = gpkg.FindLayer(regio ? "tlmregio_transportation_railway" : "tlm_oev_eisenbahn", "transportation_railway", "eisenbahn");
            if (railLayer is not null)
            {
                foreach (var f in gpkg.Query(railLayer, bbox, "objval", "objektart", "construct", "kunstbaute", "verkehrsmittel"))
                {
                    ct.ThrowIfCancellationRequested();
                    string cls = (regio ? f.Text("objval") : f.Text("objektart")).ToLowerInvariant();
                    if (cls.Contains("luftseil") || cls.Contains("seilbahn") && !cls.Contains("standseil") || cls.Contains("sessel") || cls.Contains("skilift")) continue;
                    string construct = (regio ? f.Text("construct") : f.Text("kunstbaute")).ToLowerInvariant();
                    if (construct.Contains("tunnel") || construct.Contains("galerie")) continue;
                    byte bridgeBit = construct.Contains("br") ? RailFlag.Bridge : (byte)0;
                    foreach (var line in Lines(f.Geometry))
                    {
                        var pts = Pts(line.Points);
                        raster.StrokeLine(pts, 1.5 / grid.CellSize, (cell, _, _) => rail[cell] |= (byte)(RailFlag.Bed | bridgeBit));
                        raster.StrokePath4(pts, cell => rail[cell] |= (byte)(RailFlag.Track | bridgeBit));
                    }
                }
            }
        }

        // ---- building footprints (used when no measured model is selected) ---------------------------
        if (buildingId is not null)
        {
            string? bLayer = gpkg.FindLayer(regio ? "tlmregio_buildings_building" : "tlm_bauten_gebaeude_footprint", "gebaeude_footprint", "buildings_building");
            if (bLayer is not null)
            {
                int next = 0;
                foreach (var f in gpkg.Query(bLayer, bbox, "objektart", "objval"))
                {
                    ct.ThrowIfCancellationRequested();
                    string kind = (regio ? f.Text("objval") : f.Text("objektart")).ToLowerInvariant();
                    if (kind.Contains("unterirdisch") || kind.Contains("mauer") || kind.Contains("verbindungsbruecke") || kind.Contains("lueftung") || kind.Contains("im bau")) continue;
                    int id = ++next;
                    bool church = kind.Contains("kirche") || kind.Contains("kapelle") || kind.Contains("sakral");
                    byte levels = kind.Contains("hochhaus") ? (byte)12 : kind.Contains("turm") || kind.Contains("hochkamin") ? (byte)8 : church ? (byte)3 : (byte)2;
                    byte flags = church ? BuildingFlag.Church
                        : kind.Contains("lager") || kind.Contains("industrie") || kind.Contains("flugdach") || kind.Contains("treibhaus") || kind.Contains("hochkamin") || kind.Contains("offenes") ? BuildingFlag.Industrial : (byte)0;
                    Fill(f.Geometry, cell => { buildingId[cell] = id; buildingLevels![cell] = levels; buildingFlags![cell] = flags; });
                }
                for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = z * w + x, id = buildingId[i];
                        if (id == 0) continue;
                        bool edge = x == 0 || z == 0 || x == w - 1 || z == h - 1 || buildingId[i - 1] != id || buildingId[i + 1] != id || buildingId[i - w] != id || buildingId[i + w] != id;
                        if (edge) buildingFlags![i] |= BuildingFlag.Edge;
                    }
            }
        }

        // ---- power lines -----------------------------------------------------------------------------
        if (o.Power)
        {
            string? supplyLayer = gpkg.FindLayer(regio ? "tlmregio_miscellaneous_supply" : "tlm_bauten_leitung", "bauten_leitung", "miscellaneous_supply");
            if (supplyLayer is not null)
            {
                foreach (var f in gpkg.Query(supplyLayer, bbox, "objval", "objektart", "loc", "netzebene"))
                {
                    ct.ThrowIfCancellationRequested();
                    string cls = (regio ? f.Text("objval") : f.Text("objektart")).ToLowerInvariant();
                    if (!cls.Contains("hochspannung") && !cls.Contains("stromleitung")) continue;
                    if (f.Text("loc").Contains("underground", StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var line in Lines(f.Geometry))
                    {
                        var pts = Pts(line.Points);
                        for (int i = 0; i < pts.Count; i++)
                        {
                            var (px, pz) = pts[i];
                            var (qx, qz) = i + 1 < pts.Count ? pts[i + 1] : pts[Math.Max(0, i - 1)];
                            double dx = qx - px, dz = qz - pz, len = Math.Sqrt(dx * dx + dz * dz);
                            if (len < 1e-9) { dx = 1; dz = 0; len = 1; }
                            int cx = (int)Math.Floor(px), cz = (int)Math.Floor(pz);
                            if (cx >= 0 && cz >= 0 && cx < w && cz < h) structures.Add(new Structure(StructureKind.Tower, cx, cz, dx / len, dz / len));
                            if (i + 1 < pts.Count) wires.Add(new Wire(true, px, pz, qx, qz));
                        }
                    }
                }
            }
            // wind turbines (swissTLM3D only; Regio has none)
            if (!regio && gpkg.FindLayer("tlm_bauten_versorgungsbaute_pkt", "versorgungsbaute_pkt") is { } vb)
            {
                foreach (var f in gpkg.Query(vb, bbox, "objektart"))
                {
                    if (!f.Text("objektart").Contains("windturbine", StringComparison.OrdinalIgnoreCase)) continue;
                    if (f.Geometry is PointGeometry p)
                    {
                        int x = (int)Math.Floor((p.X - grid.OriginE) / grid.CellSize), z = (int)Math.Floor((grid.OriginN - p.Y) / grid.CellSize);
                        if (x >= 0 && z >= 0 && x < w && z < h) structures.Add(new Structure(StructureKind.WindTurbine, x, z, 1, 0));
                    }
                }
            }
        }

        // ---- points of interest and place names ---------------------------------------------------------
        string? poiLayer = gpkg.FindLayer(regio ? "tlmregio_miscellaneous_poi" : "tlm_oev_haltestelle_nonexistent", "miscellaneous_poi");
        if (poiLayer is not null)
        {
            foreach (var f in gpkg.Query(poiLayer, bbox, "objval", "namn1", "name"))
            {
                if (f.Geometry is not PointGeometry p) continue;
                string kind = f.Text("objval").ToLowerInvariant();
                if (kind is not ("kirche" or "kloster" or "kapelle")) continue;
                int x = (int)Math.Floor((p.X - grid.OriginE) / grid.CellSize), z = (int)Math.Floor((grid.OriginN - p.Y) / grid.CellSize);
                if (x >= 0 && z >= 0 && x < w && z < h) points.Add(new PointOfInterest(x, z, "church", f.Text("namn1")));
            }
        }
        if (o.Signs)
        {
            string? nameLayer = gpkg.FindLayer(regio ? "tlmregio_names_namedlocation" : "tlm_namen_siedlungsname_zentrum", "names_namedlocation", "siedlungsname_zentrum");
            if (nameLayer is not null)
            {
                foreach (var f in gpkg.Query(nameLayer, bbox, "namn1", "name", "objval", "objektart"))
                {
                    if (f.Geometry is not PointGeometry p) continue;
                    string name = f.Text("namn1").Length > 0 ? f.Text("namn1") : f.Text("name");
                    if (name.Length == 0) continue;
                    if (!regio && f.Text("objektart") is not ("Ort" or "Ortsteil")) continue;
                    int x = (int)Math.Floor((p.X - grid.OriginE) / grid.CellSize), z = (int)Math.Floor((grid.OriginN - p.Y) / grid.CellSize);
                    if (x >= 0 && z >= 0 && x < w && z < h) signs.Add(new SignSpec(x, z, 0, SignPlanner.SplitSignText(name)));
                }
            }
        }

        // ---- single trees outside forests (swissTLM3D only) ------------------------------------------
        var singleTrees = new List<(int X, int Z)>();
        if (!regio && gpkg.FindLayer("tlm_bb_einzelbaum") is { } treeLayer)
        {
            foreach (var f in gpkg.Query(treeLayer, bbox))
            {
                if (f.Geometry is not PointGeometry p) continue;
                int x = (int)Math.Floor((p.X - grid.OriginE) / grid.CellSize), z = (int)Math.Floor((grid.OriginN - p.Y) / grid.CellSize);
                if (x >= 0 && z >= 0 && x < w && z < h && !forest[z * w + x]) singleTrees.Add((x, z));
            }
        }

        return new LandCover
        {
            Water = water, Forest = forest, Cover = coverClass,
            Road = road, RoadFlags = roadFlags, Rail = rail,
            BuildingId = buildingId, BuildingLevels = buildingLevels, BuildingFlags = buildingFlags,
            Structures = structures, Wires = wires, Signs = signs, Points = points, SingleTrees = singleTrees,
        };
    }

    /// <summary>River width in metres: TLMRegio codes widths up to 30 m directly; otherwise the stream order decides.</summary>
    private static double RiverWidth(Feature f, bool regio)
    {
        double breite = f.Number("breite", 0);
        if (!regio) breite = f.Number("gewaesser_breite", breite);
        if (breite > 0 && breite <= 30) return breite;
        int klasse = (int)f.Number("klasse", 0);
        return klasse switch { >= 10 => 40, 9 => 25, 8 => 15, 7 => 10, 6 => 6, 5 => 4, 4 => 3, _ => 2.5 };
    }

    private static RoadClass? RegioRoad(string objval) => objval switch
    {
        "Autobahn" => new(10, 14, RoadMaterial.Asphalt, true, true, true),
        "Autob_Ri" => new(10, 8, RoadMaterial.Asphalt, false, true, false),
        "Autostr" => new(9, 10, RoadMaterial.Asphalt, true, true, true),
        "HauptStrAB6" => new(7, 8, RoadMaterial.Asphalt, true, false, false),
        "HauptStrAB4" => new(7, 6, RoadMaterial.Asphalt, true, false, false),
        "NebenStr6" or "VerbindStr6" => new(6, 6, RoadMaterial.Asphalt, true, false, false),
        "NebenStr3" or "VerbindStr4" => new(5, 4, RoadMaterial.Asphalt, false, false, false),
        "Fahrstraes" => new(2, 3, RoadMaterial.Gravel, false, false, false),
        "Fussweg" => new(1, 1.5, RoadMaterial.DirtPath, false, false, false),
        _ when objval.EndsWith("_Rampe") => new(8, 5, RoadMaterial.Asphalt, false, false, false),
        _ => null,
    };

    /// <summary>swissTLM3D road classes carry their width in the name; Belagsart tells paved from natural.</summary>
    private static RoadClass? Tlm3dRoad(string objektart, string belagsart)
    {
        string k = objektart.ToLowerInvariant();
        bool natural = belagsart.Contains("natur", StringComparison.OrdinalIgnoreCase);
        return k switch
        {
            "autobahn" => new(10, 14, RoadMaterial.Asphalt, true, true, true),
            "autostrasse" => new(9, 10, RoadMaterial.Asphalt, true, true, true),
            "10m strasse" => new(8, 10, RoadMaterial.Asphalt, true, false, false),
            "8m strasse" => new(7, 8, RoadMaterial.Asphalt, true, false, false),
            "6m strasse" => new(6, 6, RoadMaterial.Asphalt, true, false, false),
            "4m strasse" => new(5, 4, natural ? RoadMaterial.Gravel : RoadMaterial.Asphalt, false, false, false),
            "3m strasse" => new(4, 3, natural ? RoadMaterial.Gravel : RoadMaterial.LightAsphalt, false, false, false),
            "2m weg" or "2m wegfragment" => new(2, 2, natural ? RoadMaterial.DirtPath : RoadMaterial.LightAsphalt, false, false, false),
            "1m weg" or "1m wegfragment" => new(1, 1.5, RoadMaterial.DirtPath, false, false, false),
            "ausfahrt" or "einfahrt" or "zufahrt" or "dienstzufahrt" or "verbindung" => new(6, 5, RoadMaterial.Asphalt, false, false, false),
            "platz" => new(3, 6, RoadMaterial.LightAsphalt, false, false, false),
            "markierte spur" => new(1, 1, RoadMaterial.DirtPath, false, false, false),
            _ when k.Contains("strasse") => new(5, 5, RoadMaterial.Asphalt, false, false, false),
            _ when k.Contains("weg") => new(1, 1.5, RoadMaterial.DirtPath, false, false, false),
            _ => null,
        };
    }
}

/// <summary>Shared sign placement along a road centreline (shared by land cover sources).</summary>
public static class SignPlanner
{
    /// <summary>Splits a name into at most four sign lines of at most 15 characters.</summary>
    public static string[] SplitSignText(string name)
    {
        const int max = 15;
        var lines = new List<string>();
        var current = "";
        foreach (var rawWord in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = rawWord;
            while (word.Length > max)
            {
                if (current.Length > 0) { lines.Add(current); current = ""; }
                lines.Add(word[..(max - 1)] + "-");
                word = word[(max - 1)..];
            }
            if (current.Length == 0) current = word;
            else if (current.Length + 1 + word.Length <= max) current += " " + word;
            else { lines.Add(current); current = word; }
        }
        if (current.Length > 0) lines.Add(current);
        var result = new string[4];
        for (int i = 0; i < 4; i++) result[i] = i < lines.Count ? lines[i] : "";
        return result;
    }

    public static void AlongRoad(List<(double X, double Z)> pts, double hw, string name, HeightGrid grid, int w, int h, List<SignSpec> signs)
    {
        double spacing = 120 / grid.CellSize, first = 10 / grid.CellSize;
        double next = first, arc = 0;
        var lines = SignPlanner.SplitSignText(name);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, az) = pts[i];
            var (bx, bz) = pts[i + 1];
            double dx = bx - ax, dz = bz - az, len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-9) continue;
            while (next <= arc + len)
            {
                double t = (next - arc) / len;
                double px = -dz / len, pz = dx / len;
                double off = hw + 1.5;
                int sx = (int)Math.Floor(ax + dx * t + px * off), sz = (int)Math.Floor(az + dz * t + pz * off);
                if (sx >= 0 && sz >= 0 && sx < w && sz < h)
                {
                    double angle = Math.Atan2(px, -pz) * 180 / Math.PI;
                    int rot = ((int)Math.Round(angle / 22.5) % 16 + 16) % 16;
                    signs.Add(new SignSpec(sx, sz, (byte)rot, lines));
                }
                next += spacing;
            }
            arc += len;
        }
    }
}