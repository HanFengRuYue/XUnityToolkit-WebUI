using System.Globalization;
using System.Text;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services.CharacterSets;

namespace UnityLocalizationToolkit_WebUI.Services;

public class CharacterSetService(
    AppDataPaths appDataPaths,
    GameLibraryService gameLibrary,
    ILogger<CharacterSetService> logger)
{
    public List<CharsetInfo> GetBuiltinCharsets() => BuiltinCharsets.GetAllCharsetInfos();

    public async Task<(HashSet<int> Characters, CharacterSetPreview Preview)> ResolveCharactersAsync(
        CharacterSetConfig config, IReadOnlySet<int>? preEnumeratedFontChars = null)
    {
        // UseAllFontCharacters mode: use only the characters from the font's cmap table
        // Do NOT add ASCII/CommonPunctuation — the font's cmap already includes whatever it supports
        if (config.UseAllFontCharacters && preEnumeratedFontChars != null)
        {
            var fontMerged = new HashSet<int>(preEnumeratedFontChars);

            var fontPreview = new CharacterSetPreview
            {
                TotalCharacters = fontMerged.Count,
                SourceBreakdown = new Dictionary<string, int> { ["FontGlyphs"] = fontMerged.Count },
                Warnings = []
            };
            return (fontMerged, fontPreview);
        }

        var merged = new HashSet<int>();
        var breakdown = new Dictionary<string, int>();
        var warnings = new List<string>();

        // Always include ASCII and common punctuation
        merged.UnionWith(BuiltinCharsets.Ascii());
        merged.UnionWith(BuiltinCharsets.CommonPunctuation());

        // Built-in sets
        var selectedSupersets = new HashSet<string>();
        foreach (var setId in config.BuiltinSets ?? [])
        {
            if (BuiltinCharsets.SupersetOf.TryGetValue(setId, out var subsets))
                selectedSupersets.Add(setId);

            var chars = EnumerateBuiltin(setId);
            if (chars == null)
            {
                logger.LogWarning("Unknown built-in charset: {SetId}", setId);
                continue;
            }
            var countBefore = merged.Count;
            merged.UnionWith(chars);
            breakdown[setId] = merged.Count - countBefore;
        }

        // Generate superset warnings
        foreach (var (superset, subsets) in BuiltinCharsets.SupersetOf)
        {
            if (!selectedSupersets.Contains(superset)) continue;
            foreach (var sub in subsets)
            {
                if ((config.BuiltinSets ?? []).Contains(sub))
                    warnings.Add($"{superset} 已包含 {sub} 的全部字符");
            }
        }

        // Custom charset file
        if (!string.IsNullOrEmpty(config.CustomCharsetFileName))
        {
            var path = Path.Combine(appDataPaths.FontGenerationCharsetUploadsDirectory,
                Path.GetFileName(config.CustomCharsetFileName));
            if (File.Exists(path))
            {
                var chars = ParseCustomCharsetFile(path);
                var countBefore = merged.Count;
                merged.UnionWith(chars);
                breakdown["CustomCharset"] = merged.Count - countBefore;
            }
        }

        // Translation file extraction
        if (!string.IsNullOrEmpty(config.TranslationGameId))
        {
            try
            {
                var chars = await ExtractFromGameAsync(config.TranslationGameId);
                var countBefore = merged.Count;
                merged.UnionWith(chars);
                breakdown["Translation"] = merged.Count - countBefore;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract characters from game {GameId}", config.TranslationGameId);
                warnings.Add($"从游戏提取翻译字符失败: {ex.Message}");
            }
        }
        else if (!string.IsNullOrEmpty(config.TranslationFileName))
        {
            var path = Path.Combine(appDataPaths.FontGenerationTranslationUploadsDirectory,
                Path.GetFileName(config.TranslationFileName));
            if (File.Exists(path))
            {
                var chars = ExtractFromTranslationFile(path);
                var countBefore = merged.Count;
                merged.UnionWith(chars);
                breakdown["Translation"] = merged.Count - countBefore;
            }
        }

        var preview = new CharacterSetPreview
        {
            TotalCharacters = merged.Count,
            SourceBreakdown = breakdown,
            Warnings = warnings
        };

        return (merged, preview);
    }

    public async Task<HashSet<int>> ExtractFromGameAsync(string gameId)
    {
        var game = await gameLibrary.GetByIdAsync(gameId)
            ?? throw new InvalidOperationException($"Game not found: {gameId}");

        var bepInExConfigPath = Path.Combine(game.GamePath, "BepInEx", "config", "AutoTranslatorConfig.ini");
        var outputFile = "Translation/{Lang}/Text/_AutoGeneratedTranslations.txt";
        var language = "en";

        if (File.Exists(bepInExConfigPath))
        {
            try
            {
                var ini = ParseIni(await File.ReadAllTextAsync(bepInExConfigPath));
                if (ini.TryGetValue("General", out var general) && general.TryGetValue("Language", out var lang))
                    language = lang;
                if (ini.TryGetValue("Files", out var files) && files.TryGetValue("OutputFile", out var of))
                    outputFile = of;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read INI config, using defaults");
            }
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(language, @"^[a-zA-Z0-9_\-]{1,20}$"))
            throw new InvalidOperationException($"Invalid language code in config");

        var resolvedPath = outputFile.Replace("{Lang}", language);
        var fullPath = Path.GetFullPath(Path.Combine(game.GamePath, "BepInEx", resolvedPath));
        var bepInExRoot = Path.GetFullPath(Path.Combine(game.GamePath, "BepInEx"));
        if (!fullPath.StartsWith(bepInExRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Translation file path traversal detected");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Translation file not found: {resolvedPath}");

        return ExtractFromTranslationFile(fullPath);
    }

    public HashSet<int> ExtractFromTranslationFile(string filePath)
    {
        var set = new HashSet<int>();
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;

            var eqIdx = FindUnescapedEquals(line);
            if (eqIdx < 0) continue;

            var valuePart = line[(eqIdx + 1)..];
            var decoded = XUnityTranslationFormat.Decode(valuePart);

            var enumerator = StringInfo.GetTextElementEnumerator(decoded);
            while (enumerator.MoveNext())
            {
                var element = enumerator.GetTextElement();
                var codepoint = char.ConvertToUtf32(element, 0);
                if (codepoint > 0xFFFF || !char.IsWhiteSpace((char)codepoint))
                    set.Add(codepoint);
            }
        }
        return set;
    }

    public HashSet<int> ParseCustomCharsetFile(string filePath)
    {
        var text = File.ReadAllText(filePath, Encoding.UTF8);
        var set = new HashSet<int>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var codepoint = char.ConvertToUtf32(element, 0);
            if (codepoint > 0xFFFF || !char.IsWhiteSpace((char)codepoint))
                set.Add(codepoint);
        }
        return set;
    }

    private static HashSet<int>? EnumerateBuiltin(string setId) => setId switch
    {
        "GB2312" => BuiltinCharsets.GB2312(),
        "GBK" => BuiltinCharsets.GBK(),
        "CJK_Common" => BuiltinCharsets.CjkCommon(),
        "CJK_Full" => BuiltinCharsets.CjkFull(),
        "Japanese" => BuiltinCharsets.Japanese(),
        _ => null
    };

    private static int FindUnescapedEquals(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '=') return i;
        }
        return -1;
    }

    private static Dictionary<string, Dictionary<string, string>> ParseIni(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                result.TryAdd(currentSection, new(StringComparer.OrdinalIgnoreCase));
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result.TryAdd(currentSection, new(StringComparer.OrdinalIgnoreCase));
            result[currentSection][line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }
}
