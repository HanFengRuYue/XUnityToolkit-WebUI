namespace XUnityToolkit_WebUI.Models;

public record FontGenerationRequest(
    string FontFilePath,
    string UnityVersion,
    int SamplingSize = 64,
    int AtlasWidth = 4096,
    int AtlasHeight = 4096
);

public record FontGenerationResult(
    bool Success,
    string? OutputPath,
    string? FontName,
    int GlyphCount,
    int AtlasWidth,
    int AtlasHeight,
    string? Error
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
    string? Error
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
    public required string FileName { get; init; }
    public required string FontName { get; init; }
    public int GlyphCount { get; init; }
    public long FileSize { get; init; }
    public DateTime GeneratedAt { get; init; }
}
