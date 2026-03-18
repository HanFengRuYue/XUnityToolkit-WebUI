using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class TermExtractionService(
    LlmTranslationService translationService,
    TermService termService,
    AppSettingsService settingsService,
    AppDataPaths paths,
    ILogger<TermExtractionService> logger)
{
    private readonly ConcurrentDictionary<string, TermCandidateStore> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private const int BatchSize = 20;
    private const int MaxExtractionPairs = 200;

    private const string ExtractionPrompt =
        "你是一名专业的游戏术语提取专家。根据以下游戏文本的原文和译文对，提取出有价值的术语。\n\n" +
        "请提取以下类型的术语：\n" +
        "1. 角色名、人名 (category: \"character\")\n" +
        "2. 地名、场所名 (category: \"location\")\n" +
        "3. 物品名、道具名 (category: \"item\")\n" +
        "4. 技能名、魔法名 (category: \"skill\")\n" +
        "5. 组织名、种族名 (category: \"organization\")\n" +
        "6. 其他游戏特有的专有名词 (category: \"general\")\n\n" +
        "要求：\n" +
        "- 只提取确实是专有名词的术语，不要提取普通词汇\n" +
        "- 每个术语给出原文和对应的翻译\n" +
        "- 输出 JSON 数组格式：[{\"original\": \"原文\", \"translation\": \"译文\", \"category\": \"character\"}]\n" +
        "- 只输出 JSON 数组，不要其他说明";


    /// <summary>
    /// Extract term candidates from translation pairs via LLM.
    /// Deduplicates against existing terms and saves candidates for review.
    /// </summary>
    public async Task<List<TermCandidate>> ExtractFromPairsAsync(
        string gameId,
        IList<(string original, string translation)> pairs,
        CancellationToken ct,
        Action<int, int>? onBatchProgress = null)
    {
        var settings = await settingsService.GetAsync(ct);
        if (!settings.AiTranslation.EnableAutoTermExtraction)
        {
            logger.LogDebug("自动术语提取已禁用，跳过");
            return [];
        }

        var ai = settings.AiTranslation;
        var endpoint = EndpointSelector.SelectBestEndpoint(ai.Endpoints);

        if (endpoint is null)
        {
            logger.LogWarning("术语提取: 没有可用的 AI 提供商");
            return [];
        }

        // Sample to avoid excessive LLM calls (200 pairs = 10 batches)
        if (pairs.Count > MaxExtractionPairs)
        {
            logger.LogInformation("术语提取: 从 {Total} 对中采样 {Sample} 对, 游戏 {GameId}",
                pairs.Count, MaxExtractionPairs, gameId);
            pairs = pairs.Take(MaxExtractionPairs).ToList();
        }

        // Process in batches
        var allCandidates = new List<TermCandidate>();
        var totalBatches = (int)Math.Ceiling((double)pairs.Count / BatchSize);
        for (var i = 0; i < pairs.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = pairs.Skip(i).Take(BatchSize).ToList();
            var batchIndex = i / BatchSize + 1;
            try
            {
                var batchCandidates = await ExtractBatchAsync(endpoint, batch, ct);
                allCandidates.AddRange(batchCandidates);
                logger.LogInformation("术语提取: 批次 {Current}/{Total} 完成, 提取 {Count} 条候选",
                    batchIndex, totalBatches, batchCandidates.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "术语提取批次失败 (批次起始: {Start})", i);
            }

            onBatchProgress?.Invoke(batchIndex, totalBatches);
        }

        if (allCandidates.Count == 0)
        {
            logger.LogInformation("术语提取: 游戏 {GameId} 未提取到术语候选", gameId);
            return [];
        }

        // Deduplicate: same Original → keep most frequent translation
        var grouped = allCandidates
            .GroupBy(c => c.Original, StringComparer.Ordinal)
            .Select(g =>
            {
                var best = g.GroupBy(c => c.Translation, StringComparer.Ordinal)
                    .OrderByDescending(tg => tg.Count())
                    .First();
                var representative = best.First();
                return representative with { Frequency = best.Count() };
            })
            .ToList();

        // Remove candidates that already exist in term table
        var existingTerms = await termService.GetAsync(gameId, ct);
        var existingOriginals = new HashSet<string>(
            existingTerms.Select(e => e.Original), StringComparer.Ordinal);
        var filtered = grouped.Where(c => !existingOriginals.Contains(c.Original)).ToList();

        if (filtered.Count == 0)
        {
            logger.LogInformation("术语提取: 游戏 {GameId} 所有候选术语已存在于术语表中", gameId);
            return [];
        }

        // Save to file
        var store = new TermCandidateStore
        {
            Candidates = filtered,
            ExtractedAt = DateTime.UtcNow
        };
        await SaveStoreAsync(gameId, store, ct);
        _cache[gameId] = store;

        logger.LogInformation("术语提取: 游戏 {GameId} 提取了 {Count} 条术语候选", gameId, filtered.Count);
        return filtered;
    }

    /// <summary>
    /// Get term candidates for a game (from cache or disk).
    /// </summary>
    public async Task<TermCandidateStore> GetCandidatesAsync(string gameId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(gameId, out var cached))
            return cached;

        var file = paths.TermCandidatesFile(gameId);
        if (!File.Exists(file))
            return new TermCandidateStore();

        try
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var store = JsonSerializer.Deserialize<TermCandidateStore>(json, FileHelper.DataJsonOptions)
                ?? new TermCandidateStore();
            _cache[gameId] = store;
            return store;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new TermCandidateStore();
        }
    }

    /// <summary>
    /// Apply selected (or all) candidates to the term table.
    /// Returns the number of terms actually applied.
    /// </summary>
    public async Task<int> ApplyCandidatesAsync(
        string gameId,
        IList<string>? selectedOriginals,
        CancellationToken ct = default)
    {
        var store = await GetCandidatesAsync(gameId, ct);
        if (store.Candidates.Count == 0)
            return 0;

        var toApply = selectedOriginals is not null
            ? store.Candidates.Where(c => selectedOriginals.Contains(c.Original)).ToList()
            : store.Candidates;

        if (toApply.Count == 0)
            return 0;

        // Convert candidates to TermEntry
        var newEntries = toApply.Select(c => new TermEntry
        {
            Type = TermType.Translate,
            Original = c.Original,
            Translation = c.Translation,
            Category = c.Category,
            Source = TermSource.AI,
            Priority = 0
        }).ToList();

        // Get existing terms to skip duplicates
        var existingTerms = await termService.GetAsync(gameId, ct);
        var existingOriginals = new HashSet<string>(
            existingTerms.Select(e => e.Original), StringComparer.Ordinal);
        var unique = newEntries.Where(e => !existingOriginals.Contains(e.Original)).ToList();

        if (unique.Count > 0)
            await termService.MergeAsync(gameId, unique, ct);

        // Clean up candidates file
        await DeleteCandidatesAsync(gameId);

        logger.LogInformation("术语提取: 游戏 {GameId} 应用了 {Count} 条术语（已跳过 {Skipped} 条重复）",
            gameId, unique.Count, newEntries.Count - unique.Count);
        return unique.Count;
    }

    /// <summary>
    /// Delete candidates file and remove from cache.
    /// </summary>
    public Task DeleteCandidatesAsync(string gameId)
    {
        _cache.TryRemove(gameId, out _);
        var file = paths.TermCandidatesFile(gameId);
        if (File.Exists(file))
        {
            try { File.Delete(file); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "删除术语候选文件失败: {GameId}", gameId);
            }
        }
        return Task.CompletedTask;
    }

    public int GetCandidateCount(string gameId)
    {
        return _cache.TryGetValue(gameId, out var store) ? store.Candidates.Count : 0;
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
        foreach (var sem in _locks.Values)
            sem.Dispose();
        _locks.Clear();
    }

    private async Task<List<TermCandidate>> ExtractBatchAsync(
        ApiEndpointConfig endpoint,
        List<(string original, string translation)> pairs,
        CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("原文 → 译文：");
            for (var i = 0; i < pairs.Count; i++)
            {
                sb.AppendLine($"[{i}] {pairs[i].original} → {pairs[i].translation}");
            }

            var (content, _) = await translationService.CallLlmRawAsync(
                endpoint, ExtractionPrompt, sb.ToString(), 0.1, ct);

            return ParseExtractionResult(content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "术语提取批次调用失败");
            return [];
        }
    }

    private List<TermCandidate> ParseExtractionResult(string content)
    {
        var json = LlmResponseParser.ExtractJsonArray(content);

        try
        {
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null) return [];

            var result = new List<TermCandidate>();
            foreach (var item in arr)
            {
                var original = item?["original"]?.GetValue<string>();
                var translation = item?["translation"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation))
                    continue;

                var categoryStr = item?["category"]?.GetValue<string>();
                var category = TermCategory.General;
                if (!string.IsNullOrWhiteSpace(categoryStr) && TermCategoryMapping.FromString.TryGetValue(categoryStr, out var cat))
                    category = cat;

                result.Add(new TermCandidate
                {
                    Original = original,
                    Translation = translation,
                    Category = category
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            var preview = content.Length > 200 ? content[..200] + "..." : content;
            logger.LogDebug(ex, "术语提取结果JSON解析失败: \"{Preview}\"", preview);
            return [];
        }
    }

    private async Task SaveStoreAsync(string gameId, TermCandidateStore store, CancellationToken ct)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            var file = paths.TermCandidatesFile(gameId);
            await FileHelper.WriteJsonAtomicAsync(file, store, ct: ct);
        }
        finally
        {
            gameLock.Release();
        }
    }
}
