using System.Text.Json.Serialization;

namespace XUnityToolkit_WebUI.Models;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public required string TagName { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; init; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; init; } = [];
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("browser_download_url")]
    public required string BrowserDownloadUrl { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }
}
