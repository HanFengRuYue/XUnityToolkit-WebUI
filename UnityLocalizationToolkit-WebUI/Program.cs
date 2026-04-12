using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Console;
using UnityLocalizationToolkit_WebUI.Endpoints;
using UnityLocalizationToolkit_WebUI.Hubs;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Services;

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

var appBaseDirectory = AppContext.BaseDirectory;
var webRootPath = Path.Combine(appBaseDirectory, "wwwroot");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = appBaseDirectory,
    WebRootPath = webRootPath
});

// 从 settings.json 读取端口，如果文件不存在或端口无效则使用默认值
var appDataRoot = builder.Configuration["AppData:Root"]
    ?? Path.Combine(Environment.GetFolderPath(
           Environment.SpecialFolder.ApplicationData), "UnityLocalizationToolkit");
builder.Configuration["AppData:Root"] = appDataRoot;
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
builder.Logging.AddFilter("UnityLocalizationToolkit_WebUI", LogLevel.Debug);

// 文件日志：写入程序目录/logs/，每次启动创建新日志文件，保留最近 10 个
var logsDirectory = Path.Combine(appDataRoot, "logs");
var fileLoggerProvider = new UnityLocalizationToolkit_WebUI.Infrastructure.FileLoggerProvider(logsDirectory, LogLevel.Debug);
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
    client.DefaultRequestHeaders.Add("User-Agent", "UnityLocalizationToolkit-WebUI/1.0");
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
    client.DefaultRequestHeaders.Add("User-Agent", "UnityLocalizationToolkit-WebUI/1.0");
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

builder.Services.AddHttpClient("ExternalDownload", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Add("Accept", "*/*");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});

// HTTP client for local LLM model downloads — long timeout for large files
builder.Services.AddHttpClient("LocalLlmDownload", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "UnityLocalizationToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromHours(12);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});

// HTTP client for GitHub update checks
builder.Services.AddHttpClient("GitHubUpdate", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "UnityLocalizationToolkit-WebUI");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
});

// HTTP client for GitHub CDN/web requests (not API — no rate limit)
builder.Services.AddHttpClient("GitHubCdn", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "UnityLocalizationToolkit-WebUI");
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
builder.Services.AddSingleton<TermService>();
builder.Services.AddSingleton<TermMatchingService>();
builder.Services.AddSingleton<TermAuditService>();
builder.Services.AddSingleton<ScriptTagService>();
builder.Services.AddSingleton<LlmTranslationService>();
builder.Services.AddSingleton<GlossaryExtractionService>();
builder.Services.AddSingleton<AssetExtractionService>();
builder.Services.AddSingleton<UnityAssetCatalogService>();
builder.Services.AddSingleton<AddressablesCatalogService>();
builder.Services.AddSingleton<UnityAssetWriteService>();
builder.Services.AddSingleton<ManualTranslationService>();
builder.Services.AddSingleton<PreTranslationService>();
builder.Services.AddSingleton<PreTranslationCacheMonitor>();
builder.Services.AddSingleton<TranslationMemoryService>();
builder.Services.AddSingleton<DynamicPatternService>();
builder.Services.AddSingleton<TermExtractionService>();
builder.Services.AddSingleton<PluginPackageService>();
builder.Services.AddSingleton<BepInExPluginService>();
builder.Services.AddSingleton<FontReplacementService>();
builder.Services.AddSingleton<TmpFontGeneratorService>();
builder.Services.AddSingleton<CharacterSetService>();
builder.Services.AddSingleton<BepInExLogService>();
builder.Services.AddSingleton<PluginHealthCheckService>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<SystemTrayService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemTrayService>());

// SignalR with string enum serialization
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 缩短关闭超时，避免浏览器 WebSocket 连接导致退出延迟
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(1);
});

var app = builder.Build();

// Ensure app data directories exist
var appDataPaths = app.Services.GetRequiredService<AppDataPaths>();
appDataPaths.EnsureDirectoriesExist();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("UnityLocalizationToolkit_WebUI.Startup");
var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
var indexExists = File.Exists(indexPath);
startupLogger.LogInformation(
    "Startup paths: CurrentDirectory={CurrentDirectory}, BaseDirectory={BaseDirectory}, ContentRoot={ContentRoot}, WebRoot={WebRoot}, IndexHtml={IndexHtml}, IndexExists={IndexExists}",
    Environment.CurrentDirectory,
    appBaseDirectory,
    app.Environment.ContentRootPath,
    app.Environment.WebRootPath,
    indexPath,
    indexExists);
if (!indexExists)
{
    startupLogger.LogCritical(
        "前端入口文件缺失: {IndexHtml}。程序将无法提供首页，请检查安装目录中的 wwwroot 目录。",
        indexPath);
}

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

// Global exception handler — prevent stack traces and internal details from leaking to clients
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api"))
    {
        var exLogger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnityLocalizationToolkit_WebUI.ExceptionHandler");
        exLogger.LogError(ex, "未处理的异常: {Method} {Path}", context.Request.Method, context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { success = false, message = "服务器内部错误" });
        }
    }
});

// Log incoming API requests for diagnostics (especially /api/translate from game plugin)
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/translate") && context.Request.Method == "POST")
    {
        var apiLogger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnityLocalizationToolkit_WebUI.TranslateMiddleware");
        apiLogger.LogInformation("收到翻译请求: {Remote} → {Path}",
            context.Connection.RemoteIpAddress, context.Request.Path);
    }
    await next(context);
});

// Static files (Vue SPA)
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
        {
            // 带内容哈希的文件（Vite 构建产物）：长期缓存
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else
        {
            // index.html、favicon.ico 等无哈希文件：每次验证
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

// API endpoints
app.MapGameEndpoints();
app.MapDetectionEndpoints();
app.MapInstallEndpoints();
app.MapConfigEndpoints();
app.MapFileExplorerEndpoints();
app.MapSettingsEndpoints();
app.MapTranslateEndpoints();
app.MapImageEndpoints();
app.MapLogEndpoints();
app.MapAssetEndpoints();
app.MapManualTranslationEndpoints();
app.MapScriptTagEndpoints();
app.MapTranslationEditorEndpoints();
app.MapPluginPackageEndpoints();
app.MapBepInExPluginEndpoints();
app.MapFontReplacementEndpoints();
app.MapFontGenerationEndpoints();
app.MapLocalLlmEndpoints();
app.MapBepInExLogEndpoints();
app.MapPluginHealthEndpoints();
app.MapUpdateEndpoints();
app.MapTranslationMemoryEndpoints();

// SignalR hub
app.MapHub<InstallProgressHub>("/hubs/install");

// SPA fallback — index.html 必须每次验证，防止浏览器缓存旧版本
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});

// Wire log broadcast: push new log entries to SignalR "logs" group
var hubContext = app.Services.GetRequiredService<IHubContext<InstallProgressHub>>();
fileLoggerProvider.LogBroadcast = entry =>
    _ = hubContext.Clients.Group("logs").SendAsync("logEntry", entry);

// Shutdown: hide UI immediately + flush TM with timeout — user perceives "app closed" before cleanup.
// The tray exit handler also calls HideUICore() directly for instant visual feedback.
app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<SystemTrayService>().HideUIImmediately();
    app.Services.GetRequiredService<TranslationMemoryService>().FlushAllDirtyWithTimeout(TimeSpan.FromSeconds(3));
});

// Deferred initialization after Kestrel is ready — avoids blocking startup with disk I/O + DPAPI
app.Lifetime.ApplicationStarted.Register(() =>
{
    // Initialize AI translation enabled state + cleanup stale local endpoint
    _ = Task.Run(async () =>
    {
        try
        {
            var settingsService = app.Services.GetRequiredService<AppSettingsService>();
            var settings = await settingsService.GetAsync();
            app.Services.GetRequiredService<LlmTranslationService>().Enabled = settings.AiTranslation.Enabled;

            // Local LLM is never running on fresh startup — disable any stale local endpoint
            var localEndpoint = settings.AiTranslation.Endpoints.FirstOrDefault(e => e.ApiKey == "local");
            if (localEndpoint is { Enabled: true })
            {
                await settingsService.UpdateAsync(s =>
                {
                    var ep = s.AiTranslation.Endpoints.FirstOrDefault(e => e.ApiKey == "local");
                    if (ep is not null) ep.Enabled = false;
                });
            }
        }
        catch
        {
            // Ignore — defaults to enabled
        }
    });

    // Auto-check for updates
    _ = Task.Run(async () =>
    {
        var updateService = app.Services.GetRequiredService<UpdateService>();
        await updateService.AutoCheckOnStartupAsync();
    });
});

app.Run();
