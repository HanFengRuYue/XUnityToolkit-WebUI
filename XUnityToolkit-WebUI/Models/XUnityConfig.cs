namespace XUnityToolkit_WebUI.Models;

public sealed class XUnityConfig
{
    public string SourceLanguage { get; set; } = "ja";
    public string TargetLanguage { get; set; } = "en";
    public string TranslationEngine { get; set; } = "GoogleTranslateV2";
    public string? FallbackEndpoint { get; set; }
    public bool EnableUGUI { get; set; } = true;
    public bool EnableNGUI { get; set; } = true;
    public bool EnableTextMeshPro { get; set; } = true;
    public bool EnableTextMesh { get; set; }
    public bool EnableIMGUI { get; set; }
    public int MaxCharactersPerTranslation { get; set; } = 200;
    public bool HandleRichText { get; set; } = true;
    public bool EnableUIResizing { get; set; } = true;
    public Dictionary<string, string> Extra { get; set; } = [];

    // Engine API credentials — stored in engine-specific INI sections
    public string? GoogleTranslateV2ApiKey { get; set; }
    public string? BingTranslateOcpApimSubscriptionKey { get; set; }
    public string? BaiduTranslateAppId { get; set; }
    public string? BaiduTranslateAppSecret { get; set; }
    public string? YandexTranslateApiKey { get; set; }
    public string? DeepLTranslateApiKey { get; set; }
    public string? PapagoTranslateClientId { get; set; }
    public string? PapagoTranslateClientSecret { get; set; }
}
