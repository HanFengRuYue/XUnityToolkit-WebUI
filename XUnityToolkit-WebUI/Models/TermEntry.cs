using System.Text.Json.Serialization;

namespace XUnityToolkit_WebUI.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TermType>))]
public enum TermType
{
    Translate,
    DoNotTranslate
}

[JsonConverter(typeof(JsonStringEnumConverter<TermCategory>))]
public enum TermCategory
{
    Character,
    Location,
    Item,
    Skill,
    Organization,
    General
}

public sealed record TermEntry
{
    public TermType Type { get; set; } = TermType.Translate;
    public required string Original { get; set; }
    public string? Translation { get; set; }
    public TermCategory? Category { get; set; }
    public string? Description { get; set; }
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public bool ExactMatch { get; set; }
    public int Priority { get; set; }
}
