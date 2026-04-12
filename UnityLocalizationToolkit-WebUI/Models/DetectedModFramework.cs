namespace UnityLocalizationToolkit_WebUI.Models;

public enum ModFrameworkType
{
    BepInEx,
    MelonLoader,
    IPA,
    ReiPatcher,
    Sybaris,
    UnityInjector,
    Standalone
}

public sealed class DetectedModFramework
{
    public required ModFrameworkType Framework { get; init; }
    public string? Version { get; init; }
    public bool HasXUnityPlugin { get; init; }
    public string? XUnityVersion { get; init; }
}
