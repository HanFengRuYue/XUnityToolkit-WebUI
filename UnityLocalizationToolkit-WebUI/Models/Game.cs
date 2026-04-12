namespace UnityLocalizationToolkit_WebUI.Models;

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
    public GameTranslationWorkspaceSummary XUnityStatus { get; set; } = GameTranslationWorkspaceSummary.CreateXUnity();
    public GameTranslationWorkspaceSummary ManualTranslationStatus { get; set; } = GameTranslationWorkspaceSummary.CreateManual();
}

public enum GameTranslationWorkspace
{
    XUnity,
    Manual
}

public sealed class GameTranslationWorkspaceSummary
{
    public required GameTranslationWorkspace Workspace { get; init; }
    public string State { get; set; } = "idle";
    public bool Available { get; set; }
    public bool Active { get; set; }
    public int AssetCount { get; set; }
    public int EditableAssetCount { get; set; }
    public int OverrideCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Summary { get; set; }

    public static GameTranslationWorkspaceSummary CreateXUnity() => new()
    {
        Workspace = GameTranslationWorkspace.XUnity,
        State = "notInstalled"
    };

    public static GameTranslationWorkspaceSummary CreateManual() => new()
    {
        Workspace = GameTranslationWorkspace.Manual,
        State = "notScanned"
    };
}
