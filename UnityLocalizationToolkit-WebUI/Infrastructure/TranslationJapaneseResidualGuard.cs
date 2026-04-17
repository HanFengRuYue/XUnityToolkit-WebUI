using System.Text.RegularExpressions;

namespace UnityLocalizationToolkit_WebUI.Infrastructure;

internal sealed record JapaneseResidualInspectionResult(
    bool HasResidualJapanese,
    string ResidualPreview);

internal static partial class TranslationJapaneseResidualGuard
{
    private static readonly Regex KnownPlaceholderRegex = new(
        $@"(?:{RuntimePlaceholderPatterns.SourcePlaceholderPattern}|\{{\{{(?:XU_RT|G|DNT)_\d+\}}\}})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static JapaneseResidualInspectionResult Inspect(
        string translatedText,
        IEnumerable<string>? allowedSegments = null)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
            return new JapaneseResidualInspectionResult(false, string.Empty);

        var scrubbed = KnownPlaceholderRegex.Replace(translatedText, " ");

        if (allowedSegments is not null)
        {
            foreach (var segment in allowedSegments
                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(static segment => segment.Length))
            {
                scrubbed = scrubbed.Replace(segment, " ", StringComparison.Ordinal);
            }
        }

        var matches = JapaneseKanaRegex().Matches(scrubbed);
        if (matches.Count == 0)
            return new JapaneseResidualInspectionResult(false, string.Empty);

        var preview = string.Join(", ", matches
            .Select(static match => match.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(3));

        return new JapaneseResidualInspectionResult(true, preview);
    }

    [GeneratedRegex(@"[\u3040-\u30FF\u31F0-\u31FF]+")]
    private static partial Regex JapaneseKanaRegex();
}
