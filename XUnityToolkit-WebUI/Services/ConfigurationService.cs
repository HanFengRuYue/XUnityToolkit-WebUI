using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ConfigurationService(ILogger<ConfigurationService> logger)
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
            SourceLanguage = GetValue(ini, "General", "FromLanguage", "ja"),
            TargetLanguage = GetValue(ini, "General", "Language", "en"),
            TranslationEngine = GetValue(ini, "Service", "Endpoint", "GoogleTranslateV2"),
            FallbackEndpoint = GetValue(ini, "Service", "FallbackEndpoint", null),
            EnableUGUI = GetBool(ini, "TextFramework", "EnableUGUI", true),
            EnableNGUI = GetBool(ini, "TextFramework", "EnableNGUI", true),
            EnableTextMeshPro = GetBool(ini, "TextFramework", "EnableTextMeshPro", true),
            EnableTextMesh = GetBool(ini, "TextFramework", "EnableTextMesh", false),
            EnableIMGUI = GetBool(ini, "TextFramework", "EnableIMGUI", false),
            MaxCharactersPerTranslation = GetInt(ini, "Behaviour", "MaxCharactersPerTranslation", 200),
            HandleRichText = GetBool(ini, "Behaviour", "HandleRichText", true),
            EnableUIResizing = GetBool(ini, "Behaviour", "EnableUIResizing", true),
            // Engine API credentials
            GoogleTranslateV2ApiKey = GetNullableValue(ini, "GoogleTranslateV2", "GoogleAPIKey"),
            BingTranslateOcpApimSubscriptionKey = GetNullableValue(ini, "BingTranslate", "OcpApimSubscriptionKey"),
            BaiduTranslateAppId = GetNullableValue(ini, "BaiduTranslate", "BaiduAppId"),
            BaiduTranslateAppSecret = GetNullableValue(ini, "BaiduTranslate", "BaiduAppSecret"),
            YandexTranslateApiKey = GetNullableValue(ini, "YandexTranslate", "YandexAPIKey"),
            DeepLTranslateApiKey = GetNullableValue(ini, "DeepLTranslate", "DeepLAPIKey"),
            PapagoTranslateClientId = GetNullableValue(ini, "PapagoTranslate", "PapagoClientId"),
            PapagoTranslateClientSecret = GetNullableValue(ini, "PapagoTranslate", "PapagoClientSecret"),
        };

        logger.LogInformation("已读取配置文件: {Path}", configPath);
        return Task.FromResult(config);
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
            ["TextFramework"] = BuildSectionMods(
                ("EnableUGUI", config.EnableUGUI.ToString()),
                ("EnableNGUI", config.EnableNGUI.ToString()),
                ("EnableTextMeshPro", config.EnableTextMeshPro.ToString()),
                ("EnableTextMesh", config.EnableTextMesh.ToString()),
                ("EnableIMGUI", config.EnableIMGUI.ToString())),
            ["Behaviour"] = BuildSectionMods(
                ("MaxCharactersPerTranslation", config.MaxCharactersPerTranslation.ToString()),
                ("HandleRichText", config.HandleRichText.ToString()),
                ("EnableUIResizing", config.EnableUIResizing.ToString())),
        };

        // Add engine-specific API key sections
        AddEngineKeyModification(modifications, "GoogleTranslateV2", "GoogleAPIKey", config.GoogleTranslateV2ApiKey);
        AddEngineKeyModification(modifications, "BingTranslate", "OcpApimSubscriptionKey", config.BingTranslateOcpApimSubscriptionKey);
        AddEngineKeyModifications(modifications, "BaiduTranslate",
            ("BaiduAppId", config.BaiduTranslateAppId),
            ("BaiduAppSecret", config.BaiduTranslateAppSecret));
        AddEngineKeyModification(modifications, "YandexTranslate", "YandexAPIKey", config.YandexTranslateApiKey);
        AddEngineKeyModification(modifications, "DeepLTranslate", "DeepLAPIKey", config.DeepLTranslateApiKey);
        AddEngineKeyModifications(modifications, "PapagoTranslate",
            ("PapagoClientId", config.PapagoTranslateClientId),
            ("PapagoClientSecret", config.PapagoTranslateClientSecret));

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
        sb.AppendLine("[TextFramework]");
        sb.AppendLine($"EnableUGUI={config.EnableUGUI}");
        sb.AppendLine($"EnableNGUI={config.EnableNGUI}");
        sb.AppendLine($"EnableTextMeshPro={config.EnableTextMeshPro}");
        sb.AppendLine($"EnableTextMesh={config.EnableTextMesh}");
        sb.AppendLine($"EnableIMGUI={config.EnableIMGUI}");
        sb.AppendLine();
        sb.AppendLine("[Behaviour]");
        sb.AppendLine($"MaxCharactersPerTranslation={config.MaxCharactersPerTranslation}");
        sb.AppendLine($"HandleRichText={config.HandleRichText}");
        sb.AppendLine($"EnableUIResizing={config.EnableUIResizing}");

        AppendEngineSection(sb, "GoogleTranslateV2", ("GoogleAPIKey", config.GoogleTranslateV2ApiKey));
        AppendEngineSection(sb, "BingTranslate", ("OcpApimSubscriptionKey", config.BingTranslateOcpApimSubscriptionKey));
        AppendEngineSection(sb, "BaiduTranslate", ("BaiduAppId", config.BaiduTranslateAppId), ("BaiduAppSecret", config.BaiduTranslateAppSecret));
        AppendEngineSection(sb, "YandexTranslate", ("YandexAPIKey", config.YandexTranslateApiKey));
        AppendEngineSection(sb, "DeepLTranslate", ("DeepLAPIKey", config.DeepLTranslateApiKey));
        AppendEngineSection(sb, "PapagoTranslate", ("PapagoClientId", config.PapagoTranslateClientId), ("PapagoClientSecret", config.PapagoTranslateClientSecret));

        File.WriteAllText(configPath, sb.ToString());
        logger.LogInformation("已写入配置文件: {Path}", configPath);
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
            if (!applied.Contains(key) && !string.IsNullOrEmpty(value))
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
            if (!string.IsNullOrEmpty(value))
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
