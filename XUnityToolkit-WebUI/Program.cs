using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Console;
using XUnityToolkit_WebUI.Endpoints;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

// 控制台 UTF-8 编码，必须在任何输出之前设置
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// 控制台日志：仅显示自身服务日志 + 启动信息，过滤框架噪音
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
    options.ColorBehavior = LoggerColorBehavior.Enabled;
    options.SingleLine = true;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
builder.Logging.AddFilter("XUnityToolkit_WebUI", LogLevel.Information);

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

// HTTP client for mirror downloads (no GitHub API headers)
builder.Services.AddHttpClient("Mirror", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Services
builder.Services.AddSingleton<GameLibraryService>();
builder.Services.AddSingleton<UnityDetectionService>();
builder.Services.AddSingleton<GitHubReleaseService>();
builder.Services.AddSingleton<BepInExInstallerService>();
builder.Services.AddSingleton<XUnityInstallerService>();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<InstallOrchestrator>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddHostedService<SystemTrayService>();

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
app.MapCacheEndpoints();
app.MapSettingsEndpoints();

// SignalR hub
app.MapHub<InstallProgressHub>("/hubs/install");

// SPA fallback
app.MapFallbackToFile("index.html");

app.Run();
