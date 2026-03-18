using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public class TermMatchingService(ILogger<TermMatchingService> logger)
{
    private static readonly Regex CjkDetector = new(
        @"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}]",
        RegexOptions.Compiled);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private const string CjkBoundaryClasses =
        @"\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}";

    /// <summary>
    /// Find all term entries that match at least one source text.
    /// Results sorted by Priority descending, then Original.Length descending.
    /// </summary>
    public List<TermEntry> FindMatchedTerms(List<TermEntry> allTerms, List<string> sourceTexts)
    {
        var matched = new List<TermEntry>();

        foreach (var term in allTerms)
        {
            if (string.IsNullOrEmpty(term.Original))
                continue;

            try
            {
                if (term.IsRegex)
                {
                    if (MatchesAnyRegex(term, sourceTexts))
                        matched.Add(term);
                }
                else if (term.ExactMatch)
                {
                    if (MatchesAnyExact(term, sourceTexts))
                        matched.Add(term);
                }
                else
                {
                    if (MatchesAnyContains(term, sourceTexts))
                        matched.Add(term);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                logger.LogWarning("Regex timeout for term: {Original}", term.Original);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid regex pattern for term: {Original}", term.Original);
            }
        }

        matched.Sort((a, b) =>
        {
            var cmp = b.Priority.CompareTo(a.Priority);
            return cmp != 0 ? cmp : b.Original.Length.CompareTo(a.Original.Length);
        });

        if (logger.IsEnabled(LogLevel.Debug) && matched.Count > 0)
        {
            var termList = string.Join(", ", matched.Select(t =>
                $"[{t.Type}] \"{t.Original}\"" + (t.Type == TermType.Translate ? $"→\"{t.Translation}\"" : "")));
            logger.LogDebug("术语匹配: {Count}/{Total} 条术语命中 ({TextCount} 段文本): {Terms}",
                matched.Count, allTerms.Count, sourceTexts.Count, termList);
        }

        return matched;
    }

    /// <summary>
    /// Build a regex pattern for exact-match (word boundary) matching.
    /// CJK text uses Unicode category lookaround; Western text uses \b.
    /// </summary>
    public string BuildExactMatchPattern(string original)
    {
        var escaped = Regex.Escape(original);

        if (CjkDetector.IsMatch(original))
        {
            return $"(?<![{CjkBoundaryClasses}]){escaped}(?![{CjkBoundaryClasses}])";
        }

        return $@"\b{escaped}\b";
    }

    /// <summary>
    /// Rough token count estimate: CJK chars ~1 token each, Western words ~1.3 tokens each.
    /// </summary>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var cjkCount = 0;
        var inWord = false;
        var wordCount = 0;

        foreach (var ch in text)
        {
            if (IsCjkChar(ch))
            {
                cjkCount++;
                if (inWord)
                {
                    wordCount++;
                    inWord = false;
                }
            }
            else if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    inWord = true;
                }
            }
            else
            {
                if (inWord)
                {
                    wordCount++;
                    inWord = false;
                }
            }
        }

        if (inWord)
            wordCount++;

        return cjkCount + (int)Math.Ceiling(wordCount * 1.3);
    }

    /// <summary>
    /// Estimate tokens for a list of term entries (~20 tokens per entry).
    /// </summary>
    public int EstimateTermTokens(List<TermEntry> terms)
    {
        return terms.Count * 20;
    }

    private bool MatchesAnyContains(TermEntry term, List<string> sourceTexts)
    {
        var comparison = term.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (var text in sourceTexts)
        {
            if (text.Contains(term.Original, comparison))
                return true;
        }

        return false;
    }

    private bool MatchesAnyExact(TermEntry term, List<string> sourceTexts)
    {
        var pattern = BuildExactMatchPattern(term.Original);
        var options = RegexOptions.None;
        if (!term.CaseSensitive)
            options |= RegexOptions.IgnoreCase;

        var regex = new Regex(pattern, options, RegexTimeout);

        foreach (var text in sourceTexts)
        {
            if (regex.IsMatch(text))
                return true;
        }

        return false;
    }

    private static bool MatchesAnyRegex(TermEntry term, List<string> sourceTexts)
    {
        var options = RegexOptions.None;
        if (!term.CaseSensitive)
            options |= RegexOptions.IgnoreCase;

        var regex = new Regex(term.Original, options, RegexTimeout);

        foreach (var text in sourceTexts)
        {
            if (regex.IsMatch(text))
                return true;
        }

        return false;
    }

    private static bool IsCjkChar(char ch)
    {
        // CJK Unified Ideographs: U+4E00–U+9FFF
        // Hiragana: U+3040–U+309F
        // Katakana: U+30A0–U+30FF
        return ch is (>= '\u4E00' and <= '\u9FFF')
            or (>= '\u3040' and <= '\u309F')
            or (>= '\u30A0' and <= '\u30FF');
    }
}
