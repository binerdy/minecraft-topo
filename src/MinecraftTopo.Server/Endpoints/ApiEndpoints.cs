using System.Diagnostics;
using System.Text.Json;
using MinecraftTopo.Core;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Geo;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Server.Jobs;

namespace MinecraftTopo.Server.Endpoints;

public static class ApiEndpoints
{
    public const string Attribution = "Elevation data and map tiles © swisstopo (Federal Office of Topography), free geodata (OGD).";

    private static object DatasetDto(MinecraftTopo.Core.Tlm.TlmDatasets tlm, MinecraftTopo.Core.Tlm.TlmKind kind)
    {
        var s = tlm.Status(kind);
        return new
        {
            Name = MinecraftTopo.Core.Tlm.TlmDatasets.DisplayName(kind),
            Ready = tlm.IsReady(kind),
            s.Phase,
            s.Percent,
            s.Message,
            ApproxBytes = MinecraftTopo.Core.Tlm.TlmDatasets.ApproxBytes(kind),
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<MinecraftTopo.Core.Elevation.SwissBathy3dSource.LakeInfo>> LakeCache = new();

    private static async Task<IReadOnlyList<MinecraftTopo.Core.Elevation.SwissBathy3dSource.LakeInfo>> LakesCached(MinecraftTopo.Core.Elevation.SwissBathy3dSource bathy, Lv95Rect area)
    {
        string key = $"{Math.Floor(area.MinE / 500)},{Math.Floor(area.MinN / 500)},{Math.Ceiling(area.MaxE / 500)},{Math.Ceiling(area.MaxN / 500)}";
        if (LakeCache.TryGetValue(key, out var cached)) return cached;
        var lakes = await bathy.FindLakesAsync(area, CancellationToken.None);
        LakeCache[key] = lakes;
        return lakes;
    }

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ---- overview ----------------------------------------------------------------------
        api.MapGet("/overview/status", (Dhm200Model dhm) =>
        {
            var s = dhm.Status;
            return Results.Ok(new { s.Phase, s.Percent, s.Message, Ready = dhm.IsReady });
        });

        api.MapGet("/overview/tiles/{z:int}/{x:int}/{y:int}.png", async (int z, int x, int y, bool? roads, OverviewTileRenderer renderer, HttpContext ctx, CancellationToken ct) =>
        {
            if (z < OverviewTileRenderer.MinZoom || z > OverviewTileRenderer.MaxZoom) return Results.NotFound();
            int n = 1 << z;
            if (x < 0 || y < 0 || x >= n || y >= n) return Results.NotFound();
            var png = await renderer.GetTileAsync(z, x, y, ct, roads == true);
            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Bytes(png, "image/png");
        });

        // ---- geo helpers -------------------------------------------------------------------
        api.MapGet("/elevation", (double e, double n, Dhm200Model dhm) =>
        {
            if (!dhm.IsReady) return Results.Ok(new { Elevation = (double?)null });
            var g = dhm.Grid;
            float v = g.ContainsPoint(e, n) ? g.SampleBilinear(e, n) : float.NaN;
            return Results.Ok(new { Elevation = float.IsNaN(v) ? (double?)null : Math.Round(v, 1) });
        });

        api.MapGet("/estimate", async (double minE, double minN, double maxE, double maxN, double? mpb, string? source,
            double? res, int? baseY, double? vscale, bool? lakeFloors, bool? canopy, bool? names, string? height,
            Dhm200Model dhm, MinecraftTopo.Core.Elevation.SwissBathy3dSource bathy, MinecraftTopo.Core.Names.SwissNamesSource namesSource, AppPaths paths) =>
        {
            var area = Lv95Rect.FromCorners(minE, minN, maxE, maxN);
            if (!Enum.TryParse<ElevationSourceKind>(source, ignoreCase: true, out var sourceKind)) sourceKind = ElevationSourceKind.Auto;
            var req = new GenerationRequest
            {
                Area = area,
                WorldName = "estimate",
                OutputDir = Path.GetTempPath(),
                MetresPerBlock = mpb is > 0 ? mpb.Value : 1,
                Source = sourceKind,
                Alti3dResolution = res ?? 2,
                Terrain = new TerrainOptions { BaseY = baseY ?? -60, VerticalScale = vscale, WorldHeight = (height ?? "auto").ToLowerInvariant() switch { "tall" => WorldHeight.Tall, "standard" => WorldHeight.Standard, _ => (mpb is > 0 ? mpb.Value : 1) <= 2 ? WorldHeight.Tall : WorldHeight.Standard } },
            };
            var resolved = req.ResolveSource();
            int tiles = resolved == ElevationSourceKind.SwissAlti3d ? SwissAlti3dSource.CountTiles(area) : 0;
            long tileBytes = resolved == ElevationSourceKind.SwissAlti3d ? (req.Alti3dResolution == 2 ? 2_500_000L : 40_000_000L) : 0;

            double? elevMin = null, elevMax = null, verticalScale = null;
            double? trueProportionMpb = null;
            if (dhm.IsReady && area.Width > 0 && area.Height > 0)
            {
                var g = dhm.Grid;
                float mn = float.PositiveInfinity, mx = float.NegativeInfinity;
                int steps = 64;
                for (int i = 0; i <= steps; i++)
                {
                    double n = area.MinN + area.Height * i / steps;
                    for (int j = 0; j <= steps; j++)
                    {
                        double e = area.MinE + area.Width * j / steps;
                        if (!g.ContainsPoint(e, n)) continue;
                        float v = g.SampleBilinear(e, n);
                        if (float.IsNaN(v)) continue;
                        if (v < mn) mn = v;
                        if (v > mx) mx = v;
                    }
                }
                if (!float.IsPositiveInfinity(mn))
                {
                    elevMin = Math.Round(mn); elevMax = Math.Round(mx);
                    verticalScale = Math.Round(SurfaceClassifier.ResolveVerticalScale(req.Terrain, mn, mx, req.MetresPerBlock), 4);
                    double tp = SurfaceClassifier.TrueProportionMetresPerBlock(req.Terrain, mn, mx);
                    if (tp > req.MetresPerBlock + 1e-9) trueProportionMpb = tp;
                }
            }

            var warnings = new List<string>();
            if (req.TotalBlocks > WorldGenerator.MaxBlocks) warnings.Add($"Too many blocks ({req.TotalBlocks:N0}); the limit is {WorldGenerator.MaxBlocks:N0}. Increase metres per block or shrink the area.");
            else if (req.TotalBlocks > 100_000_000) warnings.Add($"{req.TotalBlocks:N0} blocks: generation will take several minutes and need a few GB of RAM.");
            if (tiles > 200) warnings.Add($"{tiles} swissALTI3D tiles (~{tiles * tileBytes / 1_048_576.0:0} MB) would be downloaded.");
            if (elevMin is null && dhm.IsReady) warnings.Add("The area seems to be outside Switzerland; no elevation data is available there.");
            if (verticalScale is { } vsv && vsv < 1.0 / req.MetresPerBlock - 1e-9) warnings.Add($"Relief is squeezed vertically (scale {vsv:0.###}) to fit the world height." +
                (trueProportionMpb is { } tpm ? $" True proportions need at least {tpm} m per block" + (req.Terrain.WorldHeight == WorldHeight.Tall ? "." : ", or a tall world.") : ""));
            // one-time downloads of the optional sources
            if (lakeFloors == true && resolved != ElevationSourceKind.Synthetic && area.Width * area.Height < 4e9)
            {
                try
                {
                    var lakes = await LakesCached(bathy, area);
                    var missing = lakes.Where(l => !File.Exists(Path.Combine(paths.BathyCacheDir, l.Id + ".xyz.zip"))).ToList();
                    if (missing.Count > 0) warnings.Add($"Lake floors: {string.Join(", ", missing.Select(l => l.Name))} ({missing.Sum(l => l.Bytes ?? 100_000_000) / 1_048_576.0:0} MB) will be downloaded once.");
                }
                catch (Exception) { /* offline: no warning */ }
            }
            if (canopy == true && req.MetresPerBlock <= 4 && resolved != ElevationSourceKind.Synthetic)
            {
                int t2 = SwissAlti3dSource.CountTiles(area);
                warnings.Add($"Tree heights: {t2} swissSURFACE3D tile{(t2 == 1 ? "" : "s")} (~{t2 * 20} MB, cached after the first run).");
            }
            if (names == true && !namesSource.IsReady) warnings.Add("Name signs: swissNAMES3D (33 MB) will be downloaded once.");

            return Results.Ok(new
            {
                WidthM = area.Width,
                HeightM = area.Height,
                req.BlocksWide,
                req.BlocksHigh,
                Blocks = req.TotalBlocks,
                Chunks = req.TotalChunks,
                Regions = req.TotalRegions,
                Source = resolved,
                Tiles = tiles,
                DownloadBytes = tiles * tileBytes,
                OutputBytes = req.TotalChunks * 6_000L,
                ElevationMin = elevMin,
                ElevationMax = elevMax,
                VerticalScale = verticalScale,
                TrueProportionMetresPerBlock = trueProportionMpb,
                AvailableHeight = (int)SurfaceClassifier.AvailableHeight(req.Terrain),
                Warnings = warnings,
                Ok = req.TotalBlocks <= WorldGenerator.MaxBlocks && area.Width > 0 && area.Height > 0,
            });
        });

        // ---- swisstopo landscape model datasets (one-time downloads) --------------------------------
        api.MapGet("/datasets", (MinecraftTopo.Core.Tlm.TlmDatasets tlm) => Results.Ok(new
        {
            Tlm3d = DatasetDto(tlm, MinecraftTopo.Core.Tlm.TlmKind.Tlm3d),
            TlmRegio = DatasetDto(tlm, MinecraftTopo.Core.Tlm.TlmKind.Regio),
        }));

        api.MapPost("/datasets/{kind}/prepare", (string kind, MinecraftTopo.Core.Tlm.TlmDatasets tlm) =>
        {
            var k = kind.ToLowerInvariant() switch { "tlm3d" => MinecraftTopo.Core.Tlm.TlmKind.Tlm3d, "regio" or "tlmregio" => MinecraftTopo.Core.Tlm.TlmKind.Regio, _ => (MinecraftTopo.Core.Tlm.TlmKind?)null };
            if (k is null) return Results.NotFound();
            if (!tlm.IsReady(k.Value) && tlm.Status(k.Value).Phase != "tlm")
            {
                _ = Task.Run(async () =>
                {
                    try { await tlm.EnsureAsync(k.Value, null, CancellationToken.None); }
                    catch (Exception) { /* the status carries the error */ }
                });
            }
            return Results.Ok(DatasetDto(tlm, k.Value));
        });

        // ---- place search (swisstopo location search) -------------------------------------------------
        api.MapGet("/search", async (string q, Downloader downloader, CancellationToken ct) =>
        {
            q = (q ?? "").Trim();
            if (q.Length < 2) return Results.Ok(Array.Empty<object>());
            string url = "https://api3.geo.admin.ch/rest/services/api/SearchServer?type=locations&sr=2056&limit=12&searchText=" + Uri.EscapeDataString(q);
            string json;
            try { json = await downloader.GetStringAsync(url, ct); }
            catch (HttpRequestException ex) { return Results.Json(new { error = "Search service not reachable: " + ex.Message }, statusCode: 502); }
            using var doc = JsonDocument.Parse(json);
            var list = new List<object>();
            if (doc.RootElement.TryGetProperty("results", out var results))
            {
                foreach (var r in results.EnumerateArray())
                {
                    if (!r.TryGetProperty("attrs", out var a)) continue;
                    string label = System.Text.RegularExpressions.Regex.Replace(a.GetProperty("label").GetString() ?? "", "<[^>]+>", "").Trim();
                    double lat = a.GetProperty("lat").GetDouble(), lon = a.GetProperty("lon").GetDouble();
                    object? box = null;
                    var m = System.Text.RegularExpressions.Regex.Match(a.TryGetProperty("geom_st_box2d", out var b) ? b.GetString() ?? "" : "",
                        @"BOX\(([\d.]+) ([\d.]+),([\d.]+) ([\d.]+)\)");
                    if (m.Success)
                    {
                        double e1 = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), n1 = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                        double e2 = double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), n2 = double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                        if (e2 - e1 > 50 && n2 - n1 > 50) box = new { MinE = e1, MinN = n1, MaxE = e2, MaxN = n2 };
                    }
                    list.Add(new { Label = label, Origin = a.TryGetProperty("origin", out var o) ? o.GetString() : null, Lat = lat, Lon = lon, Box = box });
                }
            }
            return Results.Ok(list);
        });

        // ---- block roles and choices --------------------------------------------------------------
        api.MapGet("/blocks", () => Results.Ok(new
        {
            Roles = MinecraftTopo.Core.Anvil.BlockRoles.All.Select(r => new { r.Key, r.Label, r.Group, r.Default }),
            MinecraftTopo.Core.Anvil.BlockRoles.Choices,
        }));

        // ---- administrative unit under a point (needs the swissTLMRegio boundaries) ------------------
        api.MapGet("/region", (double e, double n, string? level, MinecraftTopo.Core.Tlm.RegionLookup regions) =>
        {
            var lvl = (level ?? "municipality").ToLowerInvariant() switch
            {
                "district" or "bezirk" => MinecraftTopo.Core.Tlm.RegionLevel.District,
                "canton" or "kanton" => MinecraftTopo.Core.Tlm.RegionLevel.Canton,
                _ => MinecraftTopo.Core.Tlm.RegionLevel.Municipality,
            };
            if (!regions.IsAvailable)
                return Results.Json(new { error = "Selecting a region needs the swissTLMRegio dataset (160 MB one-time download). Download it under Terrain > Water and forest outlines and try again." }, statusCode: 409);
            try
            {
                var r = regions.Find(e, n, lvl);
                return r is null
                    ? Results.NotFound(new { error = $"No {lvl.ToString().ToLowerInvariant()} at this point." })
                    : Results.Ok(new { r.Name, Level = lvl.ToString().ToLowerInvariant(), Bounds = new { r.Bounds.MinE, r.Bounds.MinN, r.Bounds.MaxE, r.Bounds.MaxN }, r.AreaKm2, r.Outline });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 409);
            }
        });

        // ---- defaults / cache ----------------------------------------------------------------
        api.MapGet("/defaults", (AppPaths paths, Dhm200Model dhm) =>
        {
            string? saves = AppPaths.DefaultMinecraftSavesDir();
            return Results.Ok(new
            {
                SavesDir = saves,
                SavesDirExists = saves is not null && Directory.Exists(saves),
                CacheBytes = paths.CacheSizeBytes(),
                CacheDir = paths.CacheDir,
                Dhm200Ready = dhm.IsReady,
                Attribution,
                MinecraftVersion = Core.Anvil.WorldWriter.VersionName,
                DataVersion = Core.Anvil.WorldWriter.DataVersion,
                SwitzerlandExtent = new { Lv95.SwitzerlandExtent.MinE, Lv95.SwitzerlandExtent.MinN, Lv95.SwitzerlandExtent.MaxE, Lv95.SwitzerlandExtent.MaxN },
            });
        });

        api.MapPost("/cache/clear", (AppPaths paths) =>
        {
            paths.ClearCache(includeDhm200: false);
            return Results.Ok(new { CacheBytes = paths.CacheSizeBytes() });
        });

        // ---- jobs --------------------------------------------------------------------------------
        api.MapGet("/jobs", (JobManager jobs) => Results.Ok(jobs.All()));

        api.MapPost("/jobs", (JobRequestDto dto, JobManager jobs) =>
        {
            try
            {
                var job = jobs.Create(dto);
                return Results.Created($"/api/jobs/{job.Id}", job.ToDto());
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        api.MapGet("/jobs/{id}", (string id, JobManager jobs) =>
            jobs.Get(id) is { } job ? Results.Ok(job.ToDto()) : Results.NotFound());

        api.MapDelete("/jobs/{id}", (string id, JobManager jobs) =>
            jobs.Cancel(id) ? Results.Ok() : Results.NotFound());

        api.MapGet("/jobs/{id}/events", async (string id, JobManager jobs, HttpContext ctx, CancellationToken ct) =>
        {
            var job = jobs.Get(id);
            if (job is null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var jsonOptions = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
            string? last = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var dto = job.ToDto();
                    string json = JsonSerializer.Serialize(dto, jsonOptions);
                    if (json != last)
                    {
                        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                        await ctx.Response.Body.FlushAsync(ct);
                        last = json;
                    }
                    if (job.IsTerminal) break;
                    await Task.Delay(250, ct);
                }
            }
            catch (OperationCanceledException) { /* client went away */ }
        });

        api.MapGet("/jobs/{id}/map.png", (string id, string? stage, JobManager jobs) =>
        {
            var job = jobs.Get(id);
            if (job?.MapPng is null) return Results.NotFound();
            byte[] png;
            lock (job.Sync)
            {
                png = stage is null ? job.MapPng : job.Snapshots.LastOrDefault(s => s.Stage == stage).Png ?? job.MapPng;
            }
            return Results.Bytes(png, "image/png");
        });

        api.MapPost("/jobs/{id}/spawn", (string id, SpawnRequestDto body, JobManager jobs) =>
        {
            try { return Results.Ok(jobs.SetSpawn(id, body.X, body.Z)); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (InvalidOperationException ex) { return Results.Json(new { error = ex.Message }, statusCode: 409); }
        });

        api.MapGet("/jobs/{id}/download", (string id, JobManager jobs) =>
        {
            var job = jobs.Get(id);
            if (job?.ZipPath is null || !File.Exists(job.ZipPath)) return Results.NotFound();
            return Results.File(job.ZipPath, "application/zip", job.Request.WorldName + ".zip");
        });

        api.MapPost("/jobs/{id}/reveal", (string id, JobManager jobs) =>
        {
            var job = jobs.Get(id);
            if (job?.Result is null) return Results.NotFound();
            if (!OperatingSystem.IsWindows()) return Results.StatusCode(501);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{job.Result.OutputDir}\"") { UseShellExecute = true });
                return Results.Ok();
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        });
    }
}
