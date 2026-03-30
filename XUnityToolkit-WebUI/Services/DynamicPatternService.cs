using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class DynamicPatternService(
    LlmTranslationService translationService,
    AppSettingsService settingsService,
    TranslationMemoryService translationMemoryService,
    AppDataPaths paths,
    ILogger<DynamicPatternService> logger)
{
    private const int LlmBatchSize = 20;
    private const int MinLiteralChars = 3;
    private const int MaxAnalysisPairs = 200; // 10 batches max

    private readonly ConcurrentDictionary<string, DynamicPatternStore> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // ── Template variable detection (regex-based, zero LLM cost) ──

    [GeneratedRegex(@"\{[A-Za-z_]\w*\}")]
    private static partial Regex CurlyBraceVarRegex();

    [GeneratedRegex(@"<[A-Za-z_]\w*>")]
    private static partial Regex AngleBracketVarRegex();

    [GeneratedRegex(@"%[A-Za-z_]\w*%")]
    private static partial Regex PercentVarRegex();

    [GeneratedRegex(@"\[[A-Za-z_]\w*\]")]
    private static partial Regex SquareBracketVarRegex();

    /// <summary>
    /// Scan texts for common template variable patterns.
    /// Returns list of (index, text, variables) for texts that contain variables.
    /// </summary>
    public List<(int Index, string Text, List<(int Start, int End, string Variable)> Variables)>
        DetectTemplateVariables(IList<string> texts)
    {
        var results = new List<(int, string, List<(int, int, string)>)>();
        Regex[] patterns = [CurlyBraceVarRegex(), AngleBracketVarRegex(), PercentVarRegex(), SquareBracketVarRegex()];

        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text)) continue;

            var variables = new List<(int Start, int End, string Variable)>();

            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    // Check for overlap with existing variables
                    var start = match.Index;
                    var end = match.Index + match.Length;
                    if (variables.Any(v => v.Start < end && v.End > start))
                        continue;

                    variables.Add((start, end, match.Value));
                }
            }

            if (variables.Count > 0)
            {
                variables.Sort((a, b) => a.Start.CompareTo(b.Start));
                results.Add((i, text, variables));
            }
        }

        return results;
    }

    // ── Regex entry generation ──

    /// <summary>
    /// Build XUnity r:"pattern"=replacement entries from texts with identified variable positions.
    /// Replaces variable positions with (.+?), escapes the rest as regex literals,
    /// and builds $1/$2 backreference templates.
    /// Safety: requires at least <see cref="MinLiteralChars"/> literal characters.
    /// </summary>
    public List<(string RegexPattern, string Replacement)> GenerateRegexEntries(
        IList<(string Original, string Translation, List<(int Start, int End, string Variable)> Variables)> entries)
    {
        var results = new List<(string, string)>();

        foreach (var (original, translation, variables) in entries)
        {
            if (variables.Count == 0) continue;

            // Build regex pattern from original
            var patternBuilder = new StringBuilder();
            patternBuilder.Append('^');
            var literalLength = 0;
            var lastEnd = 0;
            var groupIndex = 0;

            foreach (var (start, end, _) in variables.OrderBy(v => v.Start))
            {
                if (start > lastEnd)
                {
                    var literal = Regex.Escape(original[lastEnd..start]);
                    patternBuilder.Append(literal);
                    literalLength += start - lastEnd;
                }

                patternBuilder.Append("(.+?)");
                groupIndex++;
                lastEnd = end;
            }

            if (lastEnd < original.Length)
            {
                var literal = Regex.Escape(original[lastEnd..]);
                patternBuilder.Append(literal);
                literalLength += original.Length - lastEnd;
            }

            patternBuilder.Append('$');

            // Safety check: at least MinLiteralChars literal characters
            if (literalLength < MinLiteralChars) continue;

            // Build replacement template from translation
            var replacement = BuildSubstitutionTemplate(translation,
                variables.Select(v => (v.Start, v.End)), original);
            if (replacement is null) continue;

            results.Add((patternBuilder.ToString(), replacement));
        }

        return results;
    }

    // ── LLM-assisted dynamic fragment analysis ──

    /// <summary>
    /// Batch-analyze text pairs with LLM to identify dynamic fragments.
    /// Generates DynamicPattern entries, saves to file, and loads into TranslationMemoryService.
    /// </summary>
    public async Task<int> AnalyzeDynamicFragmentsAsync(
        string gameId,
        IList<(string Original, string Translation)> pairs,
        CancellationToken ct,
        Action<int, int>? onBatchProgress = null)
    {
        if (pairs.Count == 0) return 0;

        // Sample to avoid excessive LLM calls (200 pairs = 10 batches)
        if (pairs.Count > MaxAnalysisPairs)
        {
            logger.LogInformation("动态模式分析: 从 {Total} 对中采样 {Sample} 对, 游戏 {GameId}",
                pairs.Count, MaxAnalysisPairs, gameId);
            pairs = pairs.Take(MaxAnalysisPairs).ToList();
        }

        var settings = await settingsService.GetAsync();
        var endpoint = EndpointSelector.SelectBestEndpoint(settings.AiTranslation.Endpoints);

        if (endpoint is null)
        {
            logger.LogWarning("动态模式分析: 没有可用的 AI 端点");
            return 0;
        }

        var allPatterns = new List<DynamicPattern>();
        var totalBatches = (int)Math.Ceiling((double)pairs.Count / LlmBatchSize);

        // Process in batches
        for (var batchStart = 0; batchStart < pairs.Count; batchStart += LlmBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchEnd = Math.Min(batchStart + LlmBatchSize, pairs.Count);
            var batch = pairs.Skip(batchStart).Take(batchEnd - batchStart).ToList();
            var batchIndex = batchStart / LlmBatchSize + 1;

            try
            {
                var batchPatterns = await AnalyzeBatchAsync(endpoint, batch, ct);
                allPatterns.AddRange(batchPatterns);
                logger.LogInformation("动态模式分析: 批次 {Current}/{Total} 完成, 发现 {Count} 个模式",
                    batchIndex, totalBatches, batchPatterns.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "动态模式分析批次失败 (批次起始: {Start})", batchStart);
            }

            onBatchProgress?.Invoke(batchIndex, totalBatches);
        }

        if (allPatterns.Count == 0)
        {
            logger.LogInformation("动态模式分析完成，未发现动态模式: {GameId}", gameId);
            return 0;
        }

        // Save and load patterns
        var store = new DynamicPatternStore { Patterns = allPatterns };
        await SaveStoreAsync(gameId, store, ct);

        var compiled = CompilePatterns(allPatterns, logger);
        translationMemoryService.LoadPatterns(gameId, compiled);

        logger.LogInformation("动态模式分析完成: {GameId}, 发现 {Count} 个模式", gameId, allPatterns.Count);
        return allPatterns.Count;
    }

    // ── Pattern access ──

    public async Task<DynamicPatternStore> GetPatternsAsync(string gameId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(gameId, out var cached))
            return cached;

        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(gameId, out cached))
                return cached;

            var file = paths.DynamicPatternsFile(gameId);
            if (!File.Exists(file))
            {
                var empty = new DynamicPatternStore();
                _cache[gameId] = empty;
                return empty;
            }

            var json = await File.ReadAllTextAsync(file, ct);
            var store = JsonSerializer.Deserialize<DynamicPatternStore>(json, FileHelper.DataJsonOptions)
                        ?? new DynamicPatternStore();
            _cache[gameId] = store;
            return store;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "加载动态模式失败: {GameId}", gameId);
            var empty = new DynamicPatternStore();
            _cache[gameId] = empty;
            return empty;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public async Task DeleteAsync(string gameId, CancellationToken ct = default)
    {
        _cache.TryRemove(gameId, out _);
        var file = paths.DynamicPatternsFile(gameId);
        if (File.Exists(file))
        {
            await Task.Run(() => File.Delete(file), ct);
            logger.LogInformation("已删除游戏 {GameId} 的动态模式", gameId);
        }
    }

    public void RemoveCache(string gameId)
    {
        _cache.TryRemove(gameId, out _);
        if (_locks.TryRemove(gameId, out var sem))
            sem.Dispose();
    }

    public void ClearAllCache()
    {
        _cache.Clear();
        var semaphores = _locks.Values.ToList();
        _locks.Clear();
        foreach (var sem in semaphores)
            sem.Dispose();
    }

    public int GetPatternCount(string gameId)
    {
        return _cache.TryGetValue(gameId, out var store) ? store.Patterns.Count : 0;
    }

    // ── Internal helpers ──

    private async Task<List<DynamicPattern>> AnalyzeBatchAsync(
        ApiEndpointConfig endpoint,
        List<(string Original, string Translation)> batch,
        CancellationToken ct)
    {
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("以下是从游戏中提取的对话文本及其翻译。请识别其中可能是动态变量的部分（如玩家名、自定义角色名等会随玩家输入变化的内容）。");
        promptBuilder.AppendLine("对每段文本，输出 JSON 数组。如果没有动态部分，该项为 null。");
        promptBuilder.AppendLine("只输出 JSON 数组，不要其他内容。");
        promptBuilder.AppendLine("格式: [{\"index\": 0, \"variables\": [{\"start\": 0, \"end\": 2, \"type\": \"playerName\"}]}, ...]");
        promptBuilder.AppendLine();

        for (var i = 0; i < batch.Count; i++)
        {
            var (original, translation) = batch[i];
            promptBuilder.AppendLine($"[{i}] 原文: {original} | 译文: {translation}");
        }

        var (content, _) = await translationService.CallLlmRawAsync(
            endpoint,
            "你是一个游戏文本分析助手。你的任务是识别游戏文本中的动态变量部分。",
            promptBuilder.ToString(),
            0.1,
            ct);

        return ParseLlmResponse(content, batch);
    }

    private List<DynamicPattern> ParseLlmResponse(
        string content,
        List<(string Original, string Translation)> batch)
    {
        var patterns = new List<DynamicPattern>();

        try
        {
            var json = LlmResponseParser.ExtractJsonContent(content);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array) return patterns;

            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                if (!item.TryGetProperty("index", out var indexProp)) continue;
                var index = indexProp.GetInt32();
                if (index < 0 || index >= batch.Count) continue;

                if (!item.TryGetProperty("variables", out var variablesProp) ||
                    variablesProp.ValueKind != JsonValueKind.Array)
                    continue;

                var variables = new List<(int Start, int End, string Type)>();
                foreach (var v in variablesProp.EnumerateArray())
                {
                    if (v.ValueKind != JsonValueKind.Object) continue;

                    var start = v.TryGetProperty("start", out var sp) ? sp.GetInt32() : -1;
                    var end = v.TryGetProperty("end", out var ep) ? ep.GetInt32() : -1;
                    var type = v.TryGetProperty("type", out var tp) ? tp.GetString() ?? "unknown" : "unknown";

                    if (start < 0 || end <= start) continue;

                    var (original, _) = batch[index];
                    if (end > original.Length) continue;

                    variables.Add((start, end, type));
                }

                if (variables.Count == 0) continue;

                var (orig, trans) = batch[index];
                var varPositions = variables
                    .OrderBy(v => v.Start)
                    .Select((v, i) => new VariablePosition { Type = v.Type, GroupIndex = i + 1 })
                    .ToList();

                // Build original template (replace variable spans with placeholders)
                var origTemplate = BuildTemplate(orig, variables);
                var transTemplate = BuildSubstitutionTemplate(trans,
                    variables.Select(v => (v.Start, v.End)), orig);

                if (origTemplate is not null && transTemplate is not null)
                {
                    patterns.Add(new DynamicPattern
                    {
                        OriginalTemplate = origTemplate,
                        TranslatedTemplate = transTemplate,
                        VariablePositions = varPositions,
                        Source = "llm-analysis"
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "解析 LLM 动态模式响应失败");
        }

        return patterns;
    }

    private static string? BuildTemplate(string text, List<(int Start, int End, string Type)> variables)
    {
        var sorted = variables.OrderBy(v => v.Start).ToList();
        var sb = new StringBuilder();
        var lastEnd = 0;
        var groupIdx = 0;

        foreach (var (start, end, _) in sorted)
        {
            if (start > lastEnd)
                sb.Append(text[lastEnd..start]);
            groupIdx++;
            sb.Append($"${groupIdx}");
            lastEnd = end;
        }

        if (lastEnd < text.Length)
            sb.Append(text[lastEnd..]);

        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Build a substitution template by replacing variable spans in the translation
    /// with $N backreferences. Used by both regex entry generation and LLM pattern analysis.
    /// </summary>
    private static string? BuildSubstitutionTemplate(
        string translation,
        IEnumerable<(int Start, int End)> variableSpans,
        string original)
    {
        var sorted = variableSpans.OrderBy(v => v.Start).ToList();
        var result = translation;

        // Replace variable values in translation with backreferences (reverse order to preserve positions)
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var (start, end) = sorted[i];
            var varValue = original[start..end];

            var pos = result.IndexOf(varValue, StringComparison.Ordinal);
            if (pos >= 0)
            {
                result = string.Concat(
                    result.AsSpan(0, pos),
                    $"${i + 1}",
                    result.AsSpan(pos + varValue.Length));
            }
            else
            {
                // Variable value not found in translation — cannot build safe template
                return null;
            }
        }

        return result;
    }

    /// <summary>
    /// Compile DynamicPattern entries into regex + replacement tuples for TranslationMemoryService.
    /// </summary>
    internal static List<(Regex Pattern, string TranslatedTemplate, List<VariablePosition> Variables)>
        CompilePatterns(List<DynamicPattern> patterns, ILogger? log = null)
    {
        var results = new List<(Regex, string, List<VariablePosition>)>();

        foreach (var p in patterns)
        {
            try
            {
                // Build regex from OriginalTemplate: escape literals, replace $N with (.+?)
                var regexStr = BuildRegexFromTemplate(p.OriginalTemplate);
                if (regexStr is null) continue;

                var regex = new Regex(regexStr, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                results.Add((regex, p.TranslatedTemplate, p.VariablePositions));
            }
            catch (RegexParseException ex)
            {
                log?.LogWarning("跳过无效的动态模式正则: {Template}, 错误: {Error}",
                    p.OriginalTemplate, ex.Message);
            }
        }

        return results;
    }

    [GeneratedRegex(@"\$(\d+)")]
    private static partial Regex BackreferenceRegex();

    private static string? BuildRegexFromTemplate(string template)
    {
        // Split template by $N backreferences, escape literal parts, reconstruct with (.+?)
        var parts = BackreferenceRegex().Split(template);
        if (parts.Length <= 1) return null; // No backreferences found

        var sb = new StringBuilder();
        sb.Append('^');
        var literalLength = 0;

        for (var i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0)
            {
                // Literal part
                var escaped = Regex.Escape(parts[i]);
                sb.Append(escaped);
                literalLength += parts[i].Length;
            }
            else
            {
                // Backreference number — replace with capture group
                sb.Append("(.+?)");
            }
        }

        sb.Append('$');

        return literalLength >= MinLiteralChars ? sb.ToString() : null;
    }

    private async Task SaveStoreAsync(string gameId, DynamicPatternStore store, CancellationToken ct)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            var file = paths.DynamicPatternsFile(gameId);
            await FileHelper.WriteJsonAtomicAsync(file, store, ct: ct);
            _cache[gameId] = store;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "保存动态模式失败: {GameId}", gameId);
        }
        finally
        {
            gameLock.Release();
        }
    }
}
