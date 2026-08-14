using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PluginHealthCheckService(
    AppSettingsService settingsService,
    LocalLlmService localLlmService,
    ToolkitRuntimeEndpointState runtimeEndpoint,
    AppDataPaths appDataPaths,
    PluginConnectionRegistry connectionRegistry,
    XUnityInstallerService xUnityInstaller,
    PluginDiagnosticArtifactCollector artifactCollector,
    PluginDiagnosticAgentService diagnosticAgent,
    PluginAutoRepairService autoRepairService,
    ILogger<PluginHealthCheckService> logger)
{
    private const int VerificationTimeoutSeconds = 30;
    private const int VerificationPollIntervalMilliseconds = 250;
    private const int LogSettleDelayMilliseconds = 1000;

    private readonly ConcurrentDictionary<string, bool> _activeVerifications = [];
    private readonly ConcurrentDictionary<string, DateTime> _receivedPings = [];

    public void RecordPing(string gameId) => _receivedPings[gameId] = DateTime.UtcNow;

    public bool HasRecentPing(string gameId, DateTime since) =>
        _receivedPings.TryGetValue(gameId, out var timestamp) && timestamp >= since;

    /// <summary>
    /// Refresh local objective facts and attach the latest in-memory AI attempt without calling a model.
    /// </summary>
    public async Task<PluginHealthReport> CheckAsync(
        Game game,
        bool? connectivityVerified = null,
        bool freshRunVerified = false,
        CancellationToken ct = default)
    {
        var report = await BuildObjectiveReportAsync(game, connectivityVerified, freshRunVerified, ct);
        var snapshot = await artifactCollector.CollectInventoryAsync(game, report.Checks, ct);
        return WithRepairAvailability(game, diagnosticAgent.AttachCached(game.Id, snapshot.Fingerprint, report));
    }

    /// <summary>Run the two-stage AI agent against current files and historical log data.</summary>
    public async Task<PluginHealthReport> AnalyzeAsync(
        Game game,
        bool? connectivityVerified = null,
        bool freshRunVerified = false,
        CancellationToken ct = default)
    {
        if (!diagnosticAgent.TryBeginDiagnostic(game.Id))
            throw new PluginDiagnosticAlreadyRunningException();

        try
        {
            return await AnalyzeReservedAsync(game, connectivityVerified, freshRunVerified, ct);
        }
        finally
        {
            diagnosticAgent.EndDiagnostic(game.Id);
        }
    }

    /// <summary>
    /// Run cloud diagnosis, execute only allowlisted recoverable repairs, then collect a fresh report.
    /// Local models are deliberately rejected by the diagnostic agent before any file mutation occurs.
    /// </summary>
    public async Task<PluginAutoRepairResult> RepairAsync(Game game, CancellationToken ct = default)
    {
        if (!diagnosticAgent.TryBeginDiagnostic(game.Id))
            throw new PluginDiagnosticAlreadyRunningException();

        try
        {
            var objectiveBefore = await BuildObjectiveReportAsync(
                game, connectivityVerified: null, freshRunVerified: false, ct);
            var snapshotBefore = await artifactCollector.CollectInventoryAsync(game, objectiveBefore.Checks, ct);
            var before = WithRepairAvailability(
                game,
                await diagnosticAgent.AnalyzeReservedAsync(game, objectiveBefore, snapshotBefore, ct));

            var hasDeterministicRepair = autoRepairService.HasDeterministicRepair(game, before);
            if ((before.AnalysisState != PluginAnalysisState.Completed || before.Analysis is null)
                && !hasDeterministicRepair)
            {
                return new PluginAutoRepairResult(
                    before,
                    before,
                    [],
                    before.AnalysisMessage ?? "云端 AI 诊断未成功完成，本次没有修改任何文件。",
                    string.Empty,
                    DateTime.UtcNow);
            }

            var plan = before.AnalysisState == PluginAnalysisState.Completed && before.Analysis is not null
                ? await diagnosticAgent.PlanRepairsAsync(game, before, snapshotBefore, ct)
                : (Summary: "已根据本地确定性检查执行工具箱内置修复。",
                    Actions: new List<PluginRepairPlanAction>(), EndpointName: string.Empty);
            var actions = await autoRepairService.ExecuteAsync(game, before, snapshotBefore, plan.Actions, ct);
            if (actions.Count == 0)
            {
                return new PluginAutoRepairResult(
                    before,
                    before,
                    [],
                    plan.Summary,
                    plan.EndpointName.Length > 0 ? plan.EndpointName : before.Analysis?.EndpointName ?? string.Empty,
                    DateTime.UtcNow);
            }

            var objectiveAfter = await BuildObjectiveReportAsync(
                game, connectivityVerified: null, freshRunVerified: false, ct);
            var snapshotAfter = await artifactCollector.CollectInventoryAsync(game, objectiveAfter.Checks, ct);
            var after = before.AnalysisState == PluginAnalysisState.Completed
                ? WithRepairAvailability(
                    game,
                    await diagnosticAgent.AnalyzeReservedAsync(game, objectiveAfter, snapshotAfter, ct))
                : WithRepairAvailability(game, objectiveAfter);
            var completed = actions.Count(action => action.State == PluginRepairActionState.Completed);
            var failed = actions.Count(action => action.State == PluginRepairActionState.Failed);
            var skipped = actions.Count(action => action.State == PluginRepairActionState.Skipped);
            var summary = $"自动修复已执行并复检：成功 {completed} 项，失败 {failed} 项，跳过 {skipped} 项。";
            return new PluginAutoRepairResult(
                before,
                after,
                actions,
                summary,
                plan.EndpointName.Length > 0 ? plan.EndpointName : after.Analysis?.EndpointName ?? string.Empty,
                DateTime.UtcNow);
        }
        finally
        {
            diagnosticAgent.EndDiagnostic(game.Id);
        }
    }

    /// <summary>
    /// Installation verification deliberately remains local-only so the default install flow never incurs AI cost.
    /// </summary>
    public Task<PluginHealthReport> VerifyForInstallAsync(Game game, CancellationToken ct) =>
        VerifyCoreAsync(game, runAi: false, ct);

    /// <summary>Launch the game, wait for a fresh log and endpoint ping, then run the AI diagnostic agent.</summary>
    public async Task<PluginHealthReport> VerifyAsync(Game game, CancellationToken ct)
    {
        if (!_activeVerifications.TryAdd(game.Id, true))
            throw new InvalidOperationException("该游戏正在验证安装状态中，请等待当前验证完成。");

        if (!diagnosticAgent.TryBeginDiagnostic(game.Id))
        {
            _activeVerifications.TryRemove(game.Id, out _);
            throw new PluginDiagnosticAlreadyRunningException();
        }

        try
        {
            return await VerifyCoreAsync(game, runAi: true, ct);
        }
        finally
        {
            diagnosticAgent.EndDiagnostic(game.Id);
            _activeVerifications.TryRemove(game.Id, out _);
        }
    }

    private async Task<PluginHealthReport> VerifyCoreAsync(Game game, bool runAi, CancellationToken ct)
    {
        var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable
            ?? throw new InvalidOperationException("无法确定游戏可执行文件路径。");
        var exePath = Path.GetFullPath(Path.Combine(game.GamePath, exeName));
        var normalizedGamePath = Path.GetFullPath(game.GamePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!exePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("可执行文件路径不在游戏目录内。");

        var logPath = Path.Combine(game.GamePath, "BepInEx", "LogOutput.log");
        var verifyStartTime = DateTime.UtcNow;

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
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "无法删除旧日志文件，访问被拒绝");
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

            var deadline = DateTime.UtcNow.AddSeconds(VerificationTimeoutSeconds);
            var freshLogDetected = false;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (!freshLogDetected && IsFreshLog(logPath, verifyStartTime))
                {
                    freshLogDetected = true;
                    logger.LogInformation("已生成本次验证日志，等待 AI 翻译端点连通性 ping...");
                }

                if (freshLogDetected && HasRecentPing(game.Id, verifyStartTime))
                {
                    await Task.Delay(LogSettleDelayMilliseconds, ct);
                    break;
                }

                await Task.Delay(VerificationPollIntervalMilliseconds, ct);
            }

            if (!IsFreshLog(logPath, verifyStartTime))
                logger.LogWarning("等待本次验证日志超时 ({Timeout}s)", VerificationTimeoutSeconds);
            else if (!HasRecentPing(game.Id, verifyStartTime))
                logger.LogWarning("已生成本次验证日志，但等待工具箱连通性 ping 超时 ({Timeout}s)", VerificationTimeoutSeconds);
        }
        finally
        {
            await GameProcessHelper.KillGameProcessAsync(gameProcess, exeName, game.GamePath, logger);
        }

        var pingReceived = HasRecentPing(game.Id, verifyStartTime);
        var freshRunVerified = pingReceived && IsFreshLog(logPath, verifyStartTime);
        logger.LogInformation("连通性检测结果: {Result}, 游戏 {GameId}",
            pingReceived ? "已收到 ping" : "未收到 ping", game.Id);

        if (runAi)
            return await AnalyzeReservedAsync(game, connectivityVerified: pingReceived, freshRunVerified, ct);

        return await BuildObjectiveReportAsync(game, connectivityVerified: pingReceived, freshRunVerified, ct);
    }

    private async Task<PluginHealthReport> AnalyzeReservedAsync(
        Game game,
        bool? connectivityVerified,
        bool freshRunVerified,
        CancellationToken ct)
    {
        var report = await BuildObjectiveReportAsync(game, connectivityVerified, freshRunVerified, ct);
        var snapshot = await artifactCollector.CollectInventoryAsync(game, report.Checks, ct);
        return WithRepairAvailability(
            game,
            await diagnosticAgent.AnalyzeReservedAsync(game, report, snapshot, ct));
    }

    private async Task<PluginHealthReport> BuildObjectiveReportAsync(
        Game game,
        bool? connectivityVerified,
        bool freshRunVerified,
        CancellationToken ct)
    {
        var checks = new List<HealthCheckItem>();
        var gamePath = game.GamePath;
        var isIl2Cpp = game.DetectedInfo?.Backend == UnityBackend.IL2CPP;

        CheckDoorstopProxy(checks, gamePath, isIl2Cpp);
        await CheckDoorstopConfigAsync(checks, gamePath, ct);
        CheckBepInExCore(checks, gamePath);
        CheckXUnityPlugin(checks, gamePath);

        var endpointPath = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator",
            "Translators", "LLMTranslate.dll");
        CheckRequiredFile(checks, "translatorEndpoint", "AI 翻译端点", endpointPath, emptyIsError: true);
        CheckTranslatorEndpointPackage(checks, game);
        CheckToolkitRuntime(checks);

        var appSettings = await settingsService.GetAsync(ct);
        await CheckTranslatorConfigAsync(
            checks,
            game,
            runtimeEndpoint.BaseUrl,
            appDataPaths.ToolkitEndpointDiscoveryFile,
            ct);
        CheckToolboxAiState(checks, appSettings.AiTranslation);
        CheckPluginConnection(checks, game.Id);

        var logPath = Path.Combine(gamePath, "BepInEx", "LogOutput.log");
        var gameNeverRun = !File.Exists(logPath);
        DateTime? logLastModified = null;
        if (gameNeverRun)
        {
            checks.Add(new("runtimeLog", "BepInEx 运行日志", HealthStatus.Unknown,
                "尚未发现 BepInEx/LogOutput.log，无法确认插件实际加载状态。"));
        }
        else
        {
            var info = new FileInfo(logPath);
            logLastModified = info.LastWriteTimeUtc;
            checks.Add(new("runtimeLog", "BepInEx 运行日志", HealthStatus.Healthy,
                $"已发现运行日志，大小 {info.Length} 字节；日志内容尚未由 AI 诊断。"));
        }

        if (connectivityVerified.HasValue)
        {
            checks.Add(connectivityVerified.Value
                ? new HealthCheckItem("toolboxConnectivity", "工具箱连通性", HealthStatus.Healthy,
                    "本次启动已收到 LLMTranslate 端点 ping。")
                : new HealthCheckItem("toolboxConnectivity", "工具箱连通性", HealthStatus.Error,
                    "本次启动未收到 LLMTranslate 端点 ping。"));
        }

        var objectiveOverall = DetermineObjectiveOverall(checks);
        var report = new PluginHealthReport(
            HealthStatus.Unknown,
            objectiveOverall,
            checks,
            PluginAnalysisState.NotRun,
            null,
            null,
            logLastModified,
            gameNeverRun,
            freshRunVerified,
            DateTime.UtcNow);
        return WithRepairAvailability(game, PluginDiagnosticAgentService.RecalculateOverall(report));
    }

    private PluginHealthReport WithRepairAvailability(Game game, PluginHealthReport report) =>
        autoRepairService.AttachRepairAvailability(game, report);

    private void CheckToolboxAiState(List<HealthCheckItem> checks, AiTranslationSettings ai)
    {
        if (!ai.Enabled)
        {
            checks.Add(new("toolboxAiState", "工具箱实时 AI 翻译", HealthStatus.Warning,
                "AI 翻译总开关当前已关闭；显式智能诊断仍可使用已配置的模型端点。"));
            return;
        }

        if (string.Equals(ai.ActiveMode, "local", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(localLlmService.IsRunning
                ? new HealthCheckItem("toolboxAiState", "工具箱实时 AI 翻译", HealthStatus.Healthy,
                    "当前使用本地模式，本地模型正在运行。")
                : new HealthCheckItem("toolboxAiState", "工具箱实时 AI 翻译", HealthStatus.Warning,
                    "当前使用本地模式，但本地模型尚未运行。"));
            return;
        }

        var hasCloudEndpoint = ai.Endpoints.Any(endpoint =>
            endpoint.Enabled
            && !string.IsNullOrWhiteSpace(endpoint.ApiKey)
            && !string.Equals(endpoint.ApiKey, "local", StringComparison.OrdinalIgnoreCase));
        checks.Add(hasCloudEndpoint
            ? new HealthCheckItem("toolboxAiState", "工具箱实时 AI 翻译", HealthStatus.Healthy,
                "当前云端模式存在已启用的模型端点。")
            : new HealthCheckItem("toolboxAiState", "工具箱实时 AI 翻译", HealthStatus.Warning,
                "当前云端模式没有已启用且配置了 API Key 的模型端点。"));
    }

    internal static void CheckDoorstopProxy(List<HealthCheckItem> checks, string gamePath, bool isIl2Cpp)
    {
        var winhttpExists = File.Exists(Path.Combine(gamePath, "winhttp.dll"));
        var dobbyExists = File.Exists(Path.Combine(gamePath, "BepInEx", "core", "dobby.dll"))
                          || File.Exists(Path.Combine(gamePath, "dobby.dll"));

        if (!winhttpExists)
        {
            checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                "所需文件 winhttp.dll 不存在。"));
            return;
        }

        if (isIl2Cpp && !dobbyExists)
        {
            checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Error,
                "IL2CPP 安装中未发现 BepInEx/core/dobby.dll 或兼容的根目录 dobby.dll。"));
            return;
        }

        checks.Add(new("doorstopProxy", "BepInEx 代理 DLL", HealthStatus.Healthy,
            isIl2Cpp ? "已发现 winhttp.dll 和 dobby.dll。" : "已发现 winhttp.dll。"));
    }

    internal static async Task CheckDoorstopConfigAsync(
        List<HealthCheckItem> checks,
        string gamePath,
        CancellationToken ct = default)
    {
        var path = Path.Combine(gamePath, "doorstop_config.ini");
        if (!File.Exists(path))
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Error,
                "所需文件 doorstop_config.ini 不存在。"));
            return;
        }

        string content;
        try
        {
            content = await ReadSharedTextAsync(path, ct);
        }
        catch
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Warning,
                "doorstop_config.ini 存在，但当前无法以共享读取方式读取。"));
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            checks.Add(new("doorstopConfig", "Doorstop 启动配置", HealthStatus.Error,
                "doorstop_config.ini 存在但内容为空。"));
            return;
        }

        // UnityDoorstop 4 packages use [General] + target_assembly. Keep the
        // older [UnityDoorstop] + targetAssembly schema readable for existing installs.
        var modernEnabled = ReadIniValue(content, "General", "enabled");
        var modernTargetAssembly = ReadIniValue(content, "General", "target_assembly");
        var usesModernFormat = modernEnabled is not null || modernTargetAssembly is not null;
        var enabled = usesModernFormat
            ? modernEnabled
            : ReadIniValue(content, "UnityDoorstop", "enabled");
        var targetAssembly = usesModernFormat
            ? modernTargetAssembly
            : ReadIniValue(content, "UnityDoorstop", "targetAssembly");
        var enabledSetting = usesModernFormat ? "General.enabled" : "UnityDoorstop.enabled";
        var targetAssemblySetting = usesModernFormat
            ? "General.target_assembly"
            : "UnityDoorstop.targetAssembly";
        var errors = new List<string>();
        if (enabled is null)
            errors.Add($"{enabledSetting} 未配置");
        else if (!bool.TryParse(enabled, out var isEnabled) || !isEnabled)
            errors.Add($"{enabledSetting} 当前为 {enabled}，Doorstop 未明确启用");

        if (string.IsNullOrWhiteSpace(targetAssembly))
        {
            errors.Add($"{targetAssemblySetting} 未配置");
        }
        else
        {
            try
            {
                var root = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;
                var targetPath = Path.GetFullPath(Path.Combine(gamePath, targetAssembly));
                if (Path.IsPathFullyQualified(targetAssembly)
                    || !targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{targetAssemblySetting} 不在游戏目录内");
                }
                else if (!File.Exists(targetPath))
                {
                    errors.Add($"{targetAssemblySetting} 指向的 {targetAssembly.Replace('\\', '/')} 不存在");
                }
            }
            catch
            {
                errors.Add($"{targetAssemblySetting} 不是有效的游戏内相对路径");
            }
        }

        checks.Add(errors.Count == 0
            ? new HealthCheckItem("doorstopConfig", "Doorstop 启动配置", HealthStatus.Healthy,
                $"已识别{(usesModernFormat ? "当前" : "旧版")}配置，Doorstop 已启用，" +
                $"目标程序集 {targetAssembly!.Replace('\\', '/')} 已存在。")
            : new HealthCheckItem("doorstopConfig", "Doorstop 启动配置", HealthStatus.Error,
                string.Join("；", errors) + "。"));
    }

    private static void CheckRequiredFile(
        List<HealthCheckItem> checks,
        string id,
        string label,
        string path,
        bool emptyIsError)
    {
        if (!File.Exists(path))
        {
            checks.Add(new(id, label, HealthStatus.Error, $"所需文件 {Path.GetFileName(path)} 不存在。"));
            return;
        }

        var length = new FileInfo(path).Length;
        if (length == 0)
        {
            checks.Add(new(id, label, emptyIsError ? HealthStatus.Error : HealthStatus.Warning,
                $"文件 {Path.GetFileName(path)} 存在但内容为空。"));
            return;
        }

        checks.Add(new(id, label, HealthStatus.Healthy,
            $"已发现 {Path.GetFileName(path)}，大小 {length} 字节。"));
    }

    private static void CheckBepInExCore(List<HealthCheckItem> checks, string gamePath)
    {
        var coreDir = Path.Combine(gamePath, "BepInEx", "core");
        var bepInEx5 = Path.Combine(coreDir, "BepInEx.dll");
        var bepInEx6 = Path.Combine(coreDir, "BepInEx.Core.dll");
        if (File.Exists(bepInEx5) || File.Exists(bepInEx6))
        {
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Healthy,
                $"已发现 {Path.GetFileName(File.Exists(bepInEx6) ? bepInEx6 : bepInEx5)}。"));
        }
        else
        {
            checks.Add(new("bepinexCore", "BepInEx 核心框架", HealthStatus.Error,
                "BepInEx/core 中未发现 BepInEx.dll 或 BepInEx.Core.dll。"));
        }
    }

    private static void CheckXUnityPlugin(List<HealthCheckItem> checks, string gamePath)
    {
        var pluginDir = Path.Combine(gamePath, "BepInEx", "plugins", "XUnity.AutoTranslator");
        var hasPlugin = false;
        if (Directory.Exists(pluginDir))
        {
            try
            {
                hasPlugin = Directory.EnumerateFiles(pluginDir, "XUnity.AutoTranslator*.dll", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                // Report the observable failure below without guessing its cause.
            }
        }

        checks.Add(hasPlugin
            ? new HealthCheckItem("xunityPlugin", "XUnity 翻译插件", HealthStatus.Healthy,
                "已发现 XUnity.AutoTranslator 插件程序集。")
            : new HealthCheckItem("xunityPlugin", "XUnity 翻译插件", HealthStatus.Error,
                "未发现 XUnity.AutoTranslator 插件程序集。"));
    }

    private static async Task CheckTranslatorConfigAsync(
        List<HealthCheckItem> checks,
        Game game,
        string expectedUrl,
        string expectedDiscoveryFile,
        CancellationToken ct)
    {
        var path = Path.Combine(game.GamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
        if (!File.Exists(path))
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Error,
                "尚未发现 AutoTranslatorConfig.ini。"));
            return;
        }

        string content;
        try
        {
            content = await ReadSharedTextAsync(path, ct);
        }
        catch
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Warning,
                "AutoTranslatorConfig.ini 存在，但当前无法以共享读取方式读取。"));
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Error,
                "AutoTranslatorConfig.ini 存在但内容为空。"));
            return;
        }

        checks.Add(new("translatorConfig", "翻译配置文件", HealthStatus.Healthy,
            "已发现非空的 AutoTranslatorConfig.ini。"));

        var endpoint = ReadIniValue(content, "Service", "Endpoint");
        var toolkitUrl = ReadIniValue(content, "LLMTranslate", "ToolkitUrl");
        var discoveryFile = ReadIniValue(content, "LLMTranslate", "DiscoveryFile");
        var gameId = ReadIniValue(content, "LLMTranslate", "GameId");
        var mismatches = new List<string>();
        if (!string.Equals(endpoint, "LLMTranslate", StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"Service.Endpoint 当前为 {endpoint ?? "未配置"}，预期为 LLMTranslate");
        if (!string.Equals(toolkitUrl?.TrimEnd('/'), expectedUrl, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"LLMTranslate.ToolkitUrl 当前为 {toolkitUrl ?? "未配置"}，预期为 {expectedUrl}");
        if (!string.Equals(discoveryFile, expectedDiscoveryFile, StringComparison.OrdinalIgnoreCase))
            mismatches.Add("LLMTranslate.DiscoveryFile 未指向当前工具箱运行时发现文件");
        if (!string.Equals(gameId, game.Id, StringComparison.OrdinalIgnoreCase))
            mismatches.Add("LLMTranslate.GameId 与当前游戏记录不一致");

        checks.Add(mismatches.Count == 0
            ? new HealthCheckItem("translatorRouting", "翻译端点配置", HealthStatus.Healthy,
                "Endpoint、ToolkitUrl、DiscoveryFile 和 GameId 与当前工具箱配置一致。")
            : new HealthCheckItem("translatorRouting", "翻译端点配置", HealthStatus.Error,
                string.Join("；", mismatches) + "。"));
    }

    private void CheckPluginConnection(List<HealthCheckItem> checks, string gameId)
    {
        var latest = connectionRegistry.GetLatest(gameId);
        if (latest is not null && latest.LastSeenAtUtc >= DateTime.UtcNow - PluginConnectionRegistry.ConnectedTtl)
        {
            var version = string.IsNullOrWhiteSpace(latest.EndpointVersion)
                ? "未知版本"
                : latest.EndpointVersion;
            checks.Add(new(
                "toolboxHeartbeat",
                "翻译端点心跳",
                HealthStatus.Healthy,
                $"最近心跳 {latest.LastSeenAtUtc.ToLocalTime():HH:mm:ss}；DLL {version}；" +
                $"自动发现{(latest.DiscoveryEnabled ? "已启用" : "未启用")}；" +
                $"本机直连{(latest.DirectConnection ? "已启用" : "未确认")}。"));
            return;
        }

        if (latest is not null)
        {
            checks.Add(new(
                "toolboxHeartbeat",
                "翻译端点心跳",
                HealthStatus.Warning,
                $"最后心跳为 {latest.LastSeenAtUtc.ToLocalTime():HH:mm:ss}，当前已超过 30 秒。"));
            return;
        }

        checks.Add(runtimeEndpoint.LoopbackSelfTestSucceeded
            ? new HealthCheckItem(
                "toolboxHeartbeat",
                "翻译端点心跳",
                HealthStatus.Unknown,
                "工具箱自身环回直连正常，但尚未收到该游戏心跳；若游戏正在运行，请检查 DLL 版本、加速器的环回绕过、TUN 严格路由或安全软件规则。")
            : new HealthCheckItem(
                "toolboxHeartbeat",
                "翻译端点心跳",
                HealthStatus.Error,
                "工具箱本机环回自检失败，请先检查端口占用、安全软件或网络过滤驱动。"));
    }

    private void CheckTranslatorEndpointPackage(List<HealthCheckItem> checks, Game game)
    {
        var status = xUnityInstaller.GetTranslatorEndpointStatus(game);
        checks.Add(CreateTranslatorEndpointVersionCheck(status));
    }

    internal static HealthCheckItem CreateTranslatorEndpointVersionCheck(TranslatorEndpointStatus status)
    {
        var version = string.IsNullOrWhiteSpace(status.Version) ? "未知" : status.Version;
        return status.Origin switch
        {
            TranslatorEndpointOrigin.OfficialCurrent => new HealthCheckItem(
                "translatorEndpointVersion",
                "AI 翻译端点版本",
                HealthStatus.Healthy,
                $"官方 DLL {version}；支持自动发现；本机直连模式已内置。"),
            TranslatorEndpointOrigin.CompatibleCurrent => new HealthCheckItem(
                "translatorEndpointVersion",
                "AI 翻译端点版本",
                HealthStatus.Healthy,
                $"同版兼容 DLL {version}；版本和结构与当前端点一致，SHA-256 未列入官方清单；文件已保留并支持自动发现与本机直连。"),
            TranslatorEndpointOrigin.OfficialOutdated => new HealthCheckItem(
                "translatorEndpointVersion",
                "AI 翻译端点版本",
                HealthStatus.Warning,
                status.UpdatePending
                    ? $"旧官方 DLL {version}；游戏运行中，升级已延后。"
                    : $"旧官方 DLL {version}；退出游戏后可自动升级。"),
            TranslatorEndpointOrigin.UnknownOrCustom => new HealthCheckItem(
                "translatorEndpointVersion",
                "AI 翻译端点版本",
                HealthStatus.Warning,
                $"DLL {version} 的 SHA-256 不属于官方清单；已保留文件，无法确认自动发现与直连支持。"),
            _ => new HealthCheckItem(
                "translatorEndpointVersion",
                "AI 翻译端点版本",
                HealthStatus.Error,
                "未安装 AI 翻译端点 DLL。")
        };
    }

    private void CheckToolkitRuntime(List<HealthCheckItem> checks)
    {
        if (!runtimeEndpoint.IsStarted)
        {
            checks.Add(new HealthCheckItem(
                "toolboxRuntimeEndpoint",
                "工具箱运行端点",
                HealthStatus.Unknown,
                $"运行端点尚未就绪；首选端口为 {runtimeEndpoint.PreferredPort}。"));
            return;
        }

        var fallback = runtimeEndpoint.UsedFallback
            ? $"；首选端口 {runtimeEndpoint.PreferredPort} 不可用，已自动回退（{runtimeEndpoint.FallbackReason}）"
            : string.Empty;
        checks.Add(new HealthCheckItem(
            "toolboxRuntimeEndpoint",
            "工具箱运行端点",
            runtimeEndpoint.LoopbackSelfTestSucceeded ? HealthStatus.Healthy : HealthStatus.Error,
            $"实际地址 {runtimeEndpoint.BaseUrl}{fallback}；禁用代理的环回自检" +
            (runtimeEndpoint.LoopbackSelfTestSucceeded
                ? "已通过。"
                : $"失败：{runtimeEndpoint.LoopbackSelfTestError ?? "未知原因"}。")));
    }

    private static async Task<string> ReadSharedTextAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static string? ReadIniValue(string content, string section, string key)
    {
        string? currentSection = null;
        foreach (var rawLine in content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            if (string.Equals(line[..separator].Trim(), key, StringComparison.OrdinalIgnoreCase))
                return line[(separator + 1)..].Trim();
        }

        return null;
    }

    private static HealthStatus DetermineObjectiveOverall(IEnumerable<HealthCheckItem> checks)
    {
        var items = checks.ToList();
        if (items.Any(check => check.Status == HealthStatus.Error))
            return HealthStatus.Error;
        if (items.Any(check => check.Status == HealthStatus.Warning))
            return HealthStatus.Warning;
        if (items.Any(check => check.Status == HealthStatus.Unknown))
            return HealthStatus.Unknown;
        return HealthStatus.Healthy;
    }

    private static bool IsFreshLog(string path, DateTime since)
    {
        try
        {
            return File.Exists(path) && File.GetLastWriteTimeUtc(path) >= since.AddSeconds(-1);
        }
        catch
        {
            return false;
        }
    }
}
