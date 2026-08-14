using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Models;

public sealed record TmpFontInstallRequest
{
    public string SourceId { get; init; } = BundledFontCatalog.DefaultSourceId;
    public string ApplicationMode { get; init; } = "fallback";
    public bool Enabled { get; init; } = true;
    public bool ReplaceExistingConfig { get; init; }
}

public sealed record TmpFontStatus
{
    public bool Installed { get; init; }
    public bool Enabled { get; init; }
    public List<ReplacementSource> AvailableSources { get; init; } = [];
    public string SourceId { get; init; } = BundledFontCatalog.DefaultSourceId;
    public string SourceDisplayName { get; init; } = BundledFontCatalog.DisplayName;
    public string ApplicationMode { get; init; } = "fallback";
    public string ActiveLoader { get; init; } = "none";
    public bool DirectTtfSupported { get; init; }
    public bool LegacyFallbackUsed { get; init; }
    public bool OverrideAdapterAvailable { get; init; }
    public bool RequiresRestart { get; init; }
    public DateTime? LastRuntimeCheckUtc { get; init; }
    public string Message { get; init; } = "运行时 TMP 字体尚未安装。";
    public string? Error { get; init; }
}
