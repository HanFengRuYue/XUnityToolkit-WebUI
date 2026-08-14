using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Upgrades only SHA-256-confirmed official translator endpoints and refreshes their discovery settings.
/// Unknown/custom DLLs are deliberately never modified by this background pass.
/// </summary>
public sealed class TranslatorEndpointUpgradeService(
    GameLibraryService gameLibrary,
    XUnityInstallerService installer,
    ConfigurationService configuration,
    ILogger<TranslatorEndpointUpgradeService> logger)
{
    public async Task UpgradeManagedGamesAsync(
        CancellationToken ct,
        bool refreshCurrentConfigurations = false)
    {
        var games = await gameLibrary.GetAllAsync(ct);
        foreach (var game in games.Where(game => game.InstallState == InstallState.FullyInstalled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var status = installer.GetTranslatorEndpointStatus(game);
                var wasOutdated = status.Origin == TranslatorEndpointOrigin.OfficialOutdated;
                if (wasOutdated)
                    status = installer.EnsureTranslatorEndpoint(game);

                if ((status.Origin is TranslatorEndpointOrigin.OfficialCurrent or TranslatorEndpointOrigin.CompatibleCurrent)
                    && (refreshCurrentConfigurations || wasOutdated)
                    && File.Exists(configuration.GetConfigPath(game.GamePath)))
                {
                    await configuration.PatchTranslatorEndpointAsync(game.GamePath, game.Id, ct);
                }

                if (status.UpdatePending)
                    logger.LogDebug("游戏 {Game} 正在运行，官方 AI 翻译端点将在稍后升级", game.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "检查游戏 {Game} 的 AI 翻译端点升级失败", game.Name);
            }
        }
    }
}
