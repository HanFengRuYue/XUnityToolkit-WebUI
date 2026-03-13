namespace XUnityToolkit_WebUI.Models;

public enum InstallStep
{
    Idle,
    DetectingGame,
    InstallingBepInEx,
    InstallingXUnity,
    InstallingTmpFont,
    InstallingAiTranslation,
    GeneratingConfig,
    ApplyingConfig,
    ExtractingAssets,
    RemovingXUnity,
    RemovingBepInEx,
    Complete,
    Failed
}

public sealed class InstallationStatus
{
    public required string GameId { get; init; }
    public InstallStep Step { get; set; } = InstallStep.Idle;
    public int ProgressPercent { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}
