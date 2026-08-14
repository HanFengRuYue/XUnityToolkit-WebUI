using BepInEx;

namespace XUnityToolkit.RuntimeFontLoader
{
    [BepInPlugin(RuntimeFontBootstrap.PluginId, RuntimeFontBootstrap.PluginName, RuntimeFontBootstrap.PluginVersion)]
    [BepInDependency(RuntimeFontBootstrap.XUnityPluginId, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class RuntimeFontLoaderPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            RuntimeFontBootstrap.Initialize(
                Paths.GameRootPath,
                Paths.ConfigPath,
                message => Logger.LogInfo(message),
                message => Logger.LogWarning(message),
                message => Logger.LogError(message));
        }
    }
}
