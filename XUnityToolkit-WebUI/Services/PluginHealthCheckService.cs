using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class PluginHealthCheckService(ILogger<PluginHealthCheckService> logger)
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
    /// Perform a passive health check by inspecting files and parsing the BepInEx log.
    /// </summary>
    /// <param name="game">The game to check.</param>
    /// <param name="connectivityVerified">null=passive check, true=ping received, false=no ping.</param>
    public PluginHealthReport Check(Game game, bool? connectivityVerified = null)
    {
        var checks = new List<HealthCheckItem>();
        var gamePath = game.GamePath;
        var isIL2CPP = game.DetectedInfo?.Backend == UnityBackend.IL2CPP;

        // Tier 1: File integrity checks
        CheckDoorstopProxy(checks, gamePath, isIL2CPP);
        CheckDoorstopConfig(checks, gamePath);
        CheckBepInExCore(checks, gamePath);
        CheckXUnityPlugin(checks, gamePath);
        CheckTranslatorConfig(checks, gamePath);
        var hasEndpointDir = Directory.Exists(
            Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators"));
        if (hasEndpointDir)
            CheckTranslatorEndpoint(checks, gamePath);

        // Tier 2: Log-based checks
        var logPath = Path.Combine(gamePath, "BepInEx", "LogOutput.log");
        DateTime? logLastModified = null;
        var gameNeverRun = !File.Exists(logPath);

        if (!gameNeverRun)
        {
            var fi = new FileInfo(logPath);
            logLastModified = fi.LastWriteTimeUtc;

            var logLines = ReadLogLines(logPath);
            if (logLines != null)
            {
                CheckBepInExLoaded(checks, logLines);
                CheckXUnityLoaded(checks, logLines);
                if (hasEndpointDir)
                    CheckEndpointRegistered(checks, logLines);
                CheckLogErrors(checks, logLines);
            }
        }

        // Tier 3: Connectivity check (only after verify)
        if (connectivityVerified.HasValue)
        {
            if (connectivityVerified.Value)
                checks.Add(new("toolboxConnectivity", "工具箱连通性", HealthStatus.Healthy, null));
            else
                checks.Add(new("toolboxConnectivity", "工具箱连通性", HealthStatus.Error,
                    "插件无法连接工具箱，可能游戏存在网络兼容性问题。请检查防火墙设置或尝试以管理员权限运行"));
        }

        var overall = DetermineOverall(checks, gameNeverRun);
        return new PluginHealthReport(overall, checks, logLastModified, gameNeverRun, DateTime.UtcNow);
    }

    /// <summary>
    /// Verify installation health — called by InstallOrchestrator during install.
    /// Skips the active verification guard (orchestrator owns concurrency control).
    /// </summary>
    public Task<PluginHealthReport> VerifyForInstallAsync(Game game, CancellationToken ct)
        => VerifyCore(game, ct);

    /// <summary>
    /// Verify installation by launching the game briefly to generate logs, then analyze.
    /// </summary>
    public async Task<PluginHealthReport> VerifyAsync(Game game, CancellationToken ct)
    {
        if (!_activeVerifications.TryAdd(game.Id, true))
            throw new InvalidOperationException("该游戏正在验证安装中，请等待完成。");

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

        // Delete old log to get a fresh one
        if (File.Exists(logPath))
        {
            try { File.Delete(logPath); }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "无法删除旧日志文件，可能游戏正在运行");
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

            // Wait for log file to appear
            var elapsed = 0;
            while (!File.Exists(logPath) && elapsed < LogWaitTimeoutSeconds)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1000, ct);
                elapsed++;
            }

            if (File.Exists(logPath))
            {
                // Wait for BepInEx to finish loading plugins and ping to arrive
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
            // UseShellExecute=true may return a shell launcher (e.g. Steam) that exits
            // immediately while the real game runs separately. Kill by process name as fallback.
            await GameProcessHelper.KillGameProcessAsync(gameProcess, exeName, game.GamePath, logger);
        }

        var pingReceived = HasRecentPing(game.Id, verifyStartTime);
        logger.LogInformation("连通性检测结果: {Result}, 游戏 {GameId}",
            pingReceived ? "已收到 ping" : "未收到 ping", game.Id);
        return Check(game, connectivityVerified: pingReceived);
    }

    // ─── Tier 1: File Integrity Checks ────────────────────────────────────

    private static void CheckDoorstopProxy(List<HealthCheckItem> checks, string gamePath, bool isIL2CPP)
    {
        // BepInEx 5 (Mono) uses winhttp.dll; BepInEx 6 (IL2CPP) uses both winhttp.dll and dobby.dll
        var winhttpExists = File.Exists(Path.Combine(gamePath, "winhttp.dll"));
        var dobbyExists = File.Exists(Path.Combine(gamePath, "dobby.dll"));

        if (isIL2CPP)
        {
            // IL2CPP needs winhttp.dll + dobby.dll
            if (winhttpExists && dobbyExists)
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Healthy, null));
            else if (!winhttpExists && !dobbyExists)
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 和 dobby.dll 均不存在，可能被杀毒软件删除。请将游戏目录加入杀毒软件白名单后重新安装"));
            else if (!winhttpExists)
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 不存在，可能被杀毒软件删除。请将游戏目录加入杀毒软件白名单后重新安装"));
            else
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "dobby.dll 不存在，BepInEx 6 IL2CPP 模式需要此文件。请重新安装"));
        }
        else
        {
            // Mono needs winhttp.dll
            if (winhttpExists)
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Healthy, null));
            else
                checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                    "winhttp.dll 不存在，可能被杀毒软件删除。请将游戏目录加入杀毒软件白名单后重新安装"));
        }
    }

    private static void CheckDoorstopConfig(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "doorstop_config.ini");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Healthy, null));
        else if (File.Exists(path))
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Warning,
                "doorstop_config.ini 文件为空，BepInEx 可能无法正常加载"));
        else
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Error,
                "doorstop_config.ini 不存在，BepInEx 无法启动。请重新安装"));
    }

    private static void CheckBepInExCore(List<HealthCheckItem> checks, string gamePath)
    {
        var coreDir = Path.Combine(gamePath, "BepInEx", "core");
        var hasBepInEx5 = File.Exists(Path.Combine(coreDir, "BepInEx.dll"));
        var hasBepInEx6 = File.Exists(Path.Combine(coreDir, "BepInEx.Core.dll"));

        if (hasBepInEx5 || hasBepInEx6)
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Healthy, null));
        else
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Error,
                "BepInEx 核心 DLL 不存在，框架无法运行。请重新安装"));
    }

    private static void CheckXUnityPlugin(List<HealthCheckItem> checks, string gamePath)
    {
        var pluginDir = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator");
        if (!Directory.Exists(pluginDir))
        {
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Error,
                "XUnity.AutoTranslator 插件目录不存在。请重新安装"));
            return;
        }

        var hasDll = Directory.GetFiles(pluginDir, "XUnity.AutoTranslator*.dll").Length > 0;
        if (hasDll)
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Healthy, null));
        else
            checks.Add(new("xunityPlugin", "XUnity 翻译插件", HealthStatus.Error,
                "XUnity.AutoTranslator DLL 不存在。请重新安装"));
    }

    private static void CheckTranslatorConfig(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Healthy, null));
        else if (File.Exists(path))
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Warning,
                "AutoTranslatorConfig.ini 文件为空，翻译功能可能不正常"));
        else
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Warning,
                "AutoTranslatorConfig.ini 不存在，需要运行一次游戏生成配置文件"));
    }

    private static void CheckTranslatorEndpoint(List<HealthCheckItem> checks, string gamePath)
    {
        var path = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator", "Translators", "LLMTranslate.dll");
        if (File.Exists(path))
            checks.Add(new("translatorEndpoint", "AI 翻译端点", HealthStatus.Healthy, null));
        else
            checks.Add(new("translatorEndpoint", "AI 翻译端点", HealthStatus.Error,
                "LLMTranslate.dll 不存在，AI 翻译功能不可用。可在游戏详情页重新安装"));
    }

    // ─── Tier 2: Log-Based Checks ─────────────────────────────────────────

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
        // Check first 50 lines for BepInEx init marker
        var searchLines = lines.Length > 50 ? lines[..50] : lines;
        var found = searchLines.Any(l => BepInExInitRegex().IsMatch(l));

        if (found)
            checks.Add(new("bepinexLoaded", "BepInEx 框架初始化", HealthStatus.Healthy, null));
        else
            checks.Add(new("bepinexLoaded", "BepInEx 框架初始化", HealthStatus.Error,
                "日志中未找到 BepInEx 初始化标记，框架可能未成功加载。请检查杀毒软件是否阻止了 winhttp.dll"));
    }

    private static void CheckXUnityLoaded(List<HealthCheckItem> checks, string[] lines)
    {
        var found = lines.Any(l => XUnityLoadedRegex().IsMatch(l));

        if (found)
            checks.Add(new("xunityLoaded", "XUnity 插件加载", HealthStatus.Healthy, null));
        else
            checks.Add(new("xunityLoaded", "XUnity 插件加载", HealthStatus.Error,
                "日志中未找到 XUnity.AutoTranslator 加载记录，插件可能未正常加载"));
    }

    private static void CheckEndpointRegistered(List<HealthCheckItem> checks, string[] lines)
    {
        var found = lines.Any(l => l.Contains("LLMTranslate", StringComparison.OrdinalIgnoreCase));

        if (found)
            checks.Add(new("endpointRegistered", "翻译端点注册", HealthStatus.Healthy, null));
        else
            checks.Add(new("endpointRegistered", "翻译端点注册", HealthStatus.Warning,
                "日志中未找到 LLMTranslate 端点注册记录，AI 翻译端点可能未加载"));
    }

    private static void CheckLogErrors(List<HealthCheckItem> checks, string[] lines)
    {
        var details = new List<HealthCheckDetail>();
        var seenCategories = new Dictionary<string, int>();

        // Pre-scan: check if TMP font fallback is unsupported (non-critical error)
        var fontFallbackUnsupported = lines.Any(l => FontFallbackNotSupportedRegex().IsMatch(l));

        // Pass 1: Scan [Error:] lines for general error patterns
        var errorCount = 0;
        foreach (var line in lines)
        {
            if (!PluginErrorRegex().IsMatch(line)) continue;

            // Skip font-missing errors when the game doesn't support TMP font fallback
            if (fontFallbackUnsupported && FontAssetMissingRegex().IsMatch(line))
                continue;

            errorCount++;
            MatchErrorPatterns(line, GeneralErrorPatterns, details, seenCategories);
        }

        // Pass 2: Scan ALL lines for LLMTranslate endpoint-specific errors
        // (DLL logs via Console.WriteLine → appears as [Info/Message:], not [Error:])
        foreach (var line in lines)
        {
            if (!LlmTranslateLineRegex().IsMatch(line)) continue;
            MatchErrorPatterns(line, EndpointErrorPatterns, details, seenCategories);
        }

        // Cap total details
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
            // Only endpoint-level issues found (no [Error:] lines)
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

    private static void MatchErrorPatterns(
        string line,
        IReadOnlyList<ErrorPattern> patterns,
        List<HealthCheckDetail> details,
        Dictionary<string, int> seenCategories)
    {
        foreach (var pattern in patterns)
        {
            if (!pattern.Matcher.IsMatch(line)) continue;
            seenCategories.TryGetValue(pattern.Category, out var count);
            if (count < MaxDetailsPerCategory)
            {
                details.Add(new(pattern.Category, SafeExcerpt(line), pattern.Suggestion));
                seenCategories[pattern.Category] = count + 1;
            }
            break; // first match wins per line
        }
    }

    // ─── Overall Status ───────────────────────────────────────────────────

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

    // ─── Error Pattern Matching ─────────────────────────────────────────

    private const int MaxDetailsPerCategory = 2;
    private const int MaxTotalDetails = 10;

    private record ErrorPattern(string Category, Regex Matcher, string? Suggestion);

    private static readonly IReadOnlyList<ErrorPattern> GeneralErrorPatterns =
    [
        new("IL2CPP 兼容性错误", Il2CppErrorRegex(),
            "检查 BepInEx 6 版本是否支持当前游戏的 IL2CPP 版本"),
        new("Harmony 补丁失败", HarmonyPatchRegex(),
            "可能存在插件冲突，尝试移除其他 BepInEx 插件"),
        new("程序集版本冲突", AssemblyVersionRegex(),
            "游戏更新可能导致 DLL 版本不匹配，尝试重新安装"),
        new("类型/方法加载错误", TypeLoadRegex(),
            "通常由游戏更新引起，尝试更新 XUnity.AutoTranslator 版本"),
        new("插件加载失败", PluginLoadRegex(),
            "检查插件 DLL 是否完整，尝试重新安装"),
        new("字体资源缺失", FontAssetMissingRegex(),
            "在游戏详情页的 TMP 字体功能中重新安装字体"),
        new("文件/资源未找到", FileNotFoundRegex(),
            "检查杀毒软件是否删除了文件"),
        new("访问权限错误", AccessDeniedRegex(),
            "以管理员权限运行工具箱，或将游戏目录添加至杀毒软件白名单"),
        new("空引用崩溃", NullReferenceRegex(), null),
        new("网络连接错误", NetworkErrorRegex(),
            "检查防火墙设置或在 AI 翻译设置中修改监听端口"),
        new("XUnity 翻译异常", XUnityHookRegex(),
            "当前游戏版本可能不完全兼容 XUnity"),
    ];

    private static readonly IReadOnlyList<ErrorPattern> EndpointErrorPatterns =
    [
        new("工具箱连接失败", EndpointConnectFailRegex(),
            "确认工具箱已启动且端口正确（默认 51821）"),
        new("翻译返回空响应", EndpointEmptyResponseRegex(),
            "检查 AI 翻译设置中的 API 密钥是否有效"),
        new("翻译数量不匹配", EndpointCountMismatchRegex(),
            "AI 模型响应格式异常，尝试更换模型或降低批量大小"),
        new("本地模型未启动", EndpointLocalLlmRegex(),
            "需要在工具箱的本地 AI 页面先启动模型"),
        new("翻译端点被禁用", EndpointDisabledRegex(),
            "XUnity 因连续失败自动禁用了翻译端点，需重启游戏"),
        new("API 调用失败", EndpointApiFailRegex(),
            "检查 AI 翻译提供商配置和网络连接"),
    ];

    private static string SafeExcerpt(string line)
    {
        // Strip BepInEx log prefix: [Level : Source]
        var cleaned = LogPrefixRegex().Replace(line.Trim(), "");

        // Replace absolute path segments with filename only
        cleaned = AbsolutePathRegex().Replace(cleaned, m =>
        {
            var name = Path.GetFileName(m.Value.TrimEnd(')', ']', ',', ';', '"', '\''));
            return string.IsNullOrEmpty(name) ? "[path]" : name;
        });

        if (cleaned.Length > 120)
            cleaned = string.Concat(cleaned.AsSpan(0, 120), "…");

        return cleaned;
    }

    // ─── Compiled Regex Patterns ──────────────────────────────────────────

    [GeneratedRegex(@"\[Info\s*:\s*BepInEx\]|BepInEx \d+\.\d+")]
    private static partial Regex BepInExInitRegex();

    [GeneratedRegex(@"XUnity\.AutoTranslator", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityLoadedRegex();

    [GeneratedRegex(@"\[Error\s*:.*(?:XUnity|AutoTranslator|LLMTranslate|BepInEx)", RegexOptions.IgnoreCase)]
    private static partial Regex PluginErrorRegex();

    // Pre-filter for lines that might contain LLMTranslate endpoint errors
    [GeneratedRegex(@"LLMTranslate|consecutive.*error|endpoint.*disabled", RegexOptions.IgnoreCase)]
    private static partial Regex LlmTranslateLineRegex();

    // General error patterns (#1-#10)
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

    [GeneratedRegex(@"[Ff]ailed.*[Hh]ook|[Hh]ook.*[Ff]ailed|TextHook|Cannot.*translat|AutoTranslator.*[Ff]ailed", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityHookRegex();

    // LLMTranslate endpoint-specific patterns (#11-#16)
    [GeneratedRegex(@"LLMTranslate.*(?:Unable to connect|ConnectFailure|连接.*失败|NameResolutionFailure)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointConnectFailRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:空响应|Empty response)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointEmptyResponseRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:数量不匹配|count mismatch)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointCountMismatchRegex();

    [GeneratedRegex(@"LLMTranslate.*503|(?:本地模型未启动|本地模型未运行)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointLocalLlmRegex();

    [GeneratedRegex(@"consecutive.*error.*LLMTranslate|endpoint.*disabled.*LLMTranslate|Disabl.*LLMTranslate|LLMTranslate.*Disabl", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointDisabledRegex();

    [GeneratedRegex(@"LLMTranslate.*(?:50[0-9]|Failed|失败|Timeout|超时)", RegexOptions.IgnoreCase)]
    private static partial Regex EndpointApiFailRegex();

    // Utility patterns for SafeExcerpt
    [GeneratedRegex(@"^\[[\w\s]+\s*:\s*[\w\.\s]+\]\s*")]
    private static partial Regex LogPrefixRegex();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s,;""']+|/(?:home|usr|var|opt|tmp)/[^\s,;""']+")]
    private static partial Regex AbsolutePathRegex();
}
