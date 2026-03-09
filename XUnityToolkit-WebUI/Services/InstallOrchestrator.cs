using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class InstallOrchestrator(
    GameLibraryService gameLibrary,
    UnityDetectionService detection,
    GitHubReleaseService gitHub,
    BepInExInstallerService bepInExInstaller,
    XUnityInstallerService xUnityInstaller,
    ConfigurationService configService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<InstallOrchestrator> logger)
{
    private readonly ConcurrentDictionary<string, InstallationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

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
            status.DownloadSpeed = null;
            status.RetryMessage = null;

            var cts = new CancellationTokenSource();
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
                    logger.LogError(ex, "Installation failed for game {GameId}", gameId);
                    await UpdateStatus(status, InstallStep.Failed, 0, error: ex.Message);
                }
                finally
                {
                    _cancellations.TryRemove(gameId, out _);
                }
            }, cts.Token);

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
            status.DownloadSpeed = null;
            status.RetryMessage = null;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteUninstallAsync(game, status);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Uninstall failed for game {GameId}", gameId);
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

        // Step 2: Download BepInEx
        await UpdateStatus(status, InstallStep.DownloadingBepInEx, 10, "Fetching BepInEx releases...");

        var bepInExReleases = await gitHub.GetReleasesAsync("BepInEx", "BepInEx", ct);
        var bepInExRelease = usesBepInEx6
            ? bepInExReleases.FirstOrDefault(r => r.TagName.StartsWith("v6"))
            : bepInExReleases.FirstOrDefault(r => !r.Prerelease && r.TagName.StartsWith("v5"));

        if (bepInExRelease is null)
            throw new InvalidOperationException("No suitable BepInEx release found.");

        var bepInExAsset = bepInExInstaller.ResolveAsset(gameInfo, bepInExRelease.Assets, usesBepInEx6)
            ?? throw new InvalidOperationException(
                $"No compatible BepInEx build found for {gameInfo.Backend} {gameInfo.Architecture}.");

        await UpdateStatus(status, InstallStep.DownloadingBepInEx, 15,
            $"Downloading {bepInExAsset.Name}...");

        var bepProgress = MakeDownloadProgress(status, basePercent: 15, rangePercent: 25);

        var bepInExZip = await gitHub.DownloadAssetAsync(
            bepInExAsset.BrowserDownloadUrl, bepInExAsset.Name, bepProgress, ct);

        // Step 3: Install BepInEx
        await UpdateStatus(status, InstallStep.InstallingBepInEx, 40, "Installing BepInEx...");

        await bepInExInstaller.InstallAsync(game.GamePath, bepInExZip, ct);

        game.InstalledBepInExVersion = bepInExRelease.TagName;
        game.InstallState = InstallState.BepInExOnly;
        await gameLibrary.UpdateAsync(game, ct);

        // Step 4: Download XUnity.AutoTranslator
        await UpdateStatus(status, InstallStep.DownloadingXUnity, 50,
            "Fetching XUnity.AutoTranslator releases...");

        var xUnityRelease = await gitHub.GetLatestReleaseAsync(
            "bbepis", "XUnity.AutoTranslator", ct: ct)
            ?? throw new InvalidOperationException("No XUnity.AutoTranslator release found.");

        var xUnityAsset = xUnityInstaller.ResolveAsset(gameInfo, xUnityRelease.Assets)
            ?? throw new InvalidOperationException("No compatible XUnity.AutoTranslator build found.");

        await UpdateStatus(status, InstallStep.DownloadingXUnity, 55,
            $"Downloading {xUnityAsset.Name}...");

        var xProgress = MakeDownloadProgress(status, basePercent: 55, rangePercent: 25);

        var xUnityZip = await gitHub.DownloadAssetAsync(
            xUnityAsset.BrowserDownloadUrl, xUnityAsset.Name, xProgress, ct);

        // Step 5: Install XUnity.AutoTranslator
        await UpdateStatus(status, InstallStep.InstallingXUnity, 80, "Installing XUnity.AutoTranslator...");

        await xUnityInstaller.InstallAsync(game.GamePath, xUnityZip, ct);

        // Step 6: Write config
        await UpdateStatus(status, InstallStep.WritingConfig, 90, "Writing configuration...");

        var xUnityConfig = config ?? new XUnityConfig();
        await configService.SaveAsync(game.GamePath, xUnityConfig, ct);

        // Step 7: Complete
        game.InstalledXUnityVersion = xUnityRelease.TagName;
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

    private IProgress<DownloadProgress> MakeDownloadProgress(
        InstallationStatus status, int basePercent, int rangePercent)
    {
        return new Progress<DownloadProgress>(p =>
        {
            status.ProgressPercent = basePercent + p.Percent * rangePercent / 100;
            status.DownloadSpeed = p.SpeedFormatted;
            if (p.RetryMessage is not null)
                status.RetryMessage = p.RetryMessage;
            _ = BroadcastStatus(status);
        });
    }

    private async Task UpdateStatus(InstallationStatus status, InstallStep step, int percent,
        string? message = null, string? error = null)
    {
        status.Step = step;
        status.ProgressPercent = percent;
        status.Message = message;
        status.Error = error;
        status.DownloadSpeed = null;
        status.RetryMessage = null;
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
            logger.LogWarning(ex, "Failed to broadcast status for game {GameId}", status.GameId);
        }
    }
}
