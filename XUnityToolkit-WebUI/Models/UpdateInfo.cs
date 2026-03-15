namespace XUnityToolkit_WebUI.Models;

using System.Text.Json.Serialization;

public sealed class ManifestFileEntry
{
    public required string Hash { get; set; }
    public long Size { get; set; }
    public required string Package { get; set; }
}

public sealed class UpdateManifest
{
    public required string Version { get; set; }
    public required string Rid { get; set; }
    public Dictionary<string, ManifestFileEntry> Files { get; set; } = [];
}

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string? NewVersion { get; set; }
    public string? Changelog { get; set; }
    public long DownloadSize { get; set; }
    public List<string> ChangedPackages { get; set; } = [];
    public int ChangedFileCount { get; set; }
    public int DeletedFileCount { get; set; }
}

public enum UpdateState
{
    None,
    Checking,
    Available,
    Downloading,
    Ready,
    Applying,
    Error
}

public sealed class UpdateStatusInfo
{
    public UpdateState State { get; set; }
    public double Progress { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public string? CurrentPackage { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public sealed class UpdateAvailableInfo
{
    public required string Version { get; set; }
    public string? Changelog { get; set; }
    public long DownloadSize { get; set; }
    public List<string> ChangedPackages { get; set; } = [];
}

public sealed class UpdateCheckCache
{
    public string? ETag { get; set; }
    public string? LatestVersion { get; set; }
    public DateTime LastChecked { get; set; }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public required string TagName { get; set; }
    public string? Body { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    public required string Name { get; set; }
    [JsonPropertyName("browser_download_url")]
    public required string BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
}
