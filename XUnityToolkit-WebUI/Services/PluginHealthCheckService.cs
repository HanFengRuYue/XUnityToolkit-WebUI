using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class PluginHealthCheckService(
    AppSettingsService settingsService,
    LocalLlmService localLlmService,
    ILogger<PluginHealthCheckService> logger)
{
    private const int MaxLogLines = 2000;
    private const int LogWaitTimeoutSeconds = 30;
    private const int LogSettleDelaySeconds = 5;

    private readonly ConcurrentDictionary<string, bool> _activeVerifications = [];
    private readonly ConcurrentDictionary<string, DateTime> _receivedPings = [];

    /// <summary>Record a connectivity ping from LLMTranslate.dll.</summary>
    public void RecordPing(string gameId) => _receivedPings[gameId] = DateTime.UtcNow;

    /// <summary>Check if a ping was received from the game since the given time.</summary>
    public bool HasRecentPing(string gameId, DateTime since) =>
        _receivedPings.TryGetValue(gameId, out var ts) && ts >= since;

    /// <summary>
    /// Perform a passive health check by inspecting files, toolbox AI state, and parsing the BepInEx log.
    /// </summary>
    /// <param name="game">The game to check.</param>
    /// <param name="connectivityVerified">null=passive check, true=ping received, false=no ping.</param>
    public async Task<PluginHealthReport> CheckAsync(
        Game game,
        bool? connectivityVerified = null,
        CancellationToken ct = default)
    {
        var checks = new List<HealthCheckItem>();
        var gamePath = game.GamePath;
        var isIL2CPP = game.DetectedInfo?.Backend == UnityBackend.IL2CPP;
        var toolboxAiIssue = await GetToolboxAiIssueAsync(ct);

        // Tier 1: file integrity checks
        CheckDoorstopProxy(checks, gamePath, isIL2CPP);
        CheckDoorstopConfig(checks, gamePath);
        CheckBepInExCore(checks, gamePath);
        CheckXUnityPlugin(checks, gamePath);
        CheckTranslatorConfig(checks, gamePath);

        var hasEndpointDir = Directory.Exists(
            Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators"));
        if (hasEndpointDir)
            CheckTranslatorEndpoint(checks, gamePath);

        if (toolboxAiIssue is not null)
            checks.Add(CreateToolboxAiStateCheck(toolboxAiIssue));

        // Tier 2: log-based checks
        var logPath = Path.Combine(gamePath, "BepInEx", "LogOutput.log");
        DateTime? logLastModified = null;
        var gameNeverRun = !File.Exists(logPath);

        if (!gameNeverRun)
        {
            var fi = new FileInfo(logPath);
            logLastModified = fi.LastWriteTimeUtc;

            var logLines = ReadLogLines(logPath);
            if (logLines is not null)
            {
                CheckBepInExLoaded(checks, logLines);
                CheckXUnityLoaded(checks, logLines);
                if (hasEndpointDir)
                    CheckEndpointRegistered(checks, logLines);
                CheckLogErrors(checks, logLines, toolboxAiIssue);
            }
        }

        // Tier 3: connectivity check (only after verify)
        if (connectivityVerified.HasValue)
        {
            if (connectivityVerified.Value)
            {
                checks.Add(new("toolboxConnectivity", "工具箱连通性", HealthStatus.Healthy, null));
            }
            else
            {
                checks.Add(new("toolboxConnectivity", "工具箱连通性", HealthStatus.Error,
                    "插件未能连接到工具箱，请检查工具箱是否正在运行、防火墙设置和监听端口配置。"));
            }
        }

        var overall = DetermineOverall(checks, gameNeverRun);
        return new PluginHealthReport(overall, checks, logLastModified, gameNeverRun, DateTime.UtcNow);
    }

    /// <summary>
    /// Verify installation health; called by InstallOrchestrator during install.
    /// Skips the active verification guard because the orchestrator already owns concurrency control.
    /// </summary>
    public Task<PluginHealthReport> VerifyForInstallAsync(Game game, CancellationToken ct) =>
        VerifyCore(game, ct);

    /// <summary>
    /// Verify installation by launching the game briefly to generate logs, then analyze.
    /// </summary>
    public async Task<PluginHealthReport> VerifyAsync(Game game, CancellationToken ct)
    {
        if (!_activeVerifications.TryAdd(game.Id, true))
            throw new InvalidOperationException("该游戏正在验证安装状态中，请等待当前验证完成。");

        try
        {
            return await VerifyCore(game, ct);
        }
        finally
        {
            _activeVerifications.TryRemove(game.Id, out _);
        }
    }

    private async Task<PluginHealthReport> VerifyCore(Game game, CancellationToken ct)
    {
        var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable
            ?? throw new InvalidOperationException("无法确定游戏可执行文件路径。");
        var exePath = Path.GetFullPath(Path.Combine(game.GamePath, exeName));
        var normalizedGamePath = Path.GetFullPath(game.GamePath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!exePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("可执行文件路径不在游戏目录内。");

        var logPath = Path.Combine(game.GamePath, "BepInEx", "LogOutput.log");
        var verifyStartTime = DateTime.UtcNow;

        // Delete old log to get a fresh one.
        if (File.Exists(logPath))
        {
            try
            {
                File.Delete(logPath);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "无法删除旧日志文件，游戏可能仍在运行");
            }
        }

        Process? gameProcess = null;
        try
        {
            logger.LogInformation("启动游戏验证安装: {ExePath}", exePath);
            gameProcess = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = game.GamePath,
                UseShellExecute = true
            });

            var elapsed = 0;
            while (!File.Exists(logPath) && elapsed < LogWaitTimeoutSeconds)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1000, ct);
                elapsed++;
            }

            if (File.Exists(logPath))
            {
                logger.LogInformation("日志文件已生成，等待插件加载完成...");
                await Task.Delay(LogSettleDelaySeconds * 1000, ct);
            }
            else
            {
                logger.LogWarning("等待日志文件超时 ({Timeout}s)", LogWaitTimeoutSeconds);
            }
        }
        finally
        {
            // UseShellExecute=true may return a launcher process (for example Steam),
            // so kill by process name as a fallback.
            await GameProcessHelper.KillGameProcessAsync(gameProcess, exeName, game.GamePath, logger);
        }

        var pingReceived = HasRecentPing(game.Id, verifyStartTime);
        logger.LogInformation("连通性检测结果: {Result}, 游戏 {GameId}",
            pingReceived ? "已收到 ping" : "未收到 ping", game.Id);

        return await CheckAsync(game, connectivityVerified: pingReceived, ct);
    }

    private sealed record ToolboxAiIssue(string Category, string Detail, string Suggestion);

    private async Task<ToolboxAiIssue?> GetToolboxAiIssueAsync(CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var ai = settings.AiTranslation;

        if (!ai.Enabled)
        {
            return new ToolboxAiIssue(
                "工具箱 AI 翻译不可用",
                "AI 翻译总开关已关闭，请先在 AI 翻译页面重新启用。",
                "请在工具箱的 AI 翻译页面重新启用 AI 翻译总开关。");
        }

        var isLocalMode = string.Equals(ai.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
        if (isLocalMode && !localLlmService.IsRunning)
        {
            return new ToolboxAiIssue(
                "工具箱本地 AI 未启动",
                "当前使用本地模式，但本地模型未启动，请先在本地 AI 页面启动模型。",
                "请在工具箱的本地 AI 页面先启动本地模型，再重新验证插件状态。");
        }

        var hasUsableEndpoint = ai.Endpoints.Any(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey));
        if (!hasUsableEndpoint)
        {
            return new ToolboxAiIssue(
                "工具箱 AI 端点未配置",
                "当前没有可用的 AI 提供商，请在 AI 翻译页面至少配置一个启用中的端点。",
                "请在工具箱的 AI 翻译页面至少配置一个已启用且带有 API Key 的端点。");
        }

        return null;
    }

    private static HealthCheckItem CreateToolboxAiStateCheck(ToolboxAiIssue issue) =>
        new("toolboxAiState", "工具箱 AI 翻译", HealthStatus.Warning, issue.Detail);

    // Tier 1: file integrity checks

    private static void CheckDoorstopProxy(List<HealthCheckItem> checks, string gamePath, bool isIL2CPP)
    {
        var winhttpExists = File.Exists(Path.Combine(gamePath, "winhttp.dll"));
        var dobbyExists = File.Exists(Path.Combine(gamePath, "dobby.dll"));

        if (isIL2CPP)
        {
            if (winhttpExists && dobbyExists)
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Healthy, null));
            }
            else if (!winhttpExists && !dobbyExists)
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 和 dobby.dll 都不存在，可能被安全软件删除，请将游戏目录加入白名单后重新安装。"));
            }
            else if (!winhttpExists)
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 不存在，可能被安全软件删除，请将游戏目录加入白名单后重新安装。"));
            }
            else
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "dobby.dll 不存在，BepInEx 6 IL2CPP 模式需要该文件，请重新安装。"));
            }
        }
        else
        {
            if (winhttpExists)
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Healthy, null));
            }
            else
            {
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 不存在，可能被安全软件删除，请将游戏目录加入白名单后重新安装。"));
            }
        }
    }

    private static void CheckDoorstopConfig(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "doorstop_config.ini");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Healthy, null));
        }
        else if (File.Exists(path))
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Warning,
                "doorstop_config.ini 文件为空，BepInEx 可能无法正常加载。"));
        }
        else
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Error,
                "doorstop_config.ini 不存在，BepInEx 无法启动，请重新安装。"));
        }
    }

    private static void CheckBepInExCore(List<HealthCheckItem> checks, string gamePath)
    {
        var coreDir = Path.Combine(gamePath, "BepInEx", "core");
        var hasBepInEx5 = File.Exists(Path.Combine(coreDir, "BepInEx.dll"));
        var hasBepInEx6 = File.Exists(Path.Combine(coreDir, "BepInEx.Core.dll"));

        if (hasBepInEx5 || hasBepInEx6)
        {
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Error,
                "BepInEx 核心 DLL 不存在，框架无法运行，请重新安装。"));
        }
    }

    private static void CheckXUnityPlugin(List<HealthCheckItem> checks, string gamePath)
    {
        var pluginDir = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator");
        if (!Directory.Exists(pluginDir))
        {
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Error,
                "XUnity.AutoTranslator 插件目录不存在，请重新安装。"));
            return;
        }

        var hasDll = Directory.GetFiles(pluginDir, "XUnity.AutoTranslator*.dll").Length > 0;
        if (hasDll)
        {
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Error,
                "XUnity.AutoTranslator DLL 不存在，请重新安装。"));
        }
    }

    private static void CheckTranslatorConfig(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Healthy, null));
        }
        else if (File.Exists(path))
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Warning,
                "AutoTranslatorConfig.ini 文件为空，翻译功能可能不正常。"));
        }
        else
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Warning,
                "AutoTranslatorConfig.ini 不存在，需要先运行一次游戏生成配置文件。"));
        }
    }

    private static void CheckTranslatorEndpoint(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators", "LLMTranslate.dll");
        if (File.Exists(path))
        {
            checks.Add(new("translatorEndpoint", "AI 翻译端点", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("translatorEndpoint", "AI 翻译端点", HealthStatus.Error,
                "LLMTranslate.dll 不存在，AI 翻译功能不可用。可在游戏详情页重新安装。"));
        }
    }

    // Tier 2: log-based checks

    private string[]? ReadLogLines(string logPath)
    {
        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var content = reader.ReadToEnd();
            var lines = content.Split('\n');
            return lines.Length > MaxLogLines ? lines[^MaxLogLines..] : lines;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取 BepInEx 日志文件失败");
            return null;
        }
    }

    private static void CheckBepInExLoaded(List<HealthCheckItem> checks, string[] lines)
    {
        var searchLines = lines.Length > 50 ? lines[..50] : lines;
        var found = searchLines.Any(l => BepInExInitRegex().IsMatch(l));

        if (found)
        {
            checks.Add(new("bepinexLoaded", "BepInEx 框架初始化", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("bepinexLoaded", "BepInEx 框架初始化", HealthStatus.Error,
                "日志中未找到 BepInEx 初始化标记，框架可能未成功加载。请检查安全软件是否拦截了 winhttp.dll。"));
        }
    }

    private static void CheckXUnityLoaded(List<HealthCheckItem> checks, string[] lines)
    {
        var found = lines.Any(l => XUnityLoadedRegex().IsMatch(l));

        if (found)
        {
            checks.Add(new("xunityLoaded", "XUnity 插件加载", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("xunityLoaded", "XUnity 插件加载", HealthStatus.Error,
                "日志中未找到 XUnity.AutoTranslator 加载记录，插件可能未正常加载。"));
        }
    }

    private static void CheckEndpointRegistered(List<HealthCheckItem> checks, string[] lines)
    {
        var found = lines.Any(l => l.Contains("LLMTranslate", StringComparison.OrdinalIgnoreCase));

        if (found)
        {
            checks.Add(new("endpointRegistered", "翻译端点注册", HealthStatus.Healthy, null));
        }
        else
        {
            checks.Add(new("endpointRegistered", "翻译端点注册", HealthStatus.Warning,
                "日志中未找到 LLMTranslate 端点注册记录，AI 翻译端点可能未加载。"));
        }
    }

    private static void CheckLogErrors(
        List<HealthCheckItem> checks,
        string[] lines,
        ToolboxAiIssue? toolboxAiIssue)
    {
        var details = new List<HealthCheckDetail>();
        var seenCategories = new Dictionary<string, int>();

        var fontFallbackUnsupported = lines.Any(l => FontFallbackNotSupportedRegex().IsMatch(l));

        var errorCount = 0;
        foreach (var line in lines)
        {
            if (!PluginErrorRegex().IsMatch(line))
                continue;

            if (fontFallbackUnsupported && FontAssetMissingRegex().IsMatch(line))
                continue;

            errorCount++;

            if (toolboxAiIssue is not null &&
                MatchToolboxAiTranslationFailure(line, toolboxAiIssue, details, seenCategories))
            {
                continue;
            }

            if (MatchErrorPatterns(line, GeneralErrorPatterns, details, seenCategories))
                continue;

            if (toolboxAiIssue is null)
                _ = MatchErrorPatterns(line, XUnityErrorPatterns, details, seenCategories);
        }

        foreach (var line in lines)
        {
            if (!LlmTranslateLineRegex().IsMatch(line))
                continue;

            _ = MatchErrorPatterns(line, EndpointErrorPatterns, details, seenCategories);
        }

        if (details.Count > MaxTotalDetails)
            details = details[..MaxTotalDetails];

        var hasDetails = details.Count > 0;
        var detailList = hasDetails ? details : null;

        if (errorCount == 0 && !hasDetails)
        {
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Healthy, null));
        }
        else if (errorCount == 0 && hasDetails)
        {
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Warning,
                $"发现 {details.Count} 条翻译端点相关问题", detailList));
        }
        else if (errorCount < 5)
        {
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Warning,
                $"发现 {errorCount} 条插件相关错误日志", detailList));
        }
        else
        {
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Error,
                $"发现 {errorCount} 条插件相关错误日志，插件可能存在严重问题", detailList));
        }
    }

    private static bool MatchToolboxAiTranslationFailure(
        string line,
        ToolboxAiIssue toolboxAiIssue,
        List<HealthCheckDetail> details,
        Dictionary<string, int> seenCategories)
    {
        if (!XUnityTranslationFailureRegex().IsMatch(line))
            return false;

        seenCategories.TryGetValue(toolboxAiIssue.Category, out var count);
        if (count >= MaxDetailsPerCategory)
            return true;

        details.Add(new(toolboxAiIssue.Category, SafeExcerpt(line), toolboxAiIssue.Suggestion));
        seenCategories[toolboxAiIssue.Category] = count + 1;
        return true;
    }

    private static bool MatchErrorPatterns(
        string line,
        IReadOnlyList<ErrorPattern> patterns,
        List<HealthCheckDetail> details,
        Dictionary<string, int> seenCategories)
    {
        foreach (var pattern in patterns)
        {
            if (!pattern.Matcher.IsMatch(line))
                continue;

            seenCategories.TryGetValue(pattern.Category, out var count);
            if (count < MaxDetailsPerCategory)
            {
                details.Add(new(pattern.Category, SafeExcerpt(line), pattern.Suggestion));
                seenCategories[pattern.Category] = count + 1;
            }

            return true;
        }

        return false;
    }

    // Overall status

    private static HealthStatus DetermineOverall(List<HealthCheckItem> checks, bool gameNeverRun)
    {
        if (gameNeverRun && checks.All(c => c.Status == HealthStatus.Healthy))
            return HealthStatus.Unknown;

        if (checks.Any(c => c.Status == HealthStatus.Error))
            return HealthStatus.Error;
        if (checks.Any(c => c.Status == HealthStatus.Warning))
            return HealthStatus.Warning;
        return HealthStatus.Healthy;
    }

    // Error pattern matching

    private const int MaxDetailsPerCategory = 2;
    private const int MaxTotalDetails = 10;

    private record ErrorPattern(string Category, Regex Matcher, string? Suggestion);

    private static readonly IReadOnlyList<ErrorPattern> GeneralErrorPatterns =
    [
        new("IL2CPP 兼容性错误", Il2CppErrorRegex(),
            "请检查当前 BepInEx 6 版本是否支持该游戏使用的 IL2CPP 版本。"),
        new("Harmony 补丁失败", HarmonyPatchRegex(),
            "可能存在插件冲突，请尝试移除其他 BepInEx 插件后重试。"),
        new("程序集版本冲突", AssemblyVersionRegex(),
            "游戏更新可能导致 DLL 版本不匹配，请尝试重新安装。"),
        new("类型/方法加载错误", TypeLoadRegex(),
            "通常由游戏更新引起，请尝试更新 XUnity.AutoTranslator 或重新安装插件。"),
        new("插件加载失败", PluginLoadRegex(),
            "请检查插件 DLL 是否完整，并尝试重新安装。"),
        new("字体资源缺失", FontAssetMissingRegex(),
            "可在游戏详情页的 TMP 字体功能中重新安装所需字体资源。"),
        new("文件/资源未找到", FileNotFoundRegex(),
            "请检查安全软件是否删除了插件相关文件。"),
        new("访问权限错误", AccessDeniedRegex(),
            "请尝试以管理员权限运行工具箱，或将游戏目录加入安全软件白名单。"),
        new("空引用异常", NullReferenceRegex(), null),
        new("网络连接错误", NetworkErrorRegex(),
            "请检查防火墙设置，或确认 AI 翻译页面中的监听端口和网络配置是否正确。"),
    ];

    private static readonly IReadOnlyList<ErrorPattern> XUnityErrorPatterns =
    [
        new("XUnity 兼容性异常", XUnityCompatibilityRegex(),
            "当前游戏版本可能不完全兼容 XUnity。"),
        new("XUnity 翻译失败", XUnityTranslationFailureRegex(),
            "XUnity 已发起翻译，但翻译流程失败。请检查工具箱 AI 翻译配置、端点状态和相关日志。"),
    ];

    private static readonly IReadOnlyList<ErrorPattern> EndpointErrorPatterns =
    [
        new("工具箱连接失败", EndpointConnectFailRegex(),
            "请确认工具箱已启动且监听端口正确（默认 51821）。"),
        new("翻译返回空响应", EndpointEmptyResponseRegex(),
            "请检查 AI 翻译设置中的 API Key 或模型配置是否有效。"),
        new("翻译数量不匹配", EndpointCountMismatchRegex(),
            "AI 模型响应格式异常，请尝试更换模型或降低批处理大小。"),
        new("本地模型未启动", EndpointLocalLlmRegex(),
            "请先在工具箱的本地 AI 页面启动模型。"),
        new("翻译端点被禁用", EndpointDisabledRegex(),
            "XUnity 因连续失败自动禁用了翻译端点，请重启游戏后再试。"),
        new("API 调用失败", EndpointApiFailRegex(),
            "请检查 AI 提供商配置和网络连接状态。"),
    ];

    private static string SafeExcerpt(string line)
    {
        var cleaned = LogPrefixRegex().Replace(line.Trim(), "");

        cleaned = AbsolutePathRegex().Replace(cleaned, m =>
        {
            var name = Path.GetFileName(m.Value.TrimEnd(')', ']', ',', ';', '"', '\''));
            return string.IsNullOrEmpty(name) ? "[path]" : name;
        });

        if (cleaned.Length > 120)
            cleaned = string.Concat(cleaned.AsSpan(0, 120), "...");

        return cleaned;
    }

    // Compiled regex patterns

    [GeneratedRegex(@"\[Info\s*:\s*BepInEx\]|BepInEx \d+\.\d+")]
    private static partial Regex BepInExInitRegex();

    [GeneratedRegex(@"XUnity\.AutoTranslator", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityLoadedRegex();

    [GeneratedRegex(@"\[Error\s*:.*(?:XUnity|AutoTranslator|LLMTranslate|BepInEx)", RegexOptions.IgnoreCase)]
    private static partial Regex PluginErrorRegex();

    [GeneratedRegex(@"LLMTranslate|consecutive.*error|endpoint.*disabled", RegexOptions.IgnoreCase)]
    private static partial Regex LlmTranslateLineRegex();

    [GeneratedRegex(@"Il2Cpp|Unhollower|Il2CppInterop|il2cpp_", RegexOptions.IgnoreCase)]
    private static partial Regex Il2CppErrorRegex();

    [GeneratedRegex(@"HarmonyException|HarmonyLib.*Exception|Failed to patch|Harmony.*[Pp]atch.*[Ff]ailed|PatchProcessor", RegexOptions.IgnoreCase)]
    private static partial Regex HarmonyPatchRegex();

    [GeneratedRegex(@"AssemblyResolutionException|version conflict|Could not resolve assembly|wrong version|version mismatch", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyVersionRegex();

    [GeneratedRegex(@"TypeLoadException|MissingMethodException|MissingFieldException|Could not load type", RegexOptions.IgnoreCase)]
    private static partial Regex TypeLoadRegex();

    [GeneratedRegex(@"Could not load.*plugin|Failed to load.*plugin|Error loading plugin|ReflectionTypeLoadException|PluginException", RegexOptions.IgnoreCase)]
    private static partial Regex PluginLoadRegex();

    [GeneratedRegex(@"[Ff]ont.*(?:not found|missing|Could not find)|Could not find.*[Ff]ont", RegexOptions.IgnoreCase)]
    private static partial Regex FontAssetMissingRegex();

    [GeneratedRegex(@"Cannot use fallback font.*not supported", RegexOptions.IgnoreCase)]
    private static partial Regex FontFallbackNotSupportedRegex();

    [GeneratedRegex(@"FileNotFoundException|Could not find|No such file|file.*not found|not found.*file", RegexOptions.IgnoreCase)]
    private static partial Regex FileNotFoundRegex();

    [GeneratedRegex(@"UnauthorizedAccessException|Access.*denied|Access to the path.*is denied", RegexOptions.IgnoreCase)]
    private static partial Regex AccessDeniedRegex();

    [GeneratedRegex(@"NullReferenceException")]
    private static partial Regex NullReferenceRegex();

    [GeneratedRegex(@"WebException|SocketException|HttpRequestException|connection.*refused|Unable to connect", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkErrorRegex();

    [GeneratedRegex(@"[Ff]ailed.*[Hh]ook|[Hh]ook.*[Ff]ailed|TextHook", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityCompatibilityRegex();

    [GeneratedRegex(@"Cannot.*translat|AutoTranslator.*[Ff]ailed|(?:^|\s)Failed:\s*'[^']+'|Translation.*failed", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityTranslationFailureRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:Unable to connect|ConnectFailure|连接.*失败|NameResolutionFailure)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointConnectFailRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:空响应|Empty response)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointEmptyResponseRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:数量不匹配|count mismatch)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointCountMismatchRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:\b503\b|本地模型未启动|本地模型未运行)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointLocalLlmRegex();

    [GeneratedRegex(@"consecutive.*error.*LLMTranslate|endpoint.*disabled.*LLMTranslate|Disabl.*LLMTranslate|LLMTranslate.*Disabl", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointDisabledRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:\b50[0-9]\b|Failed|失败|Timeout|超时)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointApiFailRegex();

    [GeneratedRegex(@"^\[[\w\s]+\s*:\s*[\w\.\s]+\]\s*")]
    private static partial Regex LogPrefixRegex();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s,;""']+|/(?:home|usr|var|opt|tmp)/[^\s,;""']+")]
    private static partial Regex AbsolutePathRegex();
}
