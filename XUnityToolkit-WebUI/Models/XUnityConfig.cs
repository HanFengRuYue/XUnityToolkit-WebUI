namespace XUnityToolkit_WebUI.Models;

public sealed class XUnityConfig
{
    // [Service]
    public string TranslationEngine { get; set; } = "GoogleTranslateV2";
    public string? FallbackEndpoint { get; set; }

    // [General]
    public string SourceLanguage { get; set; } = "ja";
    public string TargetLanguage { get; set; } = "en";

    // [Files]
    public string? OutputFile { get; set; }

    // [TextFrameworks]
    public bool EnableIMGUI { get; set; }
    public bool EnableUGUI { get; set; } = true;
    public bool EnableNGUI { get; set; } = true;
    public bool EnableTextMeshPro { get; set; } = true;
    public bool EnableTextMesh { get; set; }
    public bool EnableFairyGUI { get; set; } = true;

    // [Behaviour]
    public int MaxCharactersPerTranslation { get; set; } = 200;
    public int ForceSplitTextAfterCharacters { get; set; }
    public bool HandleRichText { get; set; } = true;
    public bool EnableUIResizing { get; set; } = true;
    public string? OverrideFont { get; set; }
    public string? OverrideFontSize { get; set; }
    public string? OverrideFontTextMeshPro { get; set; }
    public string? FallbackFontTextMeshPro { get; set; }
    public string? ResizeUILineSpacingScale { get; set; }
    public bool ForceUIResizing { get; set; } = true;
    public bool TextGetterCompatibilityMode { get; set; }
    public int MaxTextParserRecursion { get; set; } = 1;
    public bool EnableTranslationHelper { get; set; }
    public bool TemplateAllNumberAway { get; set; }
    public bool DisableTextMeshProScrollInEffects { get; set; }
    public bool CacheParsedTranslations { get; set; }
    public bool CacheWhitespaceDifferences { get; set; }
    public bool IgnoreWhitespaceInDialogue { get; set; } = true;
    public int MinDialogueChars { get; set; } = 4;

    // [Texture]
    public string? TextureDirectory { get; set; }
    public bool EnableTextureTranslation { get; set; }
    public bool EnableTextureDumping { get; set; }
    public bool EnableTextureToggling { get; set; }
    public bool EnableTextureScanOnSceneLoad { get; set; }
    public bool LoadUnmodifiedTextures { get; set; }
    public string TextureHashGenerationStrategy { get; set; } = "FromImageName";
    public bool EnableSpriteHooking { get; set; }

    public Dictionary<string, string> Extra { get; set; } = [];

    // Engine API credentials — stored in engine-specific INI sections
    // GoogleTranslateLegitimate → [GoogleLegitimate]
    public string? GoogleLegitimateApiKey { get; set; }
    // BingTranslateLegitimate → [BingLegitimate]
    public string? BingLegitimateSubscriptionKey { get; set; }
    // BaiduTranslate → [Baidu]
    public string? BaiduAppId { get; set; }
    public string? BaiduAppSecret { get; set; }
    // YandexTranslate → [Yandex]
    public string? YandexApiKey { get; set; }
    // DeepLTranslateLegitimate → [DeepLLegitimate]
    public string? DeepLLegitimateApiKey { get; set; }
    public bool DeepLLegitimateFree { get; set; }
    // PapagoTranslate → [PapagoTranslate]
    public string? PapagoClientId { get; set; }
    public string? PapagoClientSecret { get; set; }
    // LingoCloudTranslate → [LingoCloud]
    public string? LingoCloudToken { get; set; }
    // WatsonTranslate → [Watson]
    public string? WatsonUrl { get; set; }
    public string? WatsonKey { get; set; }
    // CustomTranslate → [Custom]
    public string? CustomTranslateUrl { get; set; }
    // LecPowerTranslator15 → [LecPowerTranslator15]
    public string? LecInstallPath { get; set; }
    // ezTrans → [ezTrans]
    public string? EzTransInstallPath { get; set; }
}
