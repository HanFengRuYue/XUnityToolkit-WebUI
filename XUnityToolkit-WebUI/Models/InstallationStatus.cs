namespace XUnityToolkit_WebUI.Models;

public enum InstallStep
{
    Idle,
    DetectingGame,
    DownloadingBepInEx,
    InstallingBepInEx,
    DownloadingXUnity,
    InstallingXUnity,
    GeneratingConfig,
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
    public string? DownloadSpeed { get; set; }
    public string? RetryMessage { get; set; }
}
