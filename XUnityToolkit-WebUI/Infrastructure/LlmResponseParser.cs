namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Shared utilities for parsing LLM responses.
/// Handles markdown code fence stripping, think-block removal, and JSON extraction.
/// </summary>
public static class LlmResponseParser
{
    /// <summary>
    /// Strip reasoning blocks and markdown fences while preserving the raw payload content.
    /// </summary>
    public static string StripNonJsonDecorations(string content)
    {
        var normalized = content.Trim();

        var thinkEnd = normalized.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
            normalized = normalized[(thinkEnd + "</think>".Length)..].TrimStart();

        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = normalized.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                normalized = normalized[(firstLineEnd + 1)..];
                var closingFence = normalized.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0)
                    normalized = normalized[..closingFence];
            }
            else
            {
                normalized = normalized[3..];
            }

            normalized = normalized.Trim();
        }

        return normalized;
    }

    /// <summary>
    /// Strip &lt;think&gt;...&lt;/think&gt; blocks and markdown code fences from LLM output,
    /// then extract JSON content (array or object).
    /// </summary>
    public static string ExtractJsonContent(string content)
    {
        var json = StripNonJsonDecorations(content);

        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = FindJsonStart(json);
            var end = FindJsonEnd(json);
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }
        else
        {
            // Model may have wrapped JSON in prose text — extract it
            var start = FindJsonStart(json);
            if (start > 0)
            {
                var end = FindJsonEnd(json);
                if (end > start)
                    json = json[start..(end + 1)];
            }
        }

        return json;
    }

    /// <summary>
    /// Extract a JSON array from LLM output.
    /// Strips think blocks, markdown fences, and locates the outermost [...].
    /// </summary>
    public static string ExtractJsonArray(string content)
    {
        var json = StripNonJsonDecorations(content);

        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }
        else if (!json.StartsWith('['))
        {
            // Extract JSON array from surrounding prose
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        return json;
    }

    private static int FindJsonStart(string text)
    {
        var bracket = text.IndexOf('[');
        var brace = text.IndexOf('{');
        if (bracket < 0) return brace;
        if (brace < 0) return bracket;
        return Math.Min(bracket, brace);
    }

    private static int FindJsonEnd(string text)
    {
        var bracket = text.LastIndexOf(']');
        var brace = text.LastIndexOf('}');
        return Math.Max(bracket, brace);
    }
}
