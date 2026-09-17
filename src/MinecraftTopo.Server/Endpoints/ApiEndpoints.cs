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

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ---- overview ----------------------------------------------------------------------
        api.MapGet("/overview/status", (Dhm200Model dhm) =>
        {
            var s = dhm.Status;
            return Results.Ok(new { s.Phase, s.Percent, s.Message, Ready = dhm.IsReady });
        });

        api.MapGet("/overview/tiles/{z:int}/{x:int}/{y:int}.png", async (int z, int x, int y, OverviewTileRenderer renderer, HttpContext ctx, CancellationToken ct) =>
        {
            if (z < OverviewTileRenderer.MinZoom || z > OverviewTileRenderer.MaxZoom) return Results.NotFound();
            int n = 1 << z;
            if (x < 0 || y < 0 || x >= n || y >= n) return Results.NotFound();
            var png = await renderer.GetTileAsync(z, x, y, ct);
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

        api.MapGet("/estimate", (double minE, double minN, double maxE, double maxN, double? mpb, string? source,
            double? res, int? baseY, double? vscale, Dhm200Model dhm) =>
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
                Terrain = new TerrainOptions { BaseY = baseY ?? 0, VerticalScale = vscale },
            };
            var resolved = req.ResolveSource();
            int tiles = resolved == ElevationSourceKind.SwissAlti3d ? SwissAlti3dSource.CountTiles(area) : 0;
            long tileBytes = resolved == ElevationSourceKind.SwissAlti3d ? (req.Alti3dResolution == 2 ? 2_500_000L : 40_000_000L) : 0;

            double? elevMin = null, elevMax = null, verticalScale = null;
            int? trueProportionMpb = null;
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
                    int tp = SurfaceClassifier.TrueProportionMetresPerBlock(req.Terrain, mn, mx);
                    if (tp > req.MetresPerBlock) trueProportionMpb = tp;
                }
            }

            var warnings = new List<string>();
            if (req.TotalBlocks > WorldGenerator.MaxBlocks) warnings.Add($"Too many blocks ({req.TotalBlocks:N0}); the limit is {WorldGenerator.MaxBlocks:N0}. Increase metres per block or shrink the area.");
            else if (req.TotalBlocks > 100_000_000) warnings.Add($"{req.TotalBlocks:N0} blocks: generation will take several minutes and need a few GB of RAM.");
            if (tiles > 200) warnings.Add($"{tiles} swissALTI3D tiles (~{tiles * tileBytes / 1_048_576.0:0} MB) would be downloaded.");
            if (elevMin is null && dhm.IsReady) warnings.Add("The area seems to be outside Switzerland; no elevation data is available there.");
            if (verticalScale is { } vsv && vsv < 1.0 / req.MetresPerBlock - 1e-9) warnings.Add($"Relief is squeezed vertically (scale {vsv:0.###}) to fit the world height." +
                (trueProportionMpb is { } tpm ? $" True proportions need at least {tpm} m per block." : ""));

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
                Warnings = warnings,
                Ok = req.TotalBlocks <= WorldGenerator.MaxBlocks && area.Width > 0 && area.Height > 0,
            });
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
