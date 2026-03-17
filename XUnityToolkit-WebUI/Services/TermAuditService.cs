namespace XUnityToolkit_WebUI.Services;

using XUnityToolkit_WebUI.Models;

public sealed record TermAuditResult(bool Passed, List<TermEntry> FailedTerms);

public class TermAuditService
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
                        failedTerms.Add(term);

                    break;
                }

                case TermType.DoNotTranslate:
                {
                    if (!translatedText.Contains(term.Original, comparison))
                        failedTerms.Add(term);

                    break;
                }
            }
        }

        return new TermAuditResult(Passed: failedTerms.Count == 0, FailedTerms: failedTerms);
    }
}
