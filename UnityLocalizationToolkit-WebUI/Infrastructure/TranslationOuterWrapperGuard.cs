namespace UnityLocalizationToolkit_WebUI.Infrastructure;

internal sealed record OuterWrapperNormalizationResult(
    string Text,
    bool WasNormalized,
    bool IsValid,
    string? RemovedWrapperTrail);

internal static class TranslationOuterWrapperGuard
{
    private sealed record WrapperPair(char Open, char Close, string Label);

    private static readonly WrapperPair[] WrapperPairs =
    [
        new('“', '”', "中文双引号"),
        new('「', '」', "直角引号"),
        new('『', '』', "白直角引号"),
        new('【', '】', "方头括号"),
        new('[', ']', "方括号"),
        new('"', '"', "ASCII 双引号"),
        new('\'', '\'', "ASCII 单引号"),
    ];

    public static OuterWrapperNormalizationResult Normalize(string sourceText, string candidateText)
    {
        if (string.IsNullOrEmpty(candidateText))
            return new OuterWrapperNormalizationResult(candidateText, WasNormalized: false, IsValid: true, RemovedWrapperTrail: null);

        if (HasOuterWrapper(sourceText))
            return new OuterWrapperNormalizationResult(candidateText, WasNormalized: false, IsValid: true, RemovedWrapperTrail: null);

        var current = candidateText;
        List<string>? removedWrappers = null;

        while (TryGetOuterWrapper(current, out var wrapper))
        {
            current = current[1..^1];
            removedWrappers ??= [];
            removedWrappers.Add(wrapper.Label);

            if (current.Length < 2)
                break;
        }

        if (removedWrappers is null)
            return new OuterWrapperNormalizationResult(candidateText, WasNormalized: false, IsValid: true, RemovedWrapperTrail: null);

        var wrapperTrail = string.Join(" -> ", removedWrappers);
        if (string.IsNullOrWhiteSpace(current))
            return new OuterWrapperNormalizationResult(candidateText, WasNormalized: true, IsValid: false, RemovedWrapperTrail: wrapperTrail);

        return new OuterWrapperNormalizationResult(current, WasNormalized: true, IsValid: true, RemovedWrapperTrail: wrapperTrail);
    }

    public static bool HasOuterWrapper(string text) => TryGetOuterWrapper(text, out _);

    private static bool TryGetOuterWrapper(string text, out WrapperPair wrapper)
    {
        wrapper = null!;
        if (string.IsNullOrEmpty(text) || text.Length < 2)
            return false;

        var first = text[0];
        var last = text[^1];

        foreach (var candidate in WrapperPairs)
        {
            if (candidate.Open == first && candidate.Close == last)
            {
                wrapper = candidate;
                return true;
            }
        }

        return false;
    }
}
