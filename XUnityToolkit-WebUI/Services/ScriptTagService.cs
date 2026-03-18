using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class ScriptTagService(
    AppDataPaths paths,
    BundledAssetPaths bundledPaths,
    ILogger<ScriptTagService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, ScriptTagConfig> _cache = new();
    private readonly ConcurrentDictionary<string, CompiledRuleSet> _compiled = new();
    private volatile ScriptTagPreset? _presetCache;


    private sealed class CompiledRuleSet
    {
        public required List<(Regex Regex, ScriptTagAction Action)> Rules { get; init; }
    }

    // ── Preset loading ──

    public ScriptTagPreset LoadPreset()
    {
        if (_presetCache is not null) return _presetCache;

        var file = bundledPaths.ScriptTagPresetsFile;
        if (!File.Exists(file))
        {
            _presetCache = new ScriptTagPreset { Version = 0, Rules = [] };
            return _presetCache;
        }

        var json = File.ReadAllText(file);
        _presetCache = JsonSerializer.Deserialize<ScriptTagPreset>(json, FileHelper.DataJsonOptions)
            ?? new ScriptTagPreset { Version = 0, Rules = [] };
        return _presetCache;
    }

    // ── Config CRUD ──

    public async Task<ScriptTagConfig> GetAsync(string gameId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(gameId, out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(gameId, out cached)) return cached;

            var file = paths.ScriptTagFile(gameId);
            if (!File.Exists(file))
            {
                // Return preset defaults in-memory, do NOT write to disk
                var preset = LoadPreset();
                var defaults = new ScriptTagConfig
                {
                    PresetVersion = preset.Version,
                    Rules = preset.Rules.Select(r => new ScriptTagRule
                    {
                        Pattern = r.Pattern,
                        Action = r.Action,
                        Description = r.Description,
                        IsBuiltin = true
                    }).ToList()
                };
                _cache[gameId] = defaults;
                EnsureCompiled(gameId, defaults);
                return defaults;
            }

            var json = await File.ReadAllTextAsync(file, ct);
            var config = JsonSerializer.Deserialize<ScriptTagConfig>(json, FileHelper.DataJsonOptions)
                ?? new ScriptTagConfig();

            // Auto-update: if stored preset version is behind bundled version
            var currentPreset = LoadPreset();
            if (config.PresetVersion < currentPreset.Version)
            {
                config.Rules.RemoveAll(r => r.IsBuiltin);
                var builtinRules = currentPreset.Rules.Select(r => new ScriptTagRule
                {
                    Pattern = r.Pattern,
                    Action = r.Action,
                    Description = r.Description,
                    IsBuiltin = true
                }).ToList();
                config.Rules.InsertRange(0, builtinRules);
                config.PresetVersion = currentPreset.Version;

                // Write back updated config
                var updatedJson = JsonSerializer.Serialize(config, FileHelper.DataJsonOptions);
                var tmpPath = file + ".tmp";
                await File.WriteAllTextAsync(tmpPath, updatedJson, ct);
                File.Move(tmpPath, file, overwrite: true);
                logger.LogInformation("已自动更新游戏 {GameId} 的内置脚本标签规则至版本 {Version}",
                    gameId, currentPreset.Version);
            }

            _cache[gameId] = config;
            EnsureCompiled(gameId, config);
            return config;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string gameId, ScriptTagConfig config, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var file = paths.ScriptTagFile(gameId);
            var json = JsonSerializer.Serialize(config, FileHelper.DataJsonOptions);
            var tmpPath = file + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, file, overwrite: true);
            _cache[gameId] = config;
            EnsureCompiled(gameId, config);
            logger.LogInformation("已保存游戏 {GameId} 的脚本标签规则: {Count} 条", gameId, config.Rules.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RemoveCache(string gameId)
    {
        _cache.TryRemove(gameId, out _);
        _compiled.TryRemove(gameId, out _);
    }

    public void ClearAllCache()
    {
        _cache.Clear();
        _compiled.Clear();
    }

    // ── Core cleaning methods ──

    public string? CleanText(string gameId, string text)
    {
        if (!_compiled.TryGetValue(gameId, out var ruleSet) || ruleSet.Rules.Count == 0)
            return text;

        foreach (var (regex, action) in ruleSet.Rules)
        {
            var match = regex.Match(text);
            if (!match.Success) continue;

            return action switch
            {
                ScriptTagAction.Exclude => null,
                ScriptTagAction.Extract => match.Groups.Count > 1
                    ? match.Groups[1].Value
                    : text,
                _ => text
            };
        }

        return text; // No match → return unchanged
    }

    public async Task<List<string>> FilterAndCleanAsync(
        string gameId, List<ExtractedText> texts, CancellationToken ct = default)
    {
        await GetAsync(gameId, ct); // Ensure rules are loaded & compiled

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var item in texts)
        {
            var cleaned = CleanText(gameId, item.Text);
            if (cleaned is not null && !string.IsNullOrWhiteSpace(cleaned) && seen.Add(cleaned))
                result.Add(cleaned);
        }

        return result;
    }

    public string NormalizeForCache(string gameId, string text)
    {
        var stripped = XUnityTranslationFormat.NormalizeForCache(text);
        return CleanText(gameId, stripped) ?? "";
    }

    // ── Internal ──

    private void EnsureCompiled(string gameId, ScriptTagConfig config)
    {
        var compiled = new List<(Regex, ScriptTagAction)>();
        foreach (var rule in config.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                var regex = new Regex(rule.Pattern,
                    RegexOptions.Compiled,
                    TimeSpan.FromSeconds(1));
                compiled.Add((regex, rule.Action));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "跳过无效的脚本标签正则: {Pattern}", rule.Pattern);
            }
        }

        _compiled[gameId] = new CompiledRuleSet { Rules = compiled };
    }
}

// Preset model (for bundled JSON deserialization)
public sealed class ScriptTagPreset
{
    public int Version { get; set; }
    public List<ScriptTagPresetRule> Rules { get; set; } = [];
}

public sealed class ScriptTagPresetRule
{
    public string Pattern { get; set; } = "";
    public ScriptTagAction Action { get; set; } = ScriptTagAction.Extract;
    public string? Description { get; set; }
}
