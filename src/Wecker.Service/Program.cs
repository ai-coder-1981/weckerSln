using Microsoft.AspNetCore.Http.Features;
using Quartz;
using Wecker.Contracts;
using Wecker.Service.Hubs;
using Wecker.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd(); // Type=notify, Logs ins Journal

const long MaxUpload = 100L * 1024 * 1024; // 100 MB pro Upload
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MaxUpload);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUpload);

// Datenordner: systemd StateDirectory (/var/lib/wecker) > Konfig "DataDir" > ./data
var dataDir = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':')[0]
              ?? builder.Configuration["DataDir"]
              ?? Path.Combine(builder.Environment.ContentRootPath, "data");
var paths = new DataPaths(dataDir);
Directory.CreateDirectory(paths.Music);

builder.Services.AddSingleton(paths);
builder.Services.Configure<PlayerOptions>(builder.Configuration.GetSection("Player"));
builder.Services.AddSingleton<MusicLibrary>();
builder.Services.AddSingleton<AudioPlayer>();
builder.Services.Configure<ShutdownOptions>(builder.Configuration.GetSection("Shutdown"));
builder.Services.AddSingleton<ShutdownService>();
builder.Services.AddSingleton<AlarmService>();

builder.Services.AddSignalR();
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

var app = builder.Build();

var api = app.MapGroup("/api");
api.MapGet("/alarms", (AlarmService s) => s.GetAlarms());
api.MapPut("/alarms/{id:int}", async (int id, AlarmDto dto, AlarmService s) =>
    await s.SaveAsync(id, dto) is { } result ? Results.Ok(result) : Results.NotFound());
api.MapPost("/alarms/{id:int}/test", async (int id, AlarmService s) =>
    await s.TestAsync(id) is { } error ? Results.BadRequest(error) : Results.NoContent());
api.MapGet("/music", (MusicLibrary m) => m.GetFiles());
api.MapPost("/music", async (IFormFile file, MusicLibrary m, AlarmService s) =>
{
    await using var stream = file.OpenReadStream();
    var (name, error) = await m.SaveAsync(file.FileName, stream);
    if (error is not null) return Results.BadRequest(error);
    await s.NotifyMusicChangedAsync();
    return Results.Ok(name);
}).DisableAntiforgery();
api.MapDelete("/music/{file}", async (string file, AlarmService s) =>
    await s.DeleteMusicAsync(file) is { } error ? Results.Conflict(error) : Results.NoContent());
api.MapGet("/status", (AlarmService s) => s.GetStatusAsync());
api.MapGet("/shutdown", (ShutdownService s) => s.Current);
api.MapPut("/shutdown", async (ShutdownDto dto, ShutdownService s) => Results.Ok(await s.SaveAsync(dto)));
api.MapPost("/shutdown/now", (ShutdownService s) => { s.ShutdownSoon(); return Results.NoContent(); });
api.MapPost("/stop", async (AlarmService s) => { await s.StopAsync(); return Results.NoContent(); });

app.MapHub<StatusHub>(ApiRoutes.StatusHub);
app.MapGet("/", () => "Wecker.Service läuft");

await app.Services.GetRequiredService<ShutdownService>().InitializeAsync();
await app.Services.GetRequiredService<AlarmService>().InitializeAsync();
app.Run();
