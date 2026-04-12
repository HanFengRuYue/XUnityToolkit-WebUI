using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityLocalizationToolkit_WebUI.Models;

/// <summary>
/// camelCase enum converter for JSON serialization.
/// Frontend uses camelCase enum values (e.g. "translate", "doNotTranslate", "character").
/// </summary>
public class CamelCaseJsonStringEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public CamelCaseJsonStringEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<TermType>))]
public enum TermType
{
    Translate,
    DoNotTranslate
}

[JsonConverter(typeof(CamelCaseJsonStringEnumConverter<TermCategory>))]
public enum TermCategory
{
    Character,
    Location,
    Item,
    Skill,
    Organization,
    General
}

public enum TermSource { User, AI, Import }

/// <summary>
/// Shared category string-to-enum mapping used by term extraction services.
/// </summary>
public static class TermCategoryMapping
{
    public static readonly Dictionary<string, TermCategory> FromString =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["character"] = TermCategory.Character,
            ["location"] = TermCategory.Location,
            ["item"] = TermCategory.Item,
            ["skill"] = TermCategory.Skill,
            ["organization"] = TermCategory.Organization,
            ["general"] = TermCategory.General,
        };
}

public sealed record TermEntry
{
    [JsonConverter(typeof(CamelCaseJsonStringEnumConverter<TermType>))]
    public TermType Type { get; set; } = TermType.Translate;
    public required string Original { get; set; }
    public string? Translation { get; set; }
    [JsonConverter(typeof(CamelCaseJsonStringEnumConverter<TermCategory>))]
    public TermCategory? Category { get; set; }
    public string? Description { get; set; }
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public bool ExactMatch { get; set; }
    public int Priority { get; set; }
    public TermSource Source { get; set; } = TermSource.User;
}
