namespace UnityLocalizationToolkit_WebUI.Models;

public sealed class BackupEntry
{
    public required string OriginalRelativePath { get; init; }
    public required string BackupFileName { get; init; }
}

public sealed class BackupManifest
{
    public required string GameId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<string> InstalledFiles { get; init; } = [];
    public List<string> InstalledDirectories { get; init; } = [];
    public List<BackupEntry> BackedUpFiles { get; init; } = [];
}
