using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class InstallOrchestrator(
    GameLibraryService gameLibrary,
    UnityDetectionService detection,
    BepInExInstallerService bepInExInstaller,
    XUnityInstallerService xUnityInstaller,
    TmpFontService tmpFontService,
    ConfigurationService configService,
    AppSettingsService appSettingsService,
    AssetExtractionService assetExtraction,
    BundledAssetPaths bundledPaths,
    AppDataPaths appDataPaths,
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

    public async Task<InstallationStatus> StartInstallAsync(string gameId, XUnityConfig? config = null)
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
                    await ExecuteInstallAsync(game, status, config, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    await UpdateStatus(status, InstallStep.Failed, 0, error: "Installation cancelled.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "安装失败: 游戏 {GameId}", gameId);
                    await UpdateStatus(status, InstallStep.Failed, 0, error: ex.Message);
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteUninstallAsync(game, status);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "卸载失败: 游戏 {GameId}", gameId);
                    await UpdateStatus(status, InstallStep.Failed, 0, error: ex.Message);
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
        Game game, InstallationStatus status, XUnityConfig? config, CancellationToken ct)
    {
        // Step 1: Detect game
        await UpdateStatus(status, InstallStep.DetectingGame, 5, "Detecting Unity game info...");

        if (game.DetectedInfo is null)
        {
            var info = await detection.DetectAsync(game.GamePath, ct);
            game.DetectedInfo = info;
            game.ExecutableName = info.DetectedExecutable;
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

        // Step 4: Install TMP font
        await UpdateStatus(status, InstallStep.InstallingTmpFont, 66, "正在安装 TMP 字体...");
        try
        {
            if (tmpFontService.InstallFont(game.GamePath, gameInfo))
                await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体已安装");
            else
                await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体不可用（跳过）");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TMP 字体安装失败（不影响安装）");
            await UpdateStatus(status, InstallStep.InstallingTmpFont, 68, "TMP 字体安装失败（跳过）");
        }

        // Step 5: Deploy LLMTranslate translator endpoint DLL
        await UpdateStatus(status, InstallStep.InstallingAiTranslation, 70, "正在部署 AI 翻译端点...");
        if (xUnityInstaller.DeployTranslatorEndpoint(game.GamePath))
            await UpdateStatus(status, InstallStep.InstallingAiTranslation, 72, "AI 翻译端点已部署");
        else
            await UpdateStatus(status, InstallStep.InstallingAiTranslation, 72, "AI 翻译端点不可用（跳过）");

        // Step 6: Launch game to generate config file
        await UpdateStatus(status, InstallStep.GeneratingConfig, 73, "正在启动游戏以生成配置文件...");

        var configPath = configService.GetConfigPath(game.GamePath);
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
            if (gameProcess != null && !gameProcess.HasExited)
            {
                try
                {
                    gameProcess.Kill();
                    gameProcess.WaitForExit(5000);
                    logger.LogInformation("已关闭游戏进程");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "关闭游戏进程失败");
                }
            }
            gameProcess?.Dispose();
        }

        // Step 7: Apply optimal defaults and user config
        await UpdateStatus(status, InstallStep.ApplyingConfig, 86, "正在应用最佳默认配置...");
        await configService.ApplyOptimalDefaultsAsync(game.GamePath, ct);

        if (config != null)
        {
            await UpdateStatus(status, InstallStep.ApplyingConfig, 89, "正在应用用户配置...");
            await configService.PatchAsync(game.GamePath, config, ct);
        }

        await UpdateStatus(status, InstallStep.ApplyingConfig, 92, "配置应用完成");

        // Patch LLMTranslate section with GameId and ToolkitUrl
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
        await UpdateStatus(status, InstallStep.ExtractingAssets, 93, "正在提取游戏资产以检测语言...");
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
                await UpdateStatus(status, InstallStep.ExtractingAssets, 99,
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
                await File.WriteAllTextAsync(cachePath, json, ct);
                logger.LogInformation("安装时提取的文本已缓存: {Count} 条, 游戏 {GameId}",
                    extractResult.TotalTextsExtracted, game.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "资产提取失败（不影响安装）");
            await UpdateStatus(status, InstallStep.ExtractingAssets, 99, "资产提取失败（跳过）");
        }

        // Step 9: Complete
        game.InstalledXUnityVersion = installedXUnityVersion;
        game.InstallState = InstallState.FullyInstalled;
        await gameLibrary.UpdateAsync(game, ct);

        await UpdateStatus(status, InstallStep.Complete, 100, "Installation complete!");
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
