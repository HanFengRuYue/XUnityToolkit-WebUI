using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ConfigurationService(ILogger<ConfigurationService> logger)
{
    private const string ConfigFileName = "gravydevsupreme.xunity.autotranslator.cfg";

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
            EnableUIResizing = GetBool(ini, "Behaviour", "EnableUIResizing", true)
        };

        logger.LogInformation("已读取配置文件: {Path}", configPath);
        return Task.FromResult(config);
    }

    public Task SaveAsync(string gamePath, XUnityConfig config, CancellationToken ct = default)
    {
        var configPath = GetConfigPath(gamePath);
        var configDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(configDir);

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

        if (config.Extra.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[Extra]");
            foreach (var (key, value) in config.Extra)
                sb.AppendLine($"{key}={value}");
        }

        File.WriteAllText(configPath, sb.ToString());
        logger.LogInformation("已写入配置文件: {Path}", configPath);
        return Task.CompletedTask;
    }

    private static string GetConfigPath(string gamePath) =>
        Path.Combine(gamePath, "BepInEx", "config", ConfigFileName);

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
