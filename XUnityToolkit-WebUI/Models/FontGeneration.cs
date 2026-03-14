namespace XUnityToolkit_WebUI.Models;

public record FontGenerationRequest(
    string FontFilePath,
    string UnityVersion,
    int SamplingSize = 64,
    int AtlasWidth = 4096,
    int AtlasHeight = 4096,
    CharacterSetConfig? CharacterSet = null
);

public record FontGenerationResult(
    bool Success,
    string? OutputPath,
    string? FontName,
    int GlyphCount,
    int AtlasWidth,
    int AtlasHeight,
    string? Error,
    FontGenerationReport? Report = null
);

public record FontGenerationProgress(
    string Phase,
    int Current,
    int Total,
    string Message
);

public record FontGenerationComplete(
    bool Success,
    string? FontName,
    int GlyphCount,
    string? Error,
    FontGenerationReport? Report = null
);

public record FontUploadInfo
{
    public required string FileName { get; init; }
    public required string FontName { get; init; }
    public required long FileSize { get; init; }
}

public record FontGenerationStatus
{
    public bool IsGenerating { get; init; }
    public string? Phase { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
}

public record GeneratedFontInfo
{
    public string FileName { get; init; } = "";
    public string FontName { get; init; } = "";
    public int GlyphCount { get; init; }
    public long FileSize { get; init; }
    public DateTime GeneratedAt { get; init; }
    public bool HasReport { get; init; }
}

public record CharacterSetConfig
{
    public List<string> BuiltinSets { get; init; } = [];
    public string? CustomCharsetFileName { get; init; }
    public string? TranslationGameId { get; init; }
    public string? TranslationFileName { get; init; }
}

public record CharacterSetPreview
{
    public int TotalCharacters { get; init; }
    public Dictionary<string, int> SourceBreakdown { get; init; } = new();
    public int EstimatedAtlasCount { get; init; }
    public bool ExceedsSingleAtlas { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public record FontGenerationReport
{
    public string FontName { get; init; } = "";
    public int TotalCharacters { get; init; }
    public int SuccessfulGlyphs { get; init; }
    public int MissingGlyphs { get; init; }
    public List<string> MissingCharacters { get; init; } = [];
    public int TotalMissingCount { get; init; }
    public int AtlasCount { get; init; }
    public int AtlasWidth { get; init; }
    public int AtlasHeight { get; init; }
    public int SamplingSize { get; init; }
    public Dictionary<string, int> SourceBreakdown { get; init; } = new();
    public long ElapsedMilliseconds { get; init; }
}

public record CharsetInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int CharacterCount { get; init; }
}
