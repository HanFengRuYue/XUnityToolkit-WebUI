using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Console;
using XUnityToolkit_WebUI.Endpoints;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

// 控制台 UTF-8 编码，必须在任何输出之前设置
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// 从 settings.json 读取端口，如果文件不存在或端口无效则使用默认值
var appDataRoot = AppContext.BaseDirectory;
var settingsPath = Path.Combine(appDataRoot, "settings.json");
var listenPort = 51821;
if (File.Exists(settingsPath))
{
    try
    {
        var settingsJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (settingsJson.RootElement.TryGetProperty("aiTranslation", out var aiSection) &&
            aiSection.TryGetProperty("port", out var portProp) &&
            portProp.TryGetInt32(out var configuredPort) &&
            configuredPort is > 0 and < 65536)
        {
            listenPort = configuredPort;
        }
    }
    catch
    {
        // Ignore parse errors, use default port
    }
}
builder.WebHost.UseUrls($"http://127.0.0.1:{listenPort}");

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

// 文件日志：写入程序目录/logs/app.log
var logFilePath = Path.Combine(appDataRoot, "logs", "app.log");
var fileLoggerProvider = new XUnityToolkit_WebUI.Infrastructure.FileLoggerProvider(logFilePath);
builder.Logging.AddProvider(fileLoggerProvider);
builder.Services.AddSingleton<FileLoggerProvider>(_ => fileLoggerProvider);

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

// HTTP client for LLM API calls — allow high concurrent connections for parallel translation
builder.Services.AddHttpClient("LLM", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromSeconds(120);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    MaxConnectionsPerServer = 200,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
});

// HTTP client for SteamGridDB API
builder.Services.AddHttpClient("SteamGridDB", client =>
{
    client.BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/");
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Services
builder.Services.AddSingleton<GameImageService>();
builder.Services.AddSingleton<GameLibraryService>();
builder.Services.AddSingleton<UnityDetectionService>();
builder.Services.AddSingleton<PluginDetectionService>();
builder.Services.AddSingleton<GitHubReleaseService>();
builder.Services.AddSingleton<BepInExInstallerService>();
builder.Services.AddSingleton<XUnityInstallerService>();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<InstallOrchestrator>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<GlossaryService>();
builder.Services.AddSingleton<LlmTranslationService>();
builder.Services.AddSingleton<GlossaryExtractionService>();
builder.Services.AddSingleton<AssetExtractionService>();
builder.Services.AddSingleton<PreTranslationService>();
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

// Log incoming API requests for diagnostics (especially /api/translate from game plugin)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/translate") && context.Request.Method == "POST")
    {
        var apiLogger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("XUnityToolkit_WebUI.TranslateMiddleware");
        apiLogger.LogInformation("收到翻译请求: {Remote} → {Path}",
            context.Connection.RemoteIpAddress, context.Request.Path);
    }
    await next(context);
});

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
app.MapTranslateEndpoints();
app.MapImageEndpoints();
app.MapLogEndpoints();
app.MapAssetEndpoints();

// SignalR hub
app.MapHub<InstallProgressHub>("/hubs/install");

// SPA fallback
app.MapFallbackToFile("index.html");

// Wire log broadcast: push new log entries to SignalR "logs" group
var hubContext = app.Services.GetRequiredService<IHubContext<InstallProgressHub>>();
fileLoggerProvider.LogBroadcast = entry =>
    _ = hubContext.Clients.Group("logs").SendAsync("logEntry", entry);

// Initialize AI translation enabled state from settings
try
{
    var settingsService = app.Services.GetRequiredService<AppSettingsService>();
    var settings = settingsService.GetAsync().GetAwaiter().GetResult();
    app.Services.GetRequiredService<LlmTranslationService>().Enabled = settings.AiTranslation.Enabled;
}
catch
{
    // Ignore — defaults to enabled
}

app.Run();
