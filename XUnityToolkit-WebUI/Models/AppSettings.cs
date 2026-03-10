namespace XUnityToolkit_WebUI.Models;

public sealed class AppSettings
{
    public string MirrorUrl { get; set; } = "https://ghfast.top/";
    public string Theme { get; set; } = "dark";
    public AiTranslationSettings AiTranslation { get; set; } = new();
}
