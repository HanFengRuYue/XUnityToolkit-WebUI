namespace XUnityToolkit_WebUI.Models;

public record FontInfo
{
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
    public required bool IsInBundle { get; init; }
    public required string FontType { get; init; }    // "TMP" | "TTF"
    public bool IsSupported { get; init; }
    public bool ReplacementSupported { get; init; }
    public string? UnsupportedReason { get; init; }
    // TMP-specific (0 for TTF)
    public int AtlasCount { get; init; }
    public int GlyphCount { get; init; }
    public int CharacterCount { get; init; }
    public int AtlasWidth { get; init; }
    public int AtlasHeight { get; init; }
    // TTF-specific (0 for TMP)
    public string? TtfMode { get; init; } // "dynamicEmbedded" | "staticAtlas" | "osFallback" | "unknown"
    public long FontDataSize { get; init; }
    public int CharacterRectCount { get; init; }
    public int FontNamesCount { get; init; }
    public bool HasTextureRef { get; init; }
}

public record FontReplacementTarget
{
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
    public required string SourceId { get; init; }
}

public record ReplacementSource
{
    public required string Id { get; init; }
    public required string Kind { get; init; } // "TMP" | "TTF"
    public required string DisplayName { get; init; }
    public required string FileName { get; init; }
    public required string Origin { get; init; } // "default" | "custom"
    public bool IsDefault { get; init; }
    public long FileSize { get; init; }
    public DateTime? UploadedAt { get; init; }
}

public record ReplacementSourceSet
{
    public List<ReplacementSource> Tmp { get; init; } = [];
    public List<ReplacementSource> Ttf { get; init; } = [];
}

public record FontReplacementRequest
{
    public required FontReplacementTarget[] Fonts { get; init; }
    public string? CustomFontPath { get; init; }
}

public record FontReplacementStatus
{
    public bool IsReplaced { get; init; }
    public List<FontInfo> ReplacedFonts { get; init; } = [];
    public bool BackupExists { get; init; }
    public bool IsExternallyRestored { get; init; }
    public DateTime? ReplacedAt { get; init; }
    public string? FontSource { get; init; }
    public ReplacementSourceSet AvailableSources { get; init; } = new();
    public List<string> UsedSources { get; init; } = [];
}

public record FontReplacementResult
{
    public int SuccessCount { get; init; }
    public List<FailedFontEntry> FailedFonts { get; init; } = [];
}

public record FailedFontEntry
{
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
    public required string Error { get; init; }
}

public record FontBackupManifest
{
    public required string GameId { get; init; }
    public DateTime ReplacedAt { get; init; }
    public required string FontSource { get; init; }
    public List<string> UsedSources { get; init; } = [];
    public List<ReplacedFileEntry> ReplacedFiles { get; init; } = [];
    public List<CatalogFileEntry> CatalogFiles { get; init; } = [];
}

public record ReplacedFileEntry
{
    public required string OriginalPath { get; init; }
    public required string BackupFileName { get; init; }
    public required string ModifiedFileHash { get; init; }
    public List<ReplacedFontEntry> ReplacedFonts { get; init; } = [];
}

public record ReplacedFontEntry
{
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public string FontType { get; init; } = "TMP";
    public string? SourceId { get; init; }
    public string? SourceDisplayName { get; init; }
}

public record CatalogFileEntry
{
    public required string OriginalPath { get; init; }
    public required string BackupFileName { get; init; }
}

public record FontReplacementProgress
{
    public required string Phase { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string? CurrentFile { get; init; }
    public string? Message { get; init; }
}
