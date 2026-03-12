using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ConfigurationService(ILogger<ConfigurationService> logger, AppSettingsService settingsService)
{
    private const string ConfigFileName = "AutoTranslatorConfig.ini";

    public string GetConfigPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "config", ConfigFileName);

    public Task<XUnityConfig> GetAsync(string gamePath, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        if (!File.Exists(configPath))
            return Task.FromResult(new XUnityConfig());

        var ini = ParseIni(File.ReadAllText(configPath));

        var config = new XUnityConfig
        {
            // [Service]
            TranslationEngine = GetValue(ini, "Service", "Endpoint", "GoogleTranslateV2"),
            FallbackEndpoint = GetValue(ini, "Service", "FallbackEndpoint", null),
            // [General]
            SourceLanguage = GetValue(ini, "General", "FromLanguage", "ja"),
            TargetLanguage = GetValue(ini, "General", "Language", "en"),
            // [Files]
            OutputFile = GetNullableValue(ini, "Files", "OutputFile"),
            // [TextFrameworks]
            EnableIMGUI = GetBool(ini, "TextFrameworks", "EnableIMGUI", false),
            EnableUGUI = GetBool(ini, "TextFrameworks", "EnableUGUI", true),
            EnableNGUI = GetBool(ini, "TextFrameworks", "EnableNGUI", true),
            EnableTextMeshPro = GetBool(ini, "TextFrameworks", "EnableTextMeshPro", true),
            EnableTextMesh = GetBool(ini, "TextFrameworks", "EnableTextMesh", false),
            EnableFairyGUI = GetBool(ini, "TextFrameworks", "EnableFairyGUI", true),
            // [Behaviour]
            MaxCharactersPerTranslation = GetInt(ini, "Behaviour", "MaxCharactersPerTranslation", 200),
            ForceSplitTextAfterCharacters = GetInt(ini, "Behaviour", "ForceSplitTextAfterCharacters", 0),
            HandleRichText = GetBool(ini, "Behaviour", "HandleRichText", true),
            EnableUIResizing = GetBool(ini, "Behaviour", "EnableUIResizing", true),
            OverrideFont = GetNullableValue(ini, "Behaviour", "OverrideFont"),
            OverrideFontSize = GetNullableValue(ini, "Behaviour", "OverrideFontSize"),
            OverrideFontTextMeshPro = GetNullableValue(ini, "Behaviour", "OverrideFontTextMeshPro"),
            FallbackFontTextMeshPro = GetNullableValue(ini, "Behaviour", "FallbackFontTextMeshPro"),
            ResizeUILineSpacingScale = GetNullableValue(ini, "Behaviour", "ResizeUILineSpacingScale"),
            ForceUIResizing = GetBool(ini, "Behaviour", "ForceUIResizing", true),
            TextGetterCompatibilityMode = GetBool(ini, "Behaviour", "TextGetterCompatibilityMode", false),
            MaxTextParserRecursion = GetInt(ini, "Behaviour", "MaxTextParserRecursion", 1),
            EnableTranslationHelper = GetBool(ini, "Behaviour", "EnableTranslationHelper", false),
            TemplateAllNumberAway = GetBool(ini, "Behaviour", "TemplateAllNumberAway", false),
            DisableTextMeshProScrollInEffects = GetBool(ini, "Behaviour", "DisableTextMeshProScrollInEffects", false),
            CacheParsedTranslations = GetBool(ini, "Behaviour", "CacheParsedTranslations", false),
            // [Texture]
            TextureDirectory = GetNullableValue(ini, "Texture", "TextureDirectory"),
            EnableTextureTranslation = GetBool(ini, "Texture", "EnableTextureTranslation", false),
            EnableTextureDumping = GetBool(ini, "Texture", "EnableTextureDumping", false),
            EnableTextureToggling = GetBool(ini, "Texture", "EnableTextureToggling", false),
            EnableTextureScanOnSceneLoad = GetBool(ini, "Texture", "EnableTextureScanOnSceneLoad", false),
            LoadUnmodifiedTextures = GetBool(ini, "Texture", "LoadUnmodifiedTextures", false),
            TextureHashGenerationStrategy = GetValue(ini, "Texture", "TextureHashGenerationStrategy", "FromImageName"),
            EnableSpriteHooking = GetBool(ini, "Texture", "EnableSpriteHooking", false),
            // Engine API credentials
            GoogleLegitimateApiKey = GetNullableValue(ini, "GoogleLegitimate", "GoogleAPIKey"),
            BingLegitimateSubscriptionKey = GetNullableValue(ini, "BingLegitimate", "OcpApimSubscriptionKey"),
            BaiduAppId = GetNullableValue(ini, "Baidu", "BaiduAppId"),
            BaiduAppSecret = GetNullableValue(ini, "Baidu", "BaiduAppSecret"),
            YandexApiKey = GetNullableValue(ini, "Yandex", "YandexAPIKey"),
            DeepLLegitimateApiKey = GetNullableValue(ini, "DeepLLegitimate", "ApiKey"),
            DeepLLegitimateFree = GetBool(ini, "DeepLLegitimate", "Free", false),
            PapagoClientId = GetNullableValue(ini, "PapagoTranslate", "PapagoClientId"),
            PapagoClientSecret = GetNullableValue(ini, "PapagoTranslate", "PapagoClientSecret"),
            LingoCloudToken = GetNullableValue(ini, "LingoCloud", "LingoCloudToken"),
            WatsonUrl = GetNullableValue(ini, "Watson", "Url"),
            WatsonKey = GetNullableValue(ini, "Watson", "Key"),
            CustomTranslateUrl = GetNullableValue(ini, "Custom", "Url"),
            LecInstallPath = GetNullableValue(ini, "LecPowerTranslator15", "InstallationPath"),
            EzTransInstallPath = GetNullableValue(ini, "ezTrans", "InstallationPath"),
        };

        logger.LogInformation("已读取配置文件: {Path}", configPath);
        return Task.FromResult(config);
    }

    /// <summary>
    /// Get the raw INI file content as a string.
    /// </summary>
    public Task<string?> GetRawAsync(string gamePath, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        if (!File.Exists(configPath))
            return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(configPath));
    }

    /// <summary>
    /// Save raw INI file content directly.
    /// </summary>
    public Task SaveRawAsync(string gamePath, string content, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        var configDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(configDir);
        File.WriteAllText(configPath, content);
        logger.LogInformation("已保存原始配置文件: {Path}", configPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Apply optimal default settings to the config file (called during installation).
    /// Only modifies specific keys, preserves all other content.
    /// </summary>
    public async Task ApplyOptimalDefaultsAsync(string gamePath, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        if (!File.Exists(configPath))
            return;

        var lines = File.ReadAllLines(configPath).ToList();

        // Read current port from settings for LLMTranslate URL
        var settings = await settingsService.GetAsync(ct);
        var port = settings.AiTranslation.Port;

        var modifications = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["General"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Language"] = "zh",
            },
            ["TextFrameworks"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["EnableIMGUI"] = "True",
                ["EnableUGUI"] = "True",
                ["EnableNGUI"] = "True",
                ["EnableTextMeshPro"] = "True",
                ["EnableTextMesh"] = "True",
                ["EnableFairyGUI"] = "True",
            },
            ["Behaviour"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["MaxCharactersPerTranslation"] = "2500",
                ["OverrideFont"] = "Microsoft YaHei",
                ["MaxTextParserRecursion"] = "4",
                ["CacheParsedTranslations"] = "True",
            },
            ["Service"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Endpoint"] = "LLMTranslate",
                ["FallbackEndpoint"] = "GoogleTranslateV2",
            },
            ["LLMTranslate"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["ToolkitUrl"] = $"http://127.0.0.1:{port}",
            },
        };

        var result = ApplyModifications(lines, modifications);
        File.WriteAllText(configPath, string.Join(Environment.NewLine, result) + Environment.NewLine);
        logger.LogInformation("已应用最佳默认配置: {Path}", configPath);
    }

    /// <summary>
    /// Patch an existing config file, modifying only the specified keys while preserving all other content.
    /// Falls back to full write if the config file doesn't exist.
    /// </summary>
    public Task PatchAsync(string gamePath, XUnityConfig config, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        var configDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(configDir);

        if (!File.Exists(configPath))
        {
            // Fallback: generate from scratch if no auto-generated file exists
            return WriteFullAsync(gamePath, config, ct);
        }

        var lines = File.ReadAllLines(configPath).ToList();

        // Build the modifications we want to apply
        var modifications = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Service"] = BuildSectionMods(
                ("Endpoint", config.TranslationEngine),
                ("FallbackEndpoint", config.FallbackEndpoint)),
            ["General"] = BuildSectionMods(
                ("Language", config.TargetLanguage),
                ("FromLanguage", config.SourceLanguage)),
            ["Files"] = BuildSectionMods(
                ("OutputFile", config.OutputFile)),
            ["TextFrameworks"] = BuildSectionMods(
                ("EnableIMGUI", config.EnableIMGUI.ToString()),
                ("EnableUGUI", config.EnableUGUI.ToString()),
                ("EnableNGUI", config.EnableNGUI.ToString()),
                ("EnableTextMeshPro", config.EnableTextMeshPro.ToString()),
                ("EnableTextMesh", config.EnableTextMesh.ToString()),
                ("EnableFairyGUI", config.EnableFairyGUI.ToString())),
            ["Behaviour"] = BuildSectionMods(
                ("MaxCharactersPerTranslation", config.MaxCharactersPerTranslation.ToString()),
                ("ForceSplitTextAfterCharacters", config.ForceSplitTextAfterCharacters.ToString()),
                ("HandleRichText", config.HandleRichText.ToString()),
                ("EnableUIResizing", config.EnableUIResizing.ToString()),
                ("OverrideFont", config.OverrideFont),
                ("OverrideFontSize", config.OverrideFontSize),
                ("OverrideFontTextMeshPro", config.OverrideFontTextMeshPro),
                ("FallbackFontTextMeshPro", config.FallbackFontTextMeshPro),
                ("ResizeUILineSpacingScale", config.ResizeUILineSpacingScale),
                ("ForceUIResizing", config.ForceUIResizing.ToString()),
                ("TextGetterCompatibilityMode", config.TextGetterCompatibilityMode.ToString()),
                ("MaxTextParserRecursion", config.MaxTextParserRecursion.ToString()),
                ("EnableTranslationHelper", config.EnableTranslationHelper.ToString()),
                ("TemplateAllNumberAway", config.TemplateAllNumberAway.ToString()),
                ("DisableTextMeshProScrollInEffects", config.DisableTextMeshProScrollInEffects.ToString()),
                ("CacheParsedTranslations", config.CacheParsedTranslations.ToString())),
            ["Texture"] = BuildSectionMods(
                ("TextureDirectory", config.TextureDirectory),
                ("EnableTextureTranslation", config.EnableTextureTranslation.ToString()),
                ("EnableTextureDumping", config.EnableTextureDumping.ToString()),
                ("EnableTextureToggling", config.EnableTextureToggling.ToString()),
                ("EnableTextureScanOnSceneLoad", config.EnableTextureScanOnSceneLoad.ToString()),
                ("LoadUnmodifiedTextures", config.LoadUnmodifiedTextures.ToString()),
                ("TextureHashGenerationStrategy", config.TextureHashGenerationStrategy),
                ("EnableSpriteHooking", config.EnableSpriteHooking.ToString())),
        };

        // Add engine-specific API key sections
        AddEngineKeyModification(modifications, "GoogleLegitimate", "GoogleAPIKey", config.GoogleLegitimateApiKey);
        AddEngineKeyModification(modifications, "BingLegitimate", "OcpApimSubscriptionKey", config.BingLegitimateSubscriptionKey);
        AddEngineKeyModifications(modifications, "Baidu",
            ("BaiduAppId", config.BaiduAppId),
            ("BaiduAppSecret", config.BaiduAppSecret));
        AddEngineKeyModification(modifications, "Yandex", "YandexAPIKey", config.YandexApiKey);
        AddEngineKeyModifications(modifications, "DeepLLegitimate",
            ("ApiKey", config.DeepLLegitimateApiKey),
            ("Free", config.DeepLLegitimateFree.ToString()));
        AddEngineKeyModifications(modifications, "PapagoTranslate",
            ("PapagoClientId", config.PapagoClientId),
            ("PapagoClientSecret", config.PapagoClientSecret));
        AddEngineKeyModification(modifications, "LingoCloud", "LingoCloudToken", config.LingoCloudToken);
        AddEngineKeyModifications(modifications, "Watson",
            ("Url", config.WatsonUrl),
            ("Key", config.WatsonKey));
        AddEngineKeyModification(modifications, "Custom", "Url", config.CustomTranslateUrl);
        AddEngineKeyModification(modifications, "LecPowerTranslator15", "InstallationPath", config.LecInstallPath);
        AddEngineKeyModification(modifications, "ezTrans", "InstallationPath", config.EzTransInstallPath);

        // Apply modifications to the existing lines
        var result = ApplyModifications(lines, modifications);

        File.WriteAllText(configPath, string.Join(Environment.NewLine, result) + Environment.NewLine);
        logger.LogInformation("已更新配置文件: {Path}", configPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Write a complete config file from scratch (only used as fallback).
    /// </summary>
    private Task WriteFullAsync(string gamePath, XUnityConfig config, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);

        var sb = new StringBuilder();
        sb.AppendLine("[Service]");
        sb.AppendLine($"Endpoint={config.TranslationEngine}");
        sb.AppendLine($"FallbackEndpoint={config.FallbackEndpoint ?? ""}");
        sb.AppendLine();
        sb.AppendLine("[General]");
        sb.AppendLine($"Language={config.TargetLanguage}");
        sb.AppendLine($"FromLanguage={config.SourceLanguage}");
        sb.AppendLine();
        sb.AppendLine("[Files]");
        sb.AppendLine($"OutputFile={config.OutputFile ?? "Translation\\{Lang}\\Text\\_AutoGeneratedTranslations.txt"}");
        sb.AppendLine();
        sb.AppendLine("[TextFrameworks]");
        sb.AppendLine($"EnableIMGUI={config.EnableIMGUI}");
        sb.AppendLine($"EnableUGUI={config.EnableUGUI}");
        sb.AppendLine($"EnableNGUI={config.EnableNGUI}");
        sb.AppendLine($"EnableTextMeshPro={config.EnableTextMeshPro}");
        sb.AppendLine($"EnableTextMesh={config.EnableTextMesh}");
        sb.AppendLine($"EnableFairyGUI={config.EnableFairyGUI}");
        sb.AppendLine();
        sb.AppendLine("[Behaviour]");
        sb.AppendLine($"MaxCharactersPerTranslation={config.MaxCharactersPerTranslation}");
        sb.AppendLine($"ForceSplitTextAfterCharacters={config.ForceSplitTextAfterCharacters}");
        sb.AppendLine($"HandleRichText={config.HandleRichText}");
        sb.AppendLine($"EnableUIResizing={config.EnableUIResizing}");
        sb.AppendLine($"OverrideFont={config.OverrideFont ?? ""}");
        sb.AppendLine($"OverrideFontSize={config.OverrideFontSize ?? ""}");
        sb.AppendLine($"OverrideFontTextMeshPro={config.OverrideFontTextMeshPro ?? ""}");
        sb.AppendLine($"FallbackFontTextMeshPro={config.FallbackFontTextMeshPro ?? ""}");
        sb.AppendLine($"ResizeUILineSpacingScale={config.ResizeUILineSpacingScale ?? ""}");
        sb.AppendLine($"ForceUIResizing={config.ForceUIResizing}");
        sb.AppendLine($"TextGetterCompatibilityMode={config.TextGetterCompatibilityMode}");
        sb.AppendLine($"MaxTextParserRecursion={config.MaxTextParserRecursion}");
        sb.AppendLine($"EnableTranslationHelper={config.EnableTranslationHelper}");
        sb.AppendLine($"TemplateAllNumberAway={config.TemplateAllNumberAway}");
        sb.AppendLine($"DisableTextMeshProScrollInEffects={config.DisableTextMeshProScrollInEffects}");
        sb.AppendLine($"CacheParsedTranslations={config.CacheParsedTranslations}");
        sb.AppendLine();
        sb.AppendLine("[Texture]");
        sb.AppendLine($"TextureDirectory={config.TextureDirectory ?? "Translation\\{Lang}\\Texture"}");
        sb.AppendLine($"EnableTextureTranslation={config.EnableTextureTranslation}");
        sb.AppendLine($"EnableTextureDumping={config.EnableTextureDumping}");
        sb.AppendLine($"EnableTextureToggling={config.EnableTextureToggling}");
        sb.AppendLine($"EnableTextureScanOnSceneLoad={config.EnableTextureScanOnSceneLoad}");
        sb.AppendLine($"LoadUnmodifiedTextures={config.LoadUnmodifiedTextures}");
        sb.AppendLine($"TextureHashGenerationStrategy={config.TextureHashGenerationStrategy}");
        sb.AppendLine($"EnableSpriteHooking={config.EnableSpriteHooking}");

        AppendEngineSection(sb, "GoogleLegitimate", ("GoogleAPIKey", config.GoogleLegitimateApiKey));
        AppendEngineSection(sb, "BingLegitimate", ("OcpApimSubscriptionKey", config.BingLegitimateSubscriptionKey));
        AppendEngineSection(sb, "Baidu", ("BaiduAppId", config.BaiduAppId), ("BaiduAppSecret", config.BaiduAppSecret));
        AppendEngineSection(sb, "Yandex", ("YandexAPIKey", config.YandexApiKey));
        AppendEngineSection(sb, "DeepLLegitimate", ("ApiKey", config.DeepLLegitimateApiKey), ("Free", config.DeepLLegitimateFree.ToString()));
        AppendEngineSection(sb, "PapagoTranslate", ("PapagoClientId", config.PapagoClientId), ("PapagoClientSecret", config.PapagoClientSecret));
        AppendEngineSection(sb, "LingoCloud", ("LingoCloudToken", config.LingoCloudToken));
        AppendEngineSection(sb, "Watson", ("Url", config.WatsonUrl), ("Key", config.WatsonKey));
        AppendEngineSection(sb, "Custom", ("Url", config.CustomTranslateUrl));
        AppendEngineSection(sb, "LecPowerTranslator15", ("InstallationPath", config.LecInstallPath));
        AppendEngineSection(sb, "ezTrans", ("InstallationPath", config.EzTransInstallPath));

        File.WriteAllText(configPath, sb.ToString());
        logger.LogInformation("已写入配置文件: {Path}", configPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Patch a single section in the config file, modifying only the specified keys.
    /// </summary>
    public Task PatchSectionAsync(string gamePath, string section, Dictionary<string, string> keys, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        if (!File.Exists(configPath))
            return Task.CompletedTask;

        var lines = File.ReadAllLines(configPath).ToList();
        var modifications = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [section] = new Dictionary<string, string>(keys, StringComparer.OrdinalIgnoreCase),
        };

        var result = ApplyModifications(lines, modifications);
        File.WriteAllText(configPath, string.Join(Environment.NewLine, result) + Environment.NewLine);
        logger.LogInformation("已更新配置 [{Section}]: {Path}", section, configPath);
        return Task.CompletedTask;
    }

    private static List<string> ApplyModifications(
        List<string> lines,
        Dictionary<string, Dictionary<string, string>> modifications)
    {
        var result = new List<string>(lines.Count);
        string? currentSection = null;
        var appliedKeys = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Section header
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                // Before leaving current section, add any unapplied keys
                if (currentSection != null)
                    AppendMissingKeys(result, currentSection, modifications, appliedKeys);

                currentSection = line[1..^1];
                result.Add(rawLine);
                continue;
            }

            // Key=Value line
            if (currentSection != null && !string.IsNullOrEmpty(line) && !line.StartsWith('#') && !line.StartsWith(';'))
            {
                var eqIndex = line.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = line[..eqIndex].Trim();
                    if (modifications.TryGetValue(currentSection, out var sectionMods) &&
                        sectionMods.TryGetValue(key, out var newValue))
                    {
                        // Replace the value, preserve the key formatting
                        result.Add($"{key}={newValue}");
                        if (!appliedKeys.ContainsKey(currentSection))
                            appliedKeys[currentSection] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        appliedKeys[currentSection].Add(key);
                        continue;
                    }
                }
            }

            result.Add(rawLine);
        }

        // Handle the last section
        if (currentSection != null)
            AppendMissingKeys(result, currentSection, modifications, appliedKeys);

        // Add entirely new sections that don't exist in the file
        foreach (var (section, keys) in modifications)
        {
            if (appliedKeys.ContainsKey(section)) continue; // section was in the file; AppendMissingKeys handled it
            var missingKeys = keys.Where(kv => !string.IsNullOrEmpty(kv.Value)).ToList();
            if (missingKeys.Count == 0) continue;
            result.Add("");
            result.Add($"[{section}]");
            foreach (var (key, value) in missingKeys)
                result.Add($"{key}={value}");
        }

        return result;
    }

    private static void AppendMissingKeys(
        List<string> result,
        string section,
        Dictionary<string, Dictionary<string, string>> modifications,
        Dictionary<string, HashSet<string>> appliedKeys)
    {
        if (!modifications.TryGetValue(section, out var sectionMods)) return;

        var applied = appliedKeys.TryGetValue(section, out var ak) ? ak : new HashSet<string>();
        foreach (var (key, value) in sectionMods)
        {
            if (!applied.Contains(key) && value.Length > 0)
            {
                result.Add($"{key}={value}");
                if (!appliedKeys.ContainsKey(section))
                    appliedKeys[section] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                appliedKeys[section].Add(key);
            }
        }
    }

    private static Dictionary<string, string> BuildSectionMods(params (string Key, string? Value)[] fields)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            if (value is not null)
                dict[key] = value;
        return dict;
    }

    private static void AddEngineKeyModification(
        Dictionary<string, Dictionary<string, string>> modifications,
        string section, string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!modifications.ContainsKey(section))
            modifications[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        modifications[section][key] = value;
    }

    private static void AddEngineKeyModifications(
        Dictionary<string, Dictionary<string, string>> modifications,
        string section, params (string Key, string? Value)[] fields)
    {
        foreach (var (key, value) in fields)
            AddEngineKeyModification(modifications, section, key, value);
    }

    private static void AppendEngineSection(StringBuilder sb, string section,
        params (string Key, string? Value)[] fields)
    {
        if (fields.All(f => string.IsNullOrEmpty(f.Value))) return;
        sb.AppendLine();
        sb.AppendLine($"[{section}]");
        foreach (var (key, value) in fields)
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"{key}={value}");
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1];
                result.TryAdd(currentSection, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                continue;
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();

            if (!result.ContainsKey(currentSection))
                result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            result[currentSection][key] = value;
        }

        return result;
    }

    private static string GetValue(Dictionary<string, Dictionary<string, string>> ini,
        string section, string key, string? defaultValue) =>
        ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : defaultValue ?? "";

    private static string? GetNullableValue(Dictionary<string, Dictionary<string, string>> ini,
        string section, string key) =>
        ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static bool GetBool(Dictionary<string, Dictionary<string, string>> ini,
        string section, string key, bool defaultValue) =>
        ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v)
            ? v.Equals("True", StringComparison.OrdinalIgnoreCase)
            : defaultValue;

    private static int GetInt(Dictionary<string, Dictionary<string, string>> ini,
        string section, string key, int defaultValue) =>
        ini.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && int.TryParse(v, out var i)
            ? i
            : defaultValue;
}
