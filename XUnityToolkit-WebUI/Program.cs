using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging.Console;
using Microsoft.UI.Dispatching;
using XUnityToolkit_WebUI.Endpoints;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI;

public sealed class Program
{
    private static DesktopApp? _desktopApplication;

    private Program()
    {
    }

    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var headlessSmoke = args.Any(arg =>
            string.Equals(arg, "--headless-smoke", StringComparison.OrdinalIgnoreCase));
        AppBootstrap? bootstrap = null;

        try
        {
            bootstrap = BuildApplicationAsync(args, headlessSmoke).GetAwaiter().GetResult();
            if (bootstrap is null)
                return;

            if (headlessSmoke)
                bootstrap.Application.Run();
            else
                RunDesktopApplication(bootstrap.Application);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            bootstrap?.StartupLogger.LogCritical(ex, "工具箱本机服务异常停止");

            try { Console.Error.WriteLine(ex); }
            catch { /* WinExe subsystem has no console. */ }

            if (!headlessSmoke)
            {
                MessageBox.Show(
                    $"工具箱无法启动。\n\n{ex.GetBaseException().Message}",
                    "XUnity Toolkit 启动失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            if (bootstrap is not null)
                ShutdownApplication(bootstrap);
        }
    }

    private static async Task<AppBootstrap?> BuildApplicationAsync(
        string[] args,
        bool headlessSmoke)
    {

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
           Environment.SpecialFolder.ApplicationData), "XUnityToolkit");
builder.Configuration["AppData:Root"] = appDataRoot;

if (!ToolkitSingleInstance.TryAcquire(appDataRoot, out var singleInstance, out var instanceError))
{
    var discoveryFile = Path.Combine(appDataRoot, "runtime", "toolbox-endpoint-v1.json");
    var activated = await ToolkitSingleInstance.TryActivateExistingAsync(
        discoveryFile, TimeSpan.FromSeconds(5));
    if (!activated)
    {
        if (!headlessSmoke)
        {
            MessageBox.Show(
                $"{instanceError}\n\n无法唤起现有实例。请等待其启动完成，或在任务管理器中结束残留进程后重试。",
                "XUnity Toolkit 已在运行",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
    return null;
}
var acquiredSingleInstance = singleInstance!;
try
{
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
            configuredPort is >= 1024 and <= 65535)
        {
            listenPort = configuredPort;
        }
    }
    catch
    {
        // Ignore parse errors, use default port
    }
}
var runtimeEndpoint = new ToolkitRuntimeEndpointState(listenPort);
builder.WebHost.ConfigureKestrel(options =>
    options.Listen(IPAddress.Loopback, listenPort));
builder.WebHost.UseSockets(options =>
    options.CreateBoundListenSocket = runtimeEndpoint.CreateBoundListenSocket);

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
builder.Logging.AddFilter("XUnityToolkit_WebUI", LogLevel.Debug);

// 文件日志：写入程序目录/logs/，每次启动创建新日志文件，保留最近 10 个
var logsDirectory = Path.Combine(appDataRoot, "logs");
var fileLoggerProvider = new XUnityToolkit_WebUI.Infrastructure.FileLoggerProvider(logsDirectory, LogLevel.Debug);
builder.Logging.AddProvider(fileLoggerProvider);
builder.Services.AddSingleton<FileLoggerProvider>(_ => fileLoggerProvider);

// Infrastructure
builder.Services.AddSingleton<AppDataPaths>();
builder.Services.AddSingleton<BundledAssetPaths>();
builder.Services.AddSingleton(runtimeEndpoint);

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
}).ConfigurePrimaryHttpMessageHandler(ToolkitHttpHandlers.CreateCloudLlmHandler);

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
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI/1.0");
    client.Timeout = TimeSpan.FromHours(12);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});

// HTTP client for GitHub update checks
builder.Services.AddHttpClient("GitHubUpdate", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
});

// HTTP client for GitHub CDN/web requests (not API — no rate limit)
builder.Services.AddHttpClient("GitHubCdn", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "XUnityToolkit-WebUI");
});

// Local protocol traffic must never inherit Windows proxy/PAC settings.
builder.Services.AddHttpClient("ToolkitLoopback", client =>
{
    client.Timeout = TimeSpan.FromSeconds(3);
}).ConfigurePrimaryHttpMessageHandler(ToolkitHttpHandlers.CreateLoopbackHandler);

// Services
builder.Services.AddSingleton<LocalLlmService>();
builder.Services.AddSingleton<GameImageService>();
builder.Services.AddSingleton<WebImageSearchService>();
builder.Services.AddSingleton<GameLibraryService>();
builder.Services.AddSingleton<UnityDetectionService>();
builder.Services.AddSingleton<PluginDetectionService>();
builder.Services.AddSingleton<BepInExInstallerService>();
builder.Services.AddSingleton<XUnityInstallerService>();
builder.Services.AddSingleton<TranslatorEndpointUpgradeService>();
builder.Services.AddSingleton<TmpFontService>();
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<ToolkitRuntimeDiscoveryService>();
builder.Services.AddSingleton<PluginConnectionRegistry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TranslationRequestCoordinator>();
builder.Services.AddSingleton<InstallOrchestrator>();
builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<TermService>();
builder.Services.AddSingleton<TermMatchingService>();
builder.Services.AddSingleton<TermAuditService>();
builder.Services.AddSingleton<ScriptTagService>();
builder.Services.AddSingleton<LlmTranslationService>();
builder.Services.AddSingleton<GlossaryExtractionService>();
builder.Services.AddSingleton<TranslationMemoryService>();
builder.Services.AddSingleton<PluginPackageService>();
builder.Services.AddSingleton<BepInExPluginService>();
builder.Services.AddSingleton<FontReplacementService>();
builder.Services.AddSingleton<TmpFontGeneratorService>();
builder.Services.AddSingleton<CharacterSetService>();
builder.Services.AddSingleton<BepInExLogService>();
builder.Services.AddSingleton<PluginDiagnosticArtifactCollector>();
builder.Services.AddSingleton<PluginDiagnosticAgentService>();
builder.Services.AddSingleton<PluginHealthCheckService>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<DesktopWindowService>();
builder.Services.AddSingleton<IDesktopWindowService>(sp =>
    sp.GetRequiredService<DesktopWindowService>());
builder.Services.AddSingleton<SystemTrayService>();
if (!headlessSmoke)
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
    .CreateLogger("XUnityToolkit_WebUI.Startup");
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
            .CreateLogger("XUnityToolkit_WebUI.ExceptionHandler");
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
            .CreateLogger("XUnityToolkit_WebUI.TranslateMiddleware");
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
    app.Services.GetRequiredService<IDesktopWindowService>().ShutdownFromHost();
    app.Services.GetRequiredService<TranslationMemoryService>().FlushAllDirtyWithTimeout(TimeSpan.FromSeconds(3));
    app.Services.GetRequiredService<ToolkitRuntimeDiscoveryService>().DeleteOwnedDiscoveryFile();
});

// Deferred initialization after Kestrel is ready — avoids blocking startup with disk I/O + DPAPI
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var discoveryService = app.Services.GetRequiredService<ToolkitRuntimeDiscoveryService>();
            await discoveryService.PublishAndVerifyAsync(app.Lifetime.ApplicationStopping);
            startupLogger.LogInformation(
                "工具箱本机端点已就绪: PreferredPort={PreferredPort}, ActualPort={ActualPort}, UsedFallback={UsedFallback}, Reason={Reason}",
                runtimeEndpoint.PreferredPort,
                runtimeEndpoint.ActualPort,
                runtimeEndpoint.UsedFallback,
                runtimeEndpoint.FallbackReason);
        }
        catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            startupLogger.LogError(ex, "发布工具箱本机端点发现信息失败");
        }
    });

    // Keep checking hash-confirmed old official endpoints. A game that was running during startup
    // is upgraded automatically after it exits; unknown/custom DLLs are never touched by this pass.
    _ = Task.Run(async () =>
    {
        var firstPass = true;
        while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                await app.Services.GetRequiredService<TranslatorEndpointUpgradeService>()
                    .UpgradeManagedGamesAsync(
                        app.Lifetime.ApplicationStopping,
                        refreshCurrentConfigurations: firstPass);
                firstPass = false;
                await Task.Delay(TimeSpan.FromSeconds(30), app.Lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                startupLogger.LogWarning(ex, "后台检查官方 AI 翻译端点升级失败");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), app.Lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    });

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

        return new AppBootstrap(app, acquiredSingleInstance, startupLogger);
}
catch
{
    acquiredSingleInstance.Dispose();
    throw;
}
    }

    private static void RunDesktopApplication(WebApplication webApplication)
    {
        var logger = webApplication.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("XUnityToolkit_WebUI.DesktopLifetime");
        logger.LogInformation("正在启动 WinUI 消息循环");
        Microsoft.UI.Xaml.Application.Start(initializationCallbackParams =>
        {
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcherQueue));
            _desktopApplication = new DesktopApp(webApplication);
        });
        // DispatcherQueueSynchronizationContext targets the WinUI loop that just ended. Leaving
        // it installed would strand async host-shutdown continuations on a dispatcher that can
        // no longer run and make the process remain alive after the tray Exit command.
        SynchronizationContext.SetSynchronizationContext(null);
        logger.LogInformation("WinUI 消息循环已退出");
    }

    private static void ShutdownApplication(AppBootstrap bootstrap)
    {
        bootstrap.StartupLogger.LogInformation("开始释放工具箱宿主资源");
        try
        {
            bootstrap.Application.Services
                .GetRequiredService<ToolkitRuntimeDiscoveryService>()
                .DeleteOwnedDiscoveryFile();
        }
        catch
        {
            // Best effort after an incomplete startup.
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            bootstrap.Application.StopAsync(timeout.Token).GetAwaiter().GetResult();
            bootstrap.StartupLogger.LogInformation("工具箱本机服务已停止");
        }
        catch (Exception ex)
        {
            bootstrap.StartupLogger.LogWarning(ex, "关闭工具箱本机服务时发生异常");
        }

        try
        {
            bootstrap.Application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            bootstrap.StartupLogger.LogInformation("工具箱宿主资源已释放");
        }
        finally
        {
            bootstrap.SingleInstance.Dispose();
        }
    }

    private sealed record AppBootstrap(
        WebApplication Application,
        ToolkitSingleInstance SingleInstance,
        ILogger StartupLogger);
}
