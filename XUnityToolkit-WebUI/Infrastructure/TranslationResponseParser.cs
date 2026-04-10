using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace XUnityToolkit_WebUI.Infrastructure;

internal enum TranslationCandidateKind
{
    JsonArray,
    JsonString,
    PlainText,
    DirectPlaceholder,
    FallbackOriginal,
}

internal sealed record TranslationCandidate(string Text, TranslationCandidateKind Kind)
{
    public bool CanPersist => Kind != TranslationCandidateKind.FallbackOriginal;
}

internal static class TranslationResponseParser
{
    public static IList<TranslationCandidate> Parse(
        string content,
        int expectedCount,
        IList<string> fallbackTexts,
        ILogger? log = null)
    {
        var normalized = LlmResponseParser.StripNonJsonDecorations(content);

        if (TryParseJsonArray(normalized, expectedCount, out var array))
        {
            return array
                .Select(text => new TranslationCandidate(text, TranslationCandidateKind.JsonArray))
                .ToList();
        }

        if (expectedCount == 1)
        {
            if (TryParseJsonString(normalized, out var jsonString))
            {
                return
                [
                    new TranslationCandidate(jsonString, TranslationCandidateKind.JsonString)
                ];
            }

            var plainText = normalized.Trim();
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                log?.LogDebug("LLM响应为单条纯文本，已作为单条候选继续校验: \"{Preview}\"", BuildPreview(content));
                return
                [
                    new TranslationCandidate(plainText, TranslationCandidateKind.PlainText)
                ];
            }
        }

        log?.LogDebug("LLM响应为非结构化内容，已安全回退原文: \"{Preview}\"", BuildPreview(content));
        return fallbackTexts
            .Select(text => new TranslationCandidate(text, TranslationCandidateKind.FallbackOriginal))
            .ToList();
    }

    private static bool TryParseJsonArray(string normalized, int expectedCount, out List<string> translations)
    {
        translations = [];

        var json = LlmResponseParser.ExtractJsonArray(normalized);
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed is null || parsed.Count != expectedCount)
                return false;

            translations = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseJsonString(string normalized, out string translation)
    {
        translation = string.Empty;
        var candidate = normalized.Trim();
        if (!candidate.StartsWith('"') || !candidate.EndsWith('"'))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<string>(candidate);
            if (parsed is null)
                return false;

            translation = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildPreview(string content)
    {
        var preview = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return preview.Length > 200 ? preview[..200] + "..." : preview;
    }
}
