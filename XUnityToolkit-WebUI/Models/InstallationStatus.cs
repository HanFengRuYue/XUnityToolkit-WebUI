namespace XUnityToolkit_WebUI.Models;

public enum InstallStep
{
    Idle,
    DetectingGame,
    DownloadingBepInEx,
    InstallingBepInEx,
    DownloadingXUnity,
    InstallingXUnity,
    WritingConfig,
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
