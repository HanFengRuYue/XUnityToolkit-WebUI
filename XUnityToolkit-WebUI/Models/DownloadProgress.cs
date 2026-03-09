namespace XUnityToolkit_WebUI.Models;

public sealed record DownloadProgress(
    int Percent,
    string? SpeedFormatted = null,
    string? RetryMessage = null);
