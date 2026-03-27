using System.Reflection;

namespace XUnityToolkit_WebUI.Infrastructure;

public enum AppEdition { Full, NoLlama, Lite }

/// <summary>
/// Reads the app edition from build-time assembly metadata.
/// Injected via <c>-p:Edition=full|no-llama|lite</c> at publish time.
/// </summary>
public static class EditionInfo
{
    public static AppEdition Current { get; } = ParseEdition();

    /// <summary>Whether this edition bundles llama.cpp binaries.</summary>
    public static bool HasBundledLlama => Current is AppEdition.Full;

    private static AppEdition ParseEdition()
    {
        var attr = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Edition");
        return attr?.Value switch
        {
            "no-llama" => AppEdition.NoLlama,
            "lite" => AppEdition.Lite,
            _ => AppEdition.Full
        };
    }
}
