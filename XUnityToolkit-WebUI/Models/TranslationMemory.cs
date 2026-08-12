namespace XUnityToolkit_WebUI.Models;

public sealed record TranslationMemoryEntry
{
    public required string Original { get; init; }
    public required string Translation { get; init; }
    public string? NormalizedKey { get; init; }
    public DateTime TranslatedAt { get; init; } = DateTime.UtcNow;
    public int Round { get; init; } = 1;
    public bool IsFinal { get; init; }
}

public sealed record TmMatchResult
{
    public required string Translation { get; init; }
    public required TmMatchType MatchType { get; init; }
}

public enum TmMatchType { Exact, Fuzzy }
