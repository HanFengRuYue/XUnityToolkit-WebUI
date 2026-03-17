namespace XUnityToolkit_WebUI.Services;

using XUnityToolkit_WebUI.Models;

public sealed record TermAuditResult(bool Passed, List<TermEntry> FailedTerms);

public class TermAuditService(ILogger<TermAuditService> logger)
{
    public TermAuditResult AuditTranslation(string translatedText, List<TermEntry> matchedTerms)
    {
        var failedTerms = new List<TermEntry>();

        foreach (var term in matchedTerms)
        {
            // Skip regex terms — cannot be reliably verified
            if (term.IsRegex)
                continue;

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
                        failedTerms.Add(term);
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("术语审查失败: 译文中未找到术语 \"{Original}\"→\"{Translation}\"，译文=\"{Translated}\"",
                                term.Original, term.Translation, translatedText.Length > 100 ? translatedText[..100] + "..." : translatedText);
                    }

                    break;
                }

                case TermType.DoNotTranslate:
                {
                    if (!translatedText.Contains(term.Original, comparison))
                    {
                        failedTerms.Add(term);
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("术语审查失败: 禁翻词 \"{Original}\" 在译文中缺失，译文=\"{Translated}\"",
                                term.Original, translatedText.Length > 100 ? translatedText[..100] + "..." : translatedText);
                    }

                    break;
                }
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            if (failedTerms.Count == 0)
                logger.LogDebug("术语审查通过: {Count} 条术语全部验证通过", matchedTerms.Count(t => !t.IsRegex));
            else
                logger.LogDebug("术语审查结果: {Failed}/{Total} 条术语未通过",
                    failedTerms.Count, matchedTerms.Count(t => !t.IsRegex));
        }

        return new TermAuditResult(Passed: failedTerms.Count == 0, FailedTerms: failedTerms);
    }
}
