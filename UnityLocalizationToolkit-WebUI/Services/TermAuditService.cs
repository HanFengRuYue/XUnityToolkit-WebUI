namespace UnityLocalizationToolkit_WebUI.Services;

using UnityLocalizationToolkit_WebUI.Models;

public sealed record TermAuditResult(bool Passed, List<TermEntry> FailedTerms);

public class TermAuditService(ILogger<TermAuditService> logger)
{
    public TermAuditResult AuditTranslation(string translatedText, List<TermEntry> matchedTerms)
    {
        var failedTerms = new List<TermEntry>();

        // Build set of passed term originals for substring-subsumption checks.
        // A shorter term should not fail audit if its Original is a substring of a
        // longer term's Original/Translation that IS present in the translated text.
        // Example: "Settings"→"设置" should pass when "SaveSettings.es3"→"SaveSettings.es3"
        // is correctly preserved (Settings is a substring of SaveSettings.es3).
        var passedOriginals = new List<string>();

        // Sort longest-first so we audit long terms before short ones
        var sortedTerms = matchedTerms
            .Where(t => !t.IsRegex)
            .OrderByDescending(t => (t.Original?.Length ?? 0))
            .ToList();

        foreach (var term in sortedTerms)
        {
            var comparison = term.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            switch (term.Type)
            {
                case TermType.Translate:
                {
                    // Skip if no translation to verify against
                    if (string.IsNullOrEmpty(term.Translation))
                        continue;

                    if (!translatedText.Contains(term.Translation, comparison))
                    {
                        // Before failing, check if this term's Original is a substring of
                        // a longer term that already passed audit. If so, the shorter term
                        // was subsumed by the longer term and should not be flagged.
                        if (IsSubsumedByPassedTerm(term.Original, passedOriginals, comparison))
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                                logger.LogDebug(
                                    "术语审查跳过: \"{Original}\"→\"{Translation}\" 被更长的已通过术语包含",
                                    term.Original, term.Translation);
                            break;
                        }

                        failedTerms.Add(term);
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("术语审查失败: 译文中未找到术语 \"{Original}\"→\"{Translation}\"，译文=\"{Translated}\"",
                                term.Original, term.Translation, translatedText.Length > 100 ? translatedText[..100] + "..." : translatedText);
                    }
                    else
                    {
                        passedOriginals.Add(term.Original);
                    }

                    break;
                }

                case TermType.DoNotTranslate:
                {
                    if (!translatedText.Contains(term.Original, comparison))
                    {
                        if (IsSubsumedByPassedTerm(term.Original, passedOriginals, comparison))
                        {
                            if (logger.IsEnabled(LogLevel.Debug))
                                logger.LogDebug(
                                    "术语审查跳过: 禁翻词 \"{Original}\" 被更长的已通过术语包含",
                                    term.Original);
                            break;
                        }

                        failedTerms.Add(term);
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("术语审查失败: 禁翻词 \"{Original}\" 在译文中缺失，译文=\"{Translated}\"",
                                term.Original, translatedText.Length > 100 ? translatedText[..100] + "..." : translatedText);
                    }
                    else
                    {
                        passedOriginals.Add(term.Original);
                    }

                    break;
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            if (failedTerms.Count == 0)
                logger.LogDebug("术语审查通过: {Count} 条术语全部验证通过", sortedTerms.Count);
            else
                logger.LogDebug("术语审查结果: {Failed}/{Total} 条术语未通过",
                    failedTerms.Count, sortedTerms.Count);
        }

        return new TermAuditResult(Passed: failedTerms.Count == 0, FailedTerms: failedTerms);
    }

    /// <summary>
    /// Check if <paramref name="original"/> is a substring of any already-passed term's Original.
    /// This prevents shorter terms from triggering false audit failures when they are
    /// subsumed by a longer term that was correctly applied.
    /// </summary>
    private static bool IsSubsumedByPassedTerm(
        string original, List<string> passedOriginals, StringComparison comparison)
    {
        foreach (var passed in passedOriginals)
        {
            if (passed.Length > original.Length && passed.Contains(original, comparison))
                return true;
        }
        return false;
    }
}
