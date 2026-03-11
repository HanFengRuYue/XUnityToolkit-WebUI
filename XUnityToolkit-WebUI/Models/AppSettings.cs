namespace XUnityToolkit_WebUI.Models;

public sealed class AppSettings
{
    public string MirrorUrl { get; set; } = "https://ghfast.top/";
    public string Theme { get; set; } = "dark";
    public AiTranslationSettings AiTranslation { get; set; } = new();
    public string? SteamGridDbApiKey { get; set; }
    public string LibraryViewMode { get; set; } = "grid";
    public string LibrarySortBy { get; set; } = "name";
    public string AccentColor { get; set; } = "#3b82f6";
    public string LibraryCardSize { get; set; } = "medium";
    public string LibraryGap { get; set; } = "normal";
    public bool LibraryShowLabels { get; set; } = true;
}
