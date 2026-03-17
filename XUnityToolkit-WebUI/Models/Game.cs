namespace XUnityToolkit_WebUI.Models;

public enum InstallState
{
    NotInstalled,
    BepInExOnly,
    FullyInstalled,
    PartiallyInstalled
}

public sealed class Game
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public required string GamePath { get; set; }
    public string? ExecutableName { get; set; }
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsUnityGame { get; set; }
    public UnityGameInfo? DetectedInfo { get; set; }
    public InstallState InstallState { get; set; } = InstallState.NotInstalled;
    public List<DetectedModFramework>? DetectedFrameworks { get; set; }
    public string? InstalledBepInExVersion { get; set; }
    public string? InstalledXUnityVersion { get; set; }
    public int? SteamAppId { get; set; }
    public int? SteamGridDbGameId { get; set; }
    public DateTime? LastPlayedAt { get; set; }
    public string? AiDescription { get; set; }

    // Computed at API response time — not authoritative in library.json
    public bool HasCover { get; set; }
    public bool HasBackground { get; set; }
}
