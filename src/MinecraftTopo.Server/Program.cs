using System.Diagnostics;
using System.Text.Json.Serialization;
using MinecraftTopo.Core;
using MinecraftTopo.Core.Elevation;
using MinecraftTopo.Core.Terrain;
using MinecraftTopo.Server.Endpoints;
using MinecraftTopo.Server.Jobs;

// Messages and numbers are formatted the same way regardless of the machine's locale.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

// Serve the built Angular app when it exists (client/dist/client/browser relative to the repo).
// The web root must be known before the builder is created.
string? clientDist = ResolveClientDist(Environment.GetEnvironmentVariable("MINECRAFTTOPO_CLIENTDIST"), Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = clientDist,
});

string urls = builder.Configuration["Urls"] ?? "http://localhost:5100";
builder.WebHost.UseUrls(urls);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

builder.Services.AddSingleton(_ =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MinecraftTopo/1.0");
    return http;
});
builder.Services.AddSingleton<AppPaths>(_ => new AppPaths(builder.Configuration["DataDir"]));
builder.Services.AddSingleton<Downloader>();
builder.Services.AddSingleton<Dhm200Model>();
builder.Services.AddSingleton<MinecraftTopo.Core.Tlm.TlmDatasets>();
builder.Services.AddSingleton<MinecraftTopo.Core.Tlm.RegionLookup>();
builder.Services.AddSingleton<OverviewTileRenderer>();
builder.Services.AddSingleton<WorldGenerator>(sp =>
{
    var downloader = sp.GetRequiredService<Downloader>();
    var paths = sp.GetRequiredService<AppPaths>();
    var dhm = sp.GetRequiredService<Dhm200Model>();
    var tlm = sp.GetRequiredService<MinecraftTopo.Core.Tlm.TlmDatasets>();
    return new WorldGenerator(
        req => req.ResolveSource() switch
        {
            ElevationSourceKind.Synthetic => new SyntheticSource(),
            ElevationSourceKind.Dhm200 => new Dhm200Source(dhm),
            _ => new SwissAlti3dSource(downloader, paths, req.Alti3dResolution),
        },
        req => req.ResolveSource() == ElevationSourceKind.Synthetic ? null
            : req.LandCover == LandCoverKind.Tlm3d ? new MinecraftTopo.Core.Tlm.TlmLandCoverSource(tlm, MinecraftTopo.Core.Tlm.TlmKind.Tlm3d)
            : req.LandCover == LandCoverKind.TlmRegio ? new MinecraftTopo.Core.Tlm.TlmLandCoverSource(tlm, MinecraftTopo.Core.Tlm.TlmKind.Regio)
            : new MinecraftTopo.Core.Water.Vec25LandCoverSource(downloader, paths),
        req => new MinecraftTopo.Core.Buildings.SwissBuildings3dSource(downloader, paths),
        req => new MinecraftTopo.Core.Geology.Gk500Source(downloader, paths));
});
builder.Services.AddSingleton<JobManager>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapApi();

if (clientDist is not null)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
    app.Logger.LogInformation("Serving client from {Dir}", clientDist);
}
else
{
    app.MapGet("/", () => Results.Text(
        "MinecraftTopo API is running. The Angular client build was not found; run `ng build` in the client folder or use `ng serve`.",
        "text/plain"));
    app.Logger.LogWarning("Client build not found; only the API is served");
}

// Warm the country model in the background so the overview appears quickly.
var dhmModel = app.Services.GetRequiredService<Dhm200Model>();
_ = Task.Run(async () =>
{
    try { await dhmModel.GetAsync(null, CancellationToken.None); }
    catch (Exception ex) { app.Logger.LogError(ex, "Loading DHM200 failed"); }
});

if (clientDist is not null && app.Configuration.GetValue("OpenBrowser", false) && OperatingSystem.IsWindows())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try { Process.Start(new ProcessStartInfo(urls.Split(';')[0]) { UseShellExecute = true }); }
        catch (Exception ex) { app.Logger.LogWarning(ex, "Could not open the browser"); }
    });
}

app.Run();

static string? ResolveClientDist(string? configured, string cwd)
{
    var candidates = new List<string>();
    if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(Path.GetFullPath(configured, cwd));
    candidates.Add(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
    // Walk up from the working directory looking for client/dist/client/browser (repo layout).
    string? dir = cwd;
    for (int i = 0; i < 4 && dir is not null; i++)
    {
        candidates.Add(Path.Combine(dir, "client", "dist", "client", "browser"));
        dir = Path.GetDirectoryName(dir);
    }
    return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "index.html")));
}
