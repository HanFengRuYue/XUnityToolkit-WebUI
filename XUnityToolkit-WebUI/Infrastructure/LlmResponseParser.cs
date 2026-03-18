namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Shared utilities for parsing LLM responses.
/// Handles markdown code fence stripping, think-block removal, and JSON extraction.
/// </summary>
public static class LlmResponseParser
{
    /// <summary>
    /// Strip &lt;think&gt;...&lt;/think&gt; blocks and markdown code fences from LLM output,
    /// then extract JSON content (array or object).
    /// </summary>
    public static string ExtractJsonContent(string content)
    {
        var json = content.Trim();

        // Strip <think>...</think> blocks produced by reasoning models (e.g. Qwen3).
        // Find the LAST </think> tag to handle multi-block or nested output.
        var thinkEnd = json.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
            json = json[(thinkEnd + "</think>".Length)..].TrimStart();

        // Strip markdown code fences (``` or ```json)
        if (json.StartsWith("```"))
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
        var json = content.Trim();

        // Strip <think> blocks
        var thinkEnd = json.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (thinkEnd >= 0)
            json = json[(thinkEnd + "</think>".Length)..].TrimStart();

        // Strip markdown code fences
        if (json.StartsWith("```"))
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
