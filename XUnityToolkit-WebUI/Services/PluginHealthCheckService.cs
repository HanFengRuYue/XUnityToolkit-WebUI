using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
            if (gameProcess != null && !gameProcess.HasExited)
            {
                try
                {
                    gameProcess.Kill();
                    gameProcess.WaitForExit(5000);
                    logger.LogInformation("已关闭验证游戏进程");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "关闭游戏进程失败");
                }
            }
            gameProcess?.Dispose();
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
        var errorCount = lines.Count(l => PluginErrorRegex().IsMatch(l));

        if (errorCount == 0)
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Healthy, null));
        else if (errorCount < 5)
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Warning,
                $"发现 {errorCount} 条插件相关错误日志，请查看 BepInEx 日志了解详情"));
        else
            checks.Add(new("logErrors", "运行错误日志", HealthStatus.Error,
                $"发现 {errorCount} 条插件相关错误日志，插件可能存在严重问题。请查看 BepInEx 日志分析"));
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

    // ─── Compiled Regex Patterns ──────────────────────────────────────────

    [GeneratedRegex(@"\[Info\s*:\s*BepInEx\]|BepInEx \d+\.\d+")]
    private static partial Regex BepInExInitRegex();

    [GeneratedRegex(@"XUnity\.AutoTranslator", RegexOptions.IgnoreCase)]
    private static partial Regex XUnityLoadedRegex();

    [GeneratedRegex(@"\[Error\s*:.*(?:XUnity|AutoTranslator|LLMTranslate|BepInEx)", RegexOptions.IgnoreCase)]
    private static partial Regex PluginErrorRegex();
}
