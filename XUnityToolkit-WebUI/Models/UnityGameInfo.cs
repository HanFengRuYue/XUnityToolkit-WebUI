namespace XUnityToolkit_WebUI.Models;

public enum UnityBackend { Mono, IL2CPP }

public enum Architecture { X86, X64 }

public sealed class UnityGameInfo
{
    public required string UnityVersion { get; init; }
    public UnityBackend Backend { get; init; }
    public Architecture Architecture { get; init; }
    public required string DetectedExecutable { get; init; }
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}
