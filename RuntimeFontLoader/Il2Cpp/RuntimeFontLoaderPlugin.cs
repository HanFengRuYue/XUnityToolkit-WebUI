using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace XUnityToolkit.RuntimeFontLoader
{
    [BepInPlugin(RuntimeFontBootstrap.PluginId, RuntimeFontBootstrap.PluginName, RuntimeFontBootstrap.PluginVersion)]
    [BepInDependency(RuntimeFontBootstrap.XUnityPluginId, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class RuntimeFontLoaderPlugin : BasePlugin
    {
        public override void Load()
        {
            RuntimeFontBootstrap.Initialize(
                Paths.GameRootPath,
                Paths.ConfigPath,
                message => Log.LogInfo(message),
                message => Log.LogWarning(message),
                message => Log.LogError(message));
        }
    }
}
