using System.Text.Json.Serialization;
using XUnityToolkit_WebUI.Endpoints;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Infrastructure
builder.Services.AddSingleton<AppDataPaths>();

// JSON serialization: enums as strings for API responses
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// HTTP client for GitHub API
builder.Services.AddHttpClient("GitHub", client =>
{
    client.BaseAddress = new Uri("https://api.github.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});

// Services
builder.Services.AddSingleton<GameLibraryService>();
builder.Services.AddSingleton<UnityDetectionService>();
builder.Services.AddSingleton<GitHubReleaseService>();
builder.Services.AddSingleton<BepInExInstallerService>();
builder.Services.AddSingleton<XUnityInstallerService>();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<InstallOrchestrator>();

// SignalR with string enum serialization
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var app = builder.Build();

// Ensure app data directories exist
app.Services.GetRequiredService<AppDataPaths>().EnsureDirectoriesExist();

// Static files (Vue SPA)
app.UseDefaultFiles();
app.UseStaticFiles();

// API endpoints
app.MapGameEndpoints();
app.MapDetectionEndpoints();
app.MapInstallEndpoints();
app.MapConfigEndpoints();
app.MapReleaseEndpoints();
app.MapDialogEndpoints();

// SignalR hub
app.MapHub<InstallProgressHub>("/hubs/install");

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();
