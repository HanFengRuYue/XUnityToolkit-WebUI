namespace XUnityToolkit_WebUI.Models;

public record TmpFontInfo
{
    public required string Name { get; init; }
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
    public required bool IsInBundle { get; init; }
    public int AtlasCount { get; init; }
    public int GlyphCount { get; init; }
    public int CharacterCount { get; init; }
    public int AtlasWidth { get; init; }
    public int AtlasHeight { get; init; }
    public bool IsSupported { get; init; }
}

public record FontTarget
{
    public required long PathId { get; init; }
    public required string AssetFile { get; init; }
}

public record FontReplacementRequest
{
    public required FontTarget[] Fonts { get; init; }
    public string? CustomFontPath { get; init; }
}

public record FontReplacementStatus
{
    public bool IsReplaced { get; init; }
    public List<TmpFontInfo> ReplacedFonts { get; init; } = [];
    public bool BackupExists { get; init; }
    public bool IsExternallyRestored { get; init; }
    public DateTime? ReplacedAt { get; init; }
    public string? FontSource { get; init; }
}

public record FontBackupManifest
{
    public required string GameId { get; init; }
    public DateTime ReplacedAt { get; init; }
    public required string FontSource { get; init; }
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
}
