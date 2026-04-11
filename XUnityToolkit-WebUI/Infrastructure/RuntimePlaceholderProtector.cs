using System.Text.RegularExpressions;

namespace XUnityToolkit_WebUI.Infrastructure;

internal static class RuntimePlaceholderPatterns
{
    internal const string TemplateVariablePattern = @"\{[A-Za-z_]\w{0,15}\}";

    internal const string SourcePlaceholderPattern =
        @"(?:\[SPECIAL_[A-Za-z0-9_]+\]|\u3010SPECIAL_[A-Za-z0-9_]+\u3011|\{[A-Za-z_]\w{0,15}\})";
}

internal sealed record RuntimePlaceholderProtectionResult(
    IList<string> Texts,
    IReadOnlyDictionary<string, string> Mapping)
{
    public bool HasProtectedTokens => Mapping.Count > 0;
}

internal static class RuntimePlaceholderProtector
{
    private static readonly Regex SourcePlaceholderRegex = new(
        RuntimePlaceholderPatterns.SourcePlaceholderPattern,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RestorePlaceholderRegex = new(
        @"(?:\{|\uFF5B){1,2}\s*XU_RT_(\d+)\s*(?:\}|\uFF5D){1,2}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FullPlaceholderRegex = new(
        @"^(?:\{|\uFF5B){2}\s*XU_RT_(\d+)\s*(?:\}|\uFF5D){2}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static RuntimePlaceholderProtectionResult Protect(IList<string> texts)
    {
        var tokenToPlaceholder = new Dictionary<string, string>(StringComparer.Ordinal);
        var placeholderToToken = new Dictionary<string, string>(StringComparer.Ordinal);
        var protectedTexts = new List<string>(texts.Count);
        var nextIndex = 0;

        foreach (var text in texts)
        {
            var replaced = SourcePlaceholderRegex.Replace(text, match =>
            {
                var token = match.Value;
                if (!tokenToPlaceholder.TryGetValue(token, out var placeholder))
                {
                    placeholder = $"{{{{XU_RT_{nextIndex}}}}}";
                    tokenToPlaceholder[token] = placeholder;
                    placeholderToToken[placeholder] = token;
                    nextIndex++;
                }

                return placeholder;
            });

            protectedTexts.Add(replaced);
        }

        return new RuntimePlaceholderProtectionResult(protectedTexts, placeholderToToken);
    }

    public static bool TryRestoreAndValidate(
        string sourceText,
        string candidateText,
        IReadOnlyDictionary<string, string> mapping,
        out string restoredText)
    {
        restoredText = Restore(candidateText, mapping);
        return HasExactRoundTrip(sourceText, restoredText);
    }

    public static string Restore(string candidateText, IReadOnlyDictionary<string, string> mapping)
    {
        if (mapping.Count == 0 || string.IsNullOrEmpty(candidateText))
            return candidateText;

        return RestorePlaceholderRegex.Replace(candidateText, match =>
        {
            var fullKey = $"{{{{XU_RT_{match.Groups[1].Value}}}}}";
            return mapping.TryGetValue(fullKey, out var original) ? original : match.Value;
        });
    }

    public static bool TryGetDirectReplacement(
        string text,
        IReadOnlyDictionary<string, string> mapping,
        out string restoredText)
    {
        restoredText = string.Empty;
        if (mapping.Count == 0)
            return false;

        var match = FullPlaceholderRegex.Match(text);
        if (!match.Success)
            return false;

        var fullKey = $"{{{{XU_RT_{match.Groups[1].Value}}}}}";
        if (!mapping.TryGetValue(fullKey, out var original))
        {
            restoredText = string.Empty;
            return false;
        }

        restoredText = original;
        return true;
    }

    public static bool HasExactRoundTrip(string sourceText, string candidateText)
    {
        var sourceTokens = ExtractTokenCounts(sourceText);
        var candidateTokens = ExtractTokenCounts(candidateText);

        if (sourceTokens.Count != candidateTokens.Count)
            return false;

        foreach (var (token, count) in sourceTokens)
        {
            if (!candidateTokens.TryGetValue(token, out var candidateCount) || candidateCount != count)
                return false;
        }

        return true;
    }

    private static Dictionary<string, int> ExtractTokenCounts(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in SourcePlaceholderRegex.Matches(text))
        {
            counts.TryGetValue(match.Value, out var current);
            counts[match.Value] = current + 1;
        }

        return counts;
    }
}
