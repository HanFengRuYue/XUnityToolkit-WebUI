using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using UnityLocalizationToolkit_WebUI.Hubs;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

public sealed class InstallOrchestrator(
    GameLibraryService gameLibrary,
    UnityDetectionService detection,
    BepInExInstallerService bepInExInstaller,
    XUnityInstallerService xUnityInstaller,
    TmpFontService tmpFontService,
    ConfigurationService configService,
    AppSettingsService appSettingsService,
    AssetExtractionService assetExtraction,
    PluginHealthCheckService healthCheckService,
    BundledAssetPaths bundledPaths,
    AppDataPaths appDataPaths,
    SystemTrayService trayService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<InstallOrchestrator> logger)
{
    private readonly ConcurrentDictionary<string, InstallationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

    public bool HasActiveOperation => _statuses.Values.Any(s =>
        s.Step is not (InstallStep.Idle or InstallStep.Complete or InstallStep.Failed));

    public InstallationStatus GetStatus(string gameId)
    {
        return _statuses.GetOrAdd(gameId, id => new InstallationStatus { GameId = id });
    }

    public async Task<InstallationStatus> StartInstallAsync(string gameId, XUnityConfig? config = null, InstallOptions? options = null)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync();
        try
        {
            var game = await gameLibrary.GetByIdAsync(gameId)
                ?? throw new KeyNotFoundException($"Game {gameId} not found.");

            var status = GetStatus(gameId);
            if (status.Step is not InstallStep.Idle and not InstallStep.Complete and not InstallStep.Failed)
                throw new InvalidOperationException("Installation already in progress.");

            status.Step = InstallStep.Idle;
            status.ProgressPercent = 0;
            status.Error = null;
            status.Message = null;

            var cts = new CancellationTokenSource();
            if (_cancellations.TryRemove(gameId, out var oldCts))
                oldCts.Dispose();
            _cancellations[gameId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteInstallAsync(game, status, config, options ?? new InstallOptions(), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    await UpdateStatus(status, InstallStep.Failed, 0, error: "Installation cancelled.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "安装失败: 游戏 {GameId}", gameId);
                    await UpdateStatus(status, InstallStep.Failed, 0, error: "安装过程中发生内部错误，请查看日志获取详情");
                }
                finally
                {
                    if (_cancellations.TryRemove(gameId, out var doneCts))
                        doneCts.Dispose();
                }
            });

            return status;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task<InstallationStatus> StartUninstallAsync(string gameId)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync();
        try
        {
            var game = await gameLibrary.GetByIdAsync(gameId)
                ?? throw new KeyNotFoundException($"Game {gameId} not found.");

            var status = GetStatus(gameId);
            if (status.Step is not InstallStep.Idle and not InstallStep.Complete and not InstallStep.Failed)
                throw new InvalidOperationException("Operation already in progress.");

            status.Step = InstallStep.Idle;
            status.ProgressPercent = 0;
            status.Error = null;
            status.Message = null;

            var cts = new CancellationTokenSource();
            if (_cancellations.TryRemove(gameId, out var oldCts))
                oldCts.Dispose();
            _cancellations[gameId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteUninstallAsync(game, status);
                }
                catch (OperationCanceledException)
                {
                    await UpdateStatus(status, InstallStep.Failed, 0, error: "Uninstallation cancelled.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "卸载失败: 游戏 {GameId}", gameId);
                    await UpdateStatus(status, InstallStep.Failed, 0, error: "卸载过程中发生内部错误，请查看日志获取详情");
                }
                finally
                {
                    if (_cancellations.TryRemove(gameId, out var doneCts))
                        doneCts.Dispose();
                }
            });

            return status;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public void Cancel(string gameId)
    {
        if (_cancellations.TryGetValue(gameId, out var cts))
            cts.Cancel();
    }

    private async Task ExecuteInstallAsync(
        Game game, InstallationStatus status, XUnityConfig? config, InstallOptions options, CancellationToken ct)
    {
        // Step 1: Detect game (always re-detect to ensure latest info, e.g. HasTextMeshPro)
        await UpdateStatus(status, InstallStep.DetectingGame, 5, "Detecting Unity game info...");

        {
            var info = await detection.DetectAsync(game.GamePath, ct);
            game.DetectedInfo = info;
            game.ExecutableName ??= info.DetectedExecutable;
            await gameLibrary.UpdateAsync(game, ct);
        }

        var gameInfo = game.DetectedInfo!;
        var usesBepInEx6 = gameInfo.Backend == UnityBackend.IL2CPP;

        // Step 2: Install BepInEx (from bundled ZIP)
        await UpdateStatus(status, InstallStep.InstallingBepInEx, 10, "正在安装 BepInEx...");

        var bepInExZip = bepInExInstaller.ResolveBundledZip(gameInfo, bundledPaths);
        var installedBepInExVersion = BepInExInstallerService.ParseVersionFromZip(bepInExZip);

        await bepInExInstaller.InstallAsync(game.GamePath, bepInExZip, ct);
        await UpdateStatus(status, InstallStep.InstallingBepInEx, 35, "BepInEx 安装完成");

        game.InstalledBepInExVersion = installedBepInExVersion;
        game.InstallState = InstallState.BepInExOnly;
        await gameLibrary.UpdateAsync(game, ct);

        // Step 3: Install XUnity.AutoTranslator (from bundled ZIP)
        await UpdateStatus(status, InstallStep.InstallingXUnity, 40, "正在安装 XUnity.AutoTranslator...");

        var xUnityZip = xUnityInstaller.ResolveBundledZip(gameInfo, bundledPaths);
        var installedXUnityVersion = XUnityInstallerService.ParseVersionFromZip(xUnityZip);

        await xUnityInstaller.InstallAsync(game.GamePath, xUnityZip, ct);
        await UpdateStatus(status, InstallStep.InstallingXUnity, 65, "XUnity.AutoTranslator 安装完成");

        // Step 4: Install TMP font (skip if game doesn't use TextMeshPro)
        await UpdateStatus(status, InstallStep.InstallingTmpFont, 66, "正在安装 TMP 字体...");
        string? tmpFontConfigValue = null;
        if (!options.AutoInstallTmpFont)
        {
            logger.LogInformation("用户已关闭自动安装 TMP 字体");
            await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "自动安装 TMP 字体（已跳过）");
        }
        else if (gameInfo.HasTextMeshPro == false)
        {
            logger.LogInformation("游戏不使用 TextMeshPro，跳过 TMP 字体安装");
            await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "游戏不使用 TextMeshPro（跳过）");
        }
        else
        {
            try
            {
                tmpFontConfigValue = tmpFontService.InstallFont(game.GamePath, gameInfo);
                if (tmpFontConfigValue != null)
                    await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体已安装");
                else
                    await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体不可用（跳过）");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TMP 字体安装失败（不影响安装）");
                await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体安装失败（跳过）");
            }
        }

        // Step 5: Deploy LLMTranslate translator endpoint DLL
        await UpdateStatus(status, InstallStep.InstallingAiTranslation, 70, "正在部署 AI 翻译端点...");
        if (!options.AutoDeployAiEndpoint)
        {
            logger.LogInformation("用户已关闭自动部署 AI 翻译端点");
            await UpdateStatus(status, InstallStep.InstallingAiTranslation, 72, "部署 AI 翻译端点（已跳过）");
        }
        else if (xUnityInstaller.DeployTranslatorEndpoint(game.GamePath))
            await UpdateStatus(status, InstallStep.InstallingAiTranslation, 72, "AI 翻译端点已部署");
        else
            await UpdateStatus(status, InstallStep.InstallingAiTranslation, 72, "AI 翻译端点不可用（跳过）");

        // Step 6: Launch game to generate config file
        var configPath = configService.GetConfigPath(game.GamePath);
        if (!options.AutoGenerateConfig)
        {
            await UpdateStatus(status, InstallStep.GeneratingConfig, 85, "启动游戏生成配置（已跳过）");
            logger.LogInformation("用户已关闭自动启动游戏生成配置");
        }
        else
        {
            await UpdateStatus(status, InstallStep.GeneratingConfig, 73, "正在启动游戏以生成配置文件...");

            var exeName = game.ExecutableName ?? game.DetectedInfo?.DetectedExecutable
                ?? throw new InvalidOperationException("无法确定游戏可执行文件路径。");
            var exePath = Path.GetFullPath(Path.Combine(game.GamePath, exeName));
            var normalizedGamePath = Path.GetFullPath(game.GamePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!exePath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("可执行文件路径不在游戏目录内。");

            Process? gameProcess = null;
            try
            {
                gameProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = game.GamePath,
                    UseShellExecute = true
                });

                var timeout = TimeSpan.FromSeconds(60);
                var elapsed = TimeSpan.Zero;
                var interval = TimeSpan.FromSeconds(1);

                while (!File.Exists(configPath) && elapsed < timeout)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(interval, ct);
                    elapsed += interval;
                    await UpdateStatus(status, InstallStep.GeneratingConfig,
                        73 + (int)(elapsed.TotalSeconds / timeout.TotalSeconds * 12),
                        $"等待配置文件生成... ({(int)elapsed.TotalSeconds}s)");
                }

                if (File.Exists(configPath))
                    logger.LogInformation("配置文件已生成: {Path}", configPath);
                else
                    logger.LogWarning("配置文件生成超时，将在下次启动游戏时自动生成");
            }
            finally
            {
                // UseShellExecute=true may return a shell launcher (e.g. Steam) that exits
                // immediately while the real game runs separately. Kill by process name as fallback.
                await GameProcessHelper.KillGameProcessAsync(gameProcess, exeName, game.GamePath, logger);
            }
        }

        // Step 7: Apply optimal defaults and user config
        if (!options.AutoApplyOptimalConfig)
        {
            await UpdateStatus(status, InstallStep.ApplyingConfig, 92, "应用最佳配置（已跳过）");
            logger.LogInformation("用户已关闭自动应用最佳配置");
        }
        else if (!File.Exists(configPath))
        {
            await UpdateStatus(status, InstallStep.ApplyingConfig, 92, "配置文件不存在，无法应用配置（跳过）");
            logger.LogWarning("配置文件不存在，跳过应用最佳配置");
        }
        else
        {
            await UpdateStatus(status, InstallStep.ApplyingConfig, 86, "正在应用最佳默认配置...");
            await configService.ApplyOptimalDefaultsAsync(game.GamePath, ct);

            // Patch TMP font config value (must happen after defaults, since defaults no longer set it)
            if (tmpFontConfigValue != null)
            {
                await configService.PatchSectionAsync(game.GamePath, "Behaviour",
                    new Dictionary<string, string>
                    {
                        ["FallbackFontTextMeshPro"] = tmpFontConfigValue
                    }, ct);
            }

            if (config != null)
            {
                await UpdateStatus(status, InstallStep.ApplyingConfig, 89, "正在应用用户配置...");
                await configService.PatchAsync(game.GamePath, config, ct);
            }

            await UpdateStatus(status, InstallStep.ApplyingConfig, 92, "配置应用完成");

            // Apply pre-translation cache optimization config if enabled
            {
                var aiSettings = await appSettingsService.GetAsync(ct);
                if (aiSettings.AiTranslation.EnablePreTranslationCache)
                {
                    await configService.PatchSectionAsync(game.GamePath, "Behaviour", new Dictionary<string, string>
                    {
                        ["CacheWhitespaceDifferences"] = "False",
                        ["IgnoreWhitespaceInDialogue"] = "True",
                        ["MinDialogueChars"] = "4",
                        ["TemplateAllNumberAway"] = "True"
                    }, ct);
                }
            }
        }

        // Patch LLMTranslate section with GameId and ToolkitUrl (independent of optimal config —
        // required for AI endpoint to work correctly)
        if (File.Exists(configPath) && xUnityInstaller.IsTranslatorEndpointInstalled(game.GamePath))
        {
            var appSettings = await appSettingsService.GetAsync(ct);
            var port = appSettings.AiTranslation.Port;
            await configService.PatchSectionAsync(game.GamePath, "LLMTranslate",
                new Dictionary<string, string>
                {
                    ["ToolkitUrl"] = $"http://127.0.0.1:{port}",
                    ["GameId"] = game.Id
                }, ct);
        }

        // Step 8: Extract game assets for language detection
        if (!options.AutoExtractAssets)
        {
            await UpdateStatus(status, InstallStep.ExtractingAssets, 93, "提取游戏资产（已跳过）");
            logger.LogInformation("用户已关闭自动提取游戏资产");
        }
        else
        {
            await UpdateStatus(status, InstallStep.ExtractingAssets, 90, "正在提取游戏资产以检测语言...");
            try
            {
                var extractResult = await assetExtraction.ExtractTextsAsync(
                    game.GamePath, game.ExecutableName, gameInfo, ct: ct);

                if (extractResult.DetectedLanguage is not null)
                {
                    await configService.PatchSectionAsync(game.GamePath, "General",
                        new Dictionary<string, string>
                        {
                            ["FromLanguage"] = extractResult.DetectedLanguage
                        }, ct);
                    await UpdateStatus(status, InstallStep.ExtractingAssets, 93,
                        $"检测到游戏语言: {extractResult.DetectedLanguage} ({extractResult.TotalTextsExtracted} 条文本)");
                    logger.LogInformation("游戏语言检测完成: {Lang}, 提取 {Count} 条文本",
                        extractResult.DetectedLanguage, extractResult.TotalTextsExtracted);
                }

                var cachePath = appDataPaths.ExtractedTextsFile(game.Id);
                if (!File.Exists(cachePath) && extractResult.Texts.Count > 0)
                {
                    extractResult.GameId = game.Id;
                    var json = JsonSerializer.Serialize(extractResult, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    var tmpPath = cachePath + ".tmp";
                    await File.WriteAllTextAsync(tmpPath, json, CancellationToken.None);
                    File.Move(tmpPath, cachePath, overwrite: true);
                    logger.LogInformation("安装时提取的文本已缓存: {Count} 条, 游戏 {GameId}",
                        extractResult.TotalTextsExtracted, game.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "资产提取失败（不影响安装）");
                await UpdateStatus(status, InstallStep.ExtractingAssets, 93, "资产提取失败（跳过）");
            }
        }

        // Mark as installed before verification — cancellation during verify preserves install state
        game.InstalledXUnityVersion = installedXUnityVersion;
        game.InstallState = InstallState.FullyInstalled;
        await gameLibrary.UpdateAsync(game, ct);

        // Step 9: Verify plugin health
        PluginHealthReport? healthReport = null;
        if (!options.AutoVerifyHealth)
        {
            await UpdateStatus(status, InstallStep.VerifyingHealth, 99, "验证插件状态（已跳过）");
            logger.LogInformation("用户已关闭自动验证插件状态");
        }
        else
        {
            await UpdateStatus(status, InstallStep.VerifyingHealth, 93, "正在验证插件安装状态...");
            try
            {
                healthReport = await healthCheckService.VerifyForInstallAsync(game, ct);

                if (healthReport.Overall == HealthStatus.Healthy)
                {
                    await UpdateStatus(status, InstallStep.VerifyingHealth, 99, "插件验证通过，所有检查项正常");
                    logger.LogInformation("安装验证通过，游戏 {GameId}", game.Id);
                }
                else
                {
                    var problemCount = healthReport.Checks.Count(c => c.Status != HealthStatus.Healthy);
                    await UpdateStatus(status, InstallStep.VerifyingHealth, 99,
                        $"验证完成，发现 {problemCount} 项问题（不影响安装）");
                    logger.LogWarning("安装验证发现 {Count} 项问题，游戏 {GameId}", problemCount, game.Id);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "插件验证失败（不影响安装），游戏 {GameId}", game.Id);
                await UpdateStatus(status, InstallStep.VerifyingHealth, 99, "插件验证失败（跳过）");
            }

            // Broadcast health report via SignalR for PluginHealthCard
            if (healthReport != null)
            {
                try
                {
                    await hubContext.Clients.Group($"game-{game.Id}")
                        .SendAsync("healthReportReady", healthReport);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "健康报告推送失败: 游戏 {GameId}", game.Id);
                }
            }
        }

        // Step 10: Complete
        await UpdateStatus(status, InstallStep.Complete, 100, "Installation complete!");
        trayService.ShowNotification("安装完成", $"「{game.Name}」插件安装完成");
    }

    private async Task ExecuteUninstallAsync(Game game, InstallationStatus status)
    {
        await UpdateStatus(status, InstallStep.RemovingXUnity, 20, "Removing XUnity.AutoTranslator...");
        await xUnityInstaller.UninstallAsync(game.GamePath);

        await UpdateStatus(status, InstallStep.RemovingBepInEx, 50, "Removing BepInEx...");
        await bepInExInstaller.UninstallAsync(game.GamePath);

        game.InstallState = InstallState.NotInstalled;
        game.InstalledBepInExVersion = null;
        game.InstalledXUnityVersion = null;
        await gameLibrary.UpdateAsync(game);

        await UpdateStatus(status, InstallStep.Complete, 100, "Uninstall complete!");
    }

    private async Task UpdateStatus(InstallationStatus status, InstallStep step, int percent,
        string? message = null, string? error = null)
    {
        status.Step = step;
        status.ProgressPercent = percent;
        status.Message = message;
        status.Error = error;
        await BroadcastStatus(status);
    }

    private async Task BroadcastStatus(InstallationStatus status)
    {
        try
        {
            await hubContext.Clients.Group($"game-{status.GameId}")
                .SendAsync("progressUpdate", status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "状态推送失败: 游戏 {GameId}", status.GameId);
        }
    }
}
