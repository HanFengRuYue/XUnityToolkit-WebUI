namespace UnityLocalizationToolkit_WebUI.Models;

public enum ModelDownloadSource { HuggingFace, ModelScope }

public sealed class AppSettings
{
    public string HfMirrorUrl { get; set; } = "https://hf-mirror.com";
    public ModelDownloadSource ModelDownloadSource { get; set; } = ModelDownloadSource.ModelScope;
    public string Theme { get; set; } = "dark";
    public AiTranslationSettings AiTranslation { get; set; } = new();
    public string? SteamGridDbApiKey { get; set; }
    public string LibraryViewMode { get; set; } = "grid";
    public string LibrarySortBy { get; set; } = "name";
    public string AccentColor { get; set; } = "#3b82f6";
    public string LibraryCardSize { get; set; } = "medium";
    public string LibraryGap { get; set; } = "normal";
    public bool LibraryShowLabels { get; set; } = true;
    public bool ReceivePreReleaseUpdates { get; set; }
    public int PageZoom { get; set; }
    public InstallOptions InstallOptions { get; set; } = new();
}
