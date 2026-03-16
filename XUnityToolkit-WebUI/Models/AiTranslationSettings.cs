namespace XUnityToolkit_WebUI.Models;

public enum LlmProvider { OpenAI, Claude, Gemini, DeepSeek, Qwen, GLM, Kimi, Custom }

public sealed class AiTranslationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>"cloud" or "local". When "local", concurrency=1, no glossary extraction, contextSize capped at 10.</summary>
    public string ActiveMode { get; set; } = "cloud";

    public int MaxConcurrency { get; set; } = 4;

    public int Port { get; set; } = 51821;

    public string SystemPrompt { get; set; } =
        "你是一名专业的游戏文本翻译家。将以下 {from} 文本翻译为 {to}。\n\n" +
        "要求：\n" +
        "1. 仅返回翻译结果的 JSON 数组，保持与输入相同的顺序和数量。不要添加任何解释、说明或 markdown 格式。\n" +
        "2. 不要增加或省略信息，不擅自添加原文中没有的主语、代词或句子。\n" +
        "3. 保持与原文一致的格式：尽量保留行数、标点和特殊符号，仅在必要时做符合目标语言语法的微调。\n" +
        "4. 严格保留所有占位符、控制符和变量名（如 {0}、%s、%d、<b>、</b>、\\n、【SPECIAL_*】等），不要翻译、删除或改动其位置。\n" +
        "5. 若待翻译内容仅为单个字母、数字、符号或空字符串，请原样返回。\n" +
        "6. 翻译准确自然，忠于原文。结合上下文正确使用人称代词和称呼，使对白自然符合游戏语境，不随意改变说话人。\n" +
        "7. 在忠实原文含义的前提下，使译文符合目标语言的表达习惯，并考虑游戏类型和角色性格，力求达到\"信、达、雅\"。\n\n" +
        "输入示例：[\"Hello\",\"World\"] → 输出：[\"你好\",\"世界\"]";

    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Number of recent translation pairs to include as context (0 = disabled, max 100).
    /// </summary>
    public int ContextSize { get; set; } = 10;

    /// <summary>
    /// Number of recent translation pairs for local mode (0 = disabled, max 10). Separate from cloud ContextSize.
    /// </summary>
    public int LocalContextSize { get; set; }

    /// <summary>
    /// Experimental: enable pre-translation cache optimization (normalization, regex generation, monitoring).
    /// </summary>
    public bool EnablePreTranslationCache { get; set; }

    public List<ApiEndpointConfig> Endpoints { get; set; } = [];

    // Glossary extraction
    public bool GlossaryExtractionEnabled { get; set; }
    public string? GlossaryExtractionEndpointId { get; set; }
}

public sealed class ApiEndpointConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public LlmProvider Provider { get; set; } = LlmProvider.OpenAI;
    public string ApiBaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ModelName { get; set; } = "";
    public int Priority { get; set; } = 5;
    public bool Enabled { get; set; } = true;
}
