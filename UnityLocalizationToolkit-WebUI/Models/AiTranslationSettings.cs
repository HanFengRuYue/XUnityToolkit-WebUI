namespace UnityLocalizationToolkit_WebUI.Models;

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
        "硬性规则：\n" +
        "1. 只返回翻译结果的 JSON 数组，数组顺序和数量必须与输入完全一致。不要输出解释、注释、代码块、前后缀文本或任何额外内容。\n" +
        "2. 每个数组项只包含译文本体，不得额外包裹引号、书名号、方括号、圆括号、项目符号、编号或其他整句外层符号。\n" +
        "3. 如果原文没有整句外层引号或括号，译文也不得新增 “”、\"\"、''、「」、『』、【】、[]、() 等整句包裹；如果原文本身有这类外层包裹，译文必须保留对应外层包裹。\n" +
        "4. 不要增加或省略信息，不擅自添加原文中没有的主语、代词、说话人标记、提示语、舞台说明或句子；不得补出“他说：”“旁白：”这类前缀。\n" +
        "5. 保持与原文一致的格式：尽量保留行数、标点数量、停顿结构、句子边界和特殊符号，仅在目标语言语法绝对必要时做最小调整。\n" +
        "6. 严格保留所有占位符、控制符和变量名（如 {0}、%s、%d、<b>、</b>、\\n、{PLAYER}、{PC}、[SPECIAL_01] 或 【SPECIAL_01】 等），不要翻译、删除或改动其位置，括号样式、大小写、数量和位置必须与输入完全一致。\n" +
        "7. 若待翻译内容仅为单个字母、数字、符号或空字符串，请原样返回。\n" +
        "8. 翻译要准确自然、忠于原文，结合上下文正确使用人称代词和称呼，使对白自然符合游戏语境，不随意改变说话人。\n" +
        "9. 在忠实原文含义的前提下，使译文符合目标语言的表达习惯，并考虑游戏类型和角色性格，力求达到\"信、达、雅\"。\n\n" +
        "示例：\n" +
        "原文：[\"What?\"] → 输出：[\"什么？\"]，不要输出：[\"“什么？”\"] 或 [\"[什么？]\"]。\n" +
        "原文：[\"「What?」\"] → 输出：[\"「什么？」\"]。";

    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Number of recent translation pairs to include as context (0 = disabled, max 100).
    /// </summary>
    public int ContextSize { get; set; } = 10;

    /// <summary>
    /// Number of recent translation pairs for local mode (0 = disabled, max 10). Separate from cloud ContextSize.
    /// </summary>
    public int LocalContextSize { get; set; } = 3;

    /// <summary>min_p sampling parameter for local LLM (0-1, default 0.05). Dynamically prunes low-probability tokens.</summary>
    public double LocalMinP { get; set; } = 0.05;

    /// <summary>repeat_penalty for local LLM (0.5-2.0, default 1.0). 1.0 = no penalty (correct for translation).</summary>
    public double LocalRepeatPenalty { get; set; } = 1.0;

    /// <summary>
    /// Enable pre-translation cache optimization (normalization, regex generation, monitoring).
    /// </summary>
    public bool EnablePreTranslationCache { get; set; } = true;

    public List<ApiEndpointConfig> Endpoints { get; set; } = [];

    public bool TermAuditEnabled { get; set; } = true;
    public bool NaturalTranslationMode { get; set; } = true;

    // Translation Memory
    public bool EnableTranslationMemory { get; set; } = true;
    public int FuzzyMatchThreshold { get; set; } = 85;

    // LLM Pattern Analysis (pre-translation phase)
    public bool EnableLlmPatternAnalysis { get; set; } = true;

    // Multi-Round Translation (default ON = 2 rounds)
    public bool EnableMultiRoundTranslation { get; set; } = true;

    // Auto Term Extraction
    public bool EnableAutoTermExtraction { get; set; } = true;
    public bool AutoApplyExtractedTerms { get; set; }

    // Glossary extraction
    public bool GlossaryExtractionEnabled { get; set; }
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
