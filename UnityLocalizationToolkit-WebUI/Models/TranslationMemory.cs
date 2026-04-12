using System.Text.Json.Serialization;

namespace UnityLocalizationToolkit_WebUI.Models;

public sealed record TranslationMemoryEntry
{
    public required string Original { get; init; }
    public required string Translation { get; init; }
    public string? NormalizedKey { get; init; }
    public DateTime TranslatedAt { get; init; } = DateTime.UtcNow;
    public int Round { get; init; } = 1;
    public bool IsFinal { get; init; }
}

public sealed record DynamicPattern
{
    public required string OriginalTemplate { get; init; }
    public required string TranslatedTemplate { get; init; }
    public List<VariablePosition> VariablePositions { get; init; } = [];
    public string Source { get; init; } = "template-detection";
}

public sealed record VariablePosition
{
    public string Type { get; init; } = "playerName";
    public int GroupIndex { get; init; }
}

public sealed record DynamicPatternStore
{
    public List<DynamicPattern> Patterns { get; init; } = [];
}

public sealed record TermCandidate
{
    public required string Original { get; init; }
    public required string Translation { get; init; }
    [JsonConverter(typeof(CamelCaseJsonStringEnumConverter<TermCategory>))]
    public TermCategory Category { get; init; } = TermCategory.General;
    public int Frequency { get; init; } = 1;
}

public sealed record TermCandidateStore
{
    public List<TermCandidate> Candidates { get; init; } = [];
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;
}

public sealed record TmMatchResult
{
    public required string Translation { get; init; }
    public required TmMatchType MatchType { get; init; }
}

public enum TmMatchType { Exact, Pattern, Fuzzy }
