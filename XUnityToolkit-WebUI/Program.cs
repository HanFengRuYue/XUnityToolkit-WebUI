using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Console;
using XUnityToolkit_WebUI.Endpoints;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

// 控制台 UTF-8 编码 — WinExe 模式下无控制台，安全跳过
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch
{
    // WinExe subsystem: no console allocated — encoding is irrelevant
}

var builder = WebApplication.CreateBuilder(args);

// 从 settings.json 读取端口，如果文件不存在或端口无效则使用默认值
var appDataRoot = builder.Configuration["AppData:Root"]
    ?? Path.Combine(AppContext.BaseDirectory, "data");
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

// 文件日志：写入程序目录/logs/，每次启动创建新日志文件，保留最近 10 个
var logsDirectory = Path.Combine(appDataRoot, "logs");
var fileLoggerProvider = new XUnityToolkit_WebUI.Infrastructure.FileLoggerProvider(logsDirectory);
builder.Logging.AddProvider(fileLoggerProvider);
builder.Services.AddSingleton<FileLoggerProvider>(_ => fileLoggerProvider);

// Infrastructure
builder.Services.AddSingleton<AppDataPaths>();
builder.Services.AddSingleton<BundledAssetPaths>();

// JSON serialization: enums as strings for API responses
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

// HTTP client for web image scraping (Bing/Google)
builder.Services.AddHttpClient("WebImageSearch", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// HTTP client for local LLM model downloads — long timeout for large files
builder.Services.AddHttpClient("LocalLlmDownload", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromHours(12);
});

// Services
builder.Services.AddSingleton<LocalLlmService>();
builder.Services.AddSingleton<GameImageService>();
builder.Services.AddSingleton<WebImageSearchService>();
builder.Services.AddSingleton<GameLibraryService>();
builder.Services.AddSingleton<UnityDetectionService>();
builder.Services.AddSingleton<PluginDetectionService>();
builder.Services.AddSingleton<BepInExInstallerService>();
builder.Services.AddSingleton<XUnityInstallerService>();
builder.Services.AddSingleton<TmpFontService>();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<InstallOrchestrator>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<GlossaryService>();
builder.Services.AddSingleton<DoNotTranslateService>();
builder.Services.AddSingleton<LlmTranslationService>();
builder.Services.AddSingleton<GlossaryExtractionService>();
builder.Services.AddSingleton<AssetExtractionService>();
builder.Services.AddSingleton<PreTranslationService>();
builder.Services.AddSingleton<PluginPackageService>();
builder.Services.AddSingleton<FontReplacementService>();
builder.Services.AddSingleton<TmpFontGeneratorService>();
builder.Services.AddSingleton<SystemTrayService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemTrayService>());

// SignalR with string enum serialization
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var app = builder.Build();

// Ensure app data directories exist
var appDataPaths = app.Services.GetRequiredService<AppDataPaths>();
appDataPaths.EnsureDirectoriesExist();

// Clean up orphaned font generation temp directories
try
{
    if (Directory.Exists(appDataPaths.FontGenerationTempDirectory))
    {
        foreach (var dir in Directory.GetDirectories(appDataPaths.FontGenerationTempDirectory))
        {
            try { Directory.Delete(dir, true); }
            catch { /* ignore cleanup failures */ }
        }
    }
}
catch { /* ignore */ }

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
app.MapDialogEndpoints();
app.MapSettingsEndpoints();
app.MapTranslateEndpoints();
app.MapImageEndpoints();
app.MapLogEndpoints();
app.MapAssetEndpoints();
app.MapTranslationEditorEndpoints();
app.MapPluginPackageEndpoints();
app.MapFontReplacementEndpoints();
app.MapFontGenerationEndpoints();
app.MapLocalLlmEndpoints();

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
