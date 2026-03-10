namespace XUnityToolkit_WebUI.Models;

public enum LlmProvider { OpenAI, Claude, Gemini, Custom }

public sealed class AiTranslationSettings
{
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAI;

    /// <summary>
    /// API base URL. Empty = use provider default.
    /// OpenAI: https://api.openai.com/v1
    /// Claude: https://api.anthropic.com/v1
    /// Gemini: https://generativelanguage.googleapis.com/v1beta
    /// Custom: must be set manually (OpenAI-compatible endpoint).
    /// </summary>
    public string ApiBaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Model name. Empty = use provider default.
    /// OpenAI: gpt-4o-mini, Claude: claude-haiku-4-5-20251001, Gemini: gemini-2.0-flash
    /// </summary>
    public string ModelName { get; set; } = "";

    public string SystemPrompt { get; set; } =
        "You are a professional game text translator. " +
        "Translate the following texts from {from} to {to}. " +
        "Return ONLY a JSON array of translated strings in the same order as the input. " +
        "Do not add any explanation or markdown formatting. " +
        "Example input: [\"Hello\",\"World\"] Example output: [\"你好\",\"世界\"]";

    public double Temperature { get; set; } = 0.3;
}
