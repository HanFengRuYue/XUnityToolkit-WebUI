using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed partial class PreTranslationService(
    LlmTranslationService translationService,
    AppSettingsService settingsService,
    GameLibraryService gameLibrary,
    ScriptTagService scriptTagService,
    AppDataPaths appDataPaths,
    SystemTrayService trayService,
    DynamicPatternService dynamicPatternService,
    TermExtractionService termExtractionService,
    TranslationMemoryService translationMemoryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<PreTranslationService> logger)
{
    private static readonly string[] PhaseOrder =
    [
        "round1",
        "patternAnalysis",
        "termExtraction",
        "termReview",
        "round2",
        "writeCache"
    ];

    private readonly ConcurrentDictionary<string, PreTranslationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _termReviewCompletions = [];
    private readonly ConcurrentDictionary<string, long> _lastBroadcastTicks = [];
    private readonly ConcurrentDictionary<string, long> _lastCheckpointPersistTicks = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _checkpointLocks = [];
    private static readonly long CheckpointPersistIntervalTicks = TimeSpan.FromSeconds(1).Ticks;

    private sealed class ProgressCounters
    {
        public int Translated;
        public int Failed;
    }

    private sealed record ResilientBatchResult(
        IReadOnlyList<(string Text, string Translation, bool Persistable)> Successes,
        int FailedCount);

    private sealed record ResumeValidationResult(
        List<string>? TextList,
        string? TextSignature,
        string? Error);

    public bool IsPreTranslating => _statuses.Values.Any(s =>
        s.State is PreTranslationState.Running or PreTranslationState.AwaitingTermReview);

    [GeneratedRegex(@"^[a-zA-Z0-9_\-]{1,20}$")]
    private static partial Regex SafeLanguageCodeRegex();

    private PreTranslationStatus GetOrCreateStatus(string gameId) =>
        _statuses.GetOrAdd(gameId, id => new PreTranslationStatus { GameId = id });

    public async Task<PreTranslationStatus> GetStatusAsync(string gameId, CancellationToken ct = default)
    {
        if (_statuses.TryGetValue(gameId, out var liveStatus)
            && liveStatus.State is PreTranslationState.Running or PreTranslationState.AwaitingTermReview)
        {
            liveStatus.CanResume = false;
            liveStatus.ResumeBlockedReason = null;
            return liveStatus;
        }

        var checkpoint = await LoadCheckpointAsync(gameId, ct);
        if (checkpoint is null)
        {
            if (liveStatus is not null)
            {
                liveStatus.CanResume = false;
                liveStatus.ResumeBlockedReason = null;
                liveStatus.CheckpointUpdatedAt = null;
                return liveStatus;
            }

            return new PreTranslationStatus { GameId = gameId };
        }

        var validation = await ValidateCheckpointAsync(gameId, checkpoint, texts: null, ct);
        var status = liveStatus ?? GetOrCreateStatus(gameId);
        ApplyCheckpointToStatus(status, checkpoint,
            canResume: validation.Error is null,
            blockedReason: validation.Error);
        return status;
    }

    public async Task<PreTranslationStatus> StartPreTranslationAsync(
        string gameId, List<ExtractedText> texts, string fromLang, string toLang,
        bool restart = false, CancellationToken ct = default)
    {
        if (!SafeLanguageCodeRegex().IsMatch(toLang))
            throw new ArgumentException("无效的目标语言代码");
        if (!SafeLanguageCodeRegex().IsMatch(fromLang))
            throw new ArgumentException("无效的源语言代码");

        var existingCheckpoint = await LoadCheckpointAsync(gameId, ct);
        if (existingCheckpoint is not null && !restart)
        {
            var validation = await ValidateCheckpointAsync(gameId, existingCheckpoint, texts, ct);
            var message = validation.Error is null
                ? "检测到可继续的预翻译任务，请选择“继续预翻译”或“重新开始”。"
                : $"检测到旧的预翻译检查点：{validation.Error}。如需覆盖，请重新开始预翻译。";
            throw new InvalidOperationException(message);
        }

        if (restart)
            await DeleteCheckpointAsync(gameId, clearInactiveStatus: false, ct);

        await termExtractionService.DeleteCandidatesAsync(gameId);

        return await StartSessionAsync(gameId, texts, fromLang, toLang, checkpoint: null, ct);
    }

    public async Task<PreTranslationStatus> ResumePreTranslationAsync(
        string gameId, List<ExtractedText> texts, CancellationToken ct = default)
    {
        var checkpoint = await LoadCheckpointAsync(gameId, ct)
            ?? throw new InvalidOperationException("没有可继续的预翻译任务。");

        var validation = await ValidateCheckpointAsync(gameId, checkpoint, texts, ct);
        if (validation.Error is not null)
            throw new InvalidOperationException(validation.Error);

        return await StartSessionAsync(gameId, texts, checkpoint.FromLang, checkpoint.ToLang, checkpoint, ct);
    }

    public void Cancel(string gameId)
    {
        if (_cancellations.TryGetValue(gameId, out var cts))
            cts.Cancel();

        if (_termReviewCompletions.TryRemove(gameId, out var tcs))
            tcs.TrySetCanceled();
    }

    public void ResumeAfterTermReview(string gameId)
    {
        if (_termReviewCompletions.TryRemove(gameId, out var tcs))
            tcs.TrySetResult();
    }

    public async Task DeleteCheckpointAsync(
        string gameId,
        bool clearInactiveStatus = false,
        CancellationToken ct = default)
    {
        var checkpointLock = _checkpointLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await checkpointLock.WaitAsync(ct);
        try
        {
            var file = appDataPaths.PreTranslationSessionFile(gameId);
            if (File.Exists(file))
                File.Delete(file);
        }
        finally
        {
            checkpointLock.Release();
        }

        _lastCheckpointPersistTicks.TryRemove(gameId, out _);

        if (_statuses.TryGetValue(gameId, out var status))
        {
            status.CanResume = false;
            status.ResumeBlockedReason = null;
            status.CheckpointUpdatedAt = null;

            if (clearInactiveStatus
                && status.State is not PreTranslationState.Running
                and not PreTranslationState.AwaitingTermReview)
            {
                _statuses.TryRemove(gameId, out _);
            }
        }
    }

    private async Task<PreTranslationStatus> StartSessionAsync(
        string gameId,
        List<ExtractedText> texts,
        string fromLang,
        string toLang,
        PreTranslationCheckpoint? checkpoint,
        CancellationToken ct)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync(ct);
        try
        {
            var status = GetOrCreateStatus(gameId);
            if (status.State is PreTranslationState.Running or PreTranslationState.AwaitingTermReview)
                throw new InvalidOperationException("预翻译任务已在运行中");

            if (checkpoint is null)
            {
                status.State = PreTranslationState.Running;
                status.TotalTexts = texts.Count;
                status.TranslatedTexts = 0;
                status.FailedTexts = 0;
                status.Error = null;
                status.CurrentRound = 0;
                status.CurrentPhase = null;
                status.PhaseProgress = 0;
                status.PhaseTotal = 0;
                status.ExtractedTermCount = 0;
                status.DynamicPatternCount = 0;
                status.CanResume = false;
                status.ResumeBlockedReason = null;
                status.CheckpointUpdatedAt = null;
            }
            else
            {
                ApplyCheckpointToStatus(status, checkpoint, canResume: false, blockedReason: null);
                status.State = checkpoint.State == PreTranslationState.AwaitingTermReview
                    ? PreTranslationState.AwaitingTermReview
                    : PreTranslationState.Running;
                status.Error = null;
                status.CanResume = false;
                status.ResumeBlockedReason = null;
            }

            status.FromLang = fromLang;
            status.ToLang = toLang;

            var cts = new CancellationTokenSource();
            if (_cancellations.TryRemove(gameId, out var oldCts))
                oldCts.Dispose();
            _cancellations[gameId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecutePreTranslationAsync(gameId, texts, fromLang, toLang, status, checkpoint, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    status.State = PreTranslationState.Cancelled;
                    status.Error = "预翻译已取消";
                    status.CanResume = false;
                    status.ResumeBlockedReason = null;
                    await UpdateCheckpointStatusAsync(gameId, status);
                    await BroadcastStatus(gameId, status);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "预翻译失败: 游戏 {GameId}", gameId);
                    status.State = PreTranslationState.Failed;
                    status.Error = "预翻译过程中发生内部错误，请查看日志获取详情";
                    status.CanResume = false;
                    status.ResumeBlockedReason = null;
                    await UpdateCheckpointStatusAsync(gameId, status);
                    await BroadcastStatus(gameId, status);
                }
                finally
                {
                    _termReviewCompletions.TryRemove(gameId, out _);
                    if (_cancellations.TryRemove(gameId, out var doneCts))
                        doneCts.Dispose();
                }
            });

            return status;
        }
        finally
        {
            gameLock.Release();
        }
    }

    private async Task ExecutePreTranslationAsync(
        string gameId,
        List<ExtractedText> texts,
        string fromLang,
        string toLang,
        PreTranslationStatus status,
        PreTranslationCheckpoint? checkpoint,
        CancellationToken ct)
    {
        var game = await gameLibrary.GetByIdAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} not found.");

        var prepared = await PrepareTextListAsync(gameId, texts, checkpoint?.TextSignature, ct);
        var textList = prepared.TextList;
        var textSignature = prepared.TextSignature;

        logger.LogInformation("脚本标签清洗: {Original} 条提取文本 -> {Cleaned} 条翻译文本, 游戏 {GameId}",
            texts.Count, textList.Count, gameId);

        status.TotalTexts = textList.Count;
        status.FromLang = fromLang;
        status.ToLang = toLang;

        var round1Seed = checkpoint?.Round1Translations
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var round2Seed = checkpoint?.Round2Translations
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var round1Translations = new ConcurrentDictionary<string, string>(round1Seed, StringComparer.Ordinal);
        var round2Translations = new ConcurrentDictionary<string, string>(round2Seed, StringComparer.Ordinal);
        var round1PersistableTranslations = new ConcurrentDictionary<string, string>(
            FilterPersistableTranslations(round1Seed), StringComparer.Ordinal);
        var finalPersistableTranslations = new ConcurrentDictionary<string, string>(
            round1PersistableTranslations, StringComparer.Ordinal);
        foreach (var key in round2Seed.Keys)
            finalPersistableTranslations.TryRemove(key, out _);
        foreach (var (key, value) in FilterPersistableTranslations(round2Seed))
            finalPersistableTranslations[key] = value;
        var translations = new ConcurrentDictionary<string, string>(round1Translations, StringComparer.Ordinal);
        foreach (var (key, value) in round2Translations)
            translations[key] = value;

        var settings = await settingsService.GetAsync(ct);
        var aiSettings = settings.AiTranslation;
        var isLocalMode = string.Equals(aiSettings.ActiveMode, "local", StringComparison.OrdinalIgnoreCase);
        var maxConc = isLocalMode ? 1 : Math.Clamp(aiSettings.MaxConcurrency, 1, 100);
        var batchSize = isLocalMode ? 5 : 10;

        var templateVars = dynamicPatternService.DetectTemplateVariables(textList);
        logger.LogInformation("模板变量检测: 发现 {Count} 条含模板变量的文本, 游戏 {GameId}",
            templateVars.Count, gameId);

        var resumePhase = checkpoint?.CurrentPhase;
        if (checkpoint is not null
            && string.Equals(resumePhase, "termReview", StringComparison.Ordinal)
            && checkpoint.ExtractedTermCount > 0)
        {
            var candidateStore = await termExtractionService.GetCandidatesAsync(gameId, ct);
            if (candidateStore.Candidates.Count == 0)
            {
                logger.LogWarning("术语审核检查点缺少候选术语，回退到术语提取阶段, 游戏 {GameId}", gameId);
                resumePhase = "termExtraction";
            }
        }

        var resumePhaseIndex = GetPhaseIndex(resumePhase);
        var dynamicPatternCount = checkpoint?.DynamicPatternCount ?? 0;
        var extractedTermCount = checkpoint?.ExtractedTermCount ?? 0;

        if (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("round1"))
        {
            status.State = PreTranslationState.Running;
            status.CurrentRound = 1;
            status.CurrentPhase = "round1";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            status.TranslatedTexts = round1Translations.Count;
            status.FailedTexts = 0;
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);
            await BroadcastEvent(gameId, "roundProgress",
                new { gameId, round = 1, phase = "translating" });

            var pendingRound1Texts = textList.Where(text => !round1Translations.ContainsKey(text)).ToList();
            var round1Counters = new ProgressCounters
            {
                Translated = round1Translations.Count,
                Failed = 0
            };
            var round1Batches = pendingRound1Texts.Chunk(batchSize).ToList();

            await Parallel.ForEachAsync(round1Batches,
                new ParallelOptions { MaxDegreeOfParallelism = maxConc, CancellationToken = ct },
                async (batch, token) =>
                {
                    try
                    {
                        var batchList = batch.ToList();
                        var batchResult = await TranslatePreTranslationBatchResilientAsync(
                            gameId, batchList, fromLang, toLang, token);

                        foreach (var (text, translation, persistable) in batchResult.Successes)
                        {
                            round1Translations[text] = translation;
                            translations[text] = translation;
                            if (persistable)
                            {
                                round1PersistableTranslations[text] = translation;
                                finalPersistableTranslations[text] = translation;
                            }
                            else
                            {
                                round1PersistableTranslations.TryRemove(text, out _);
                                finalPersistableTranslations.TryRemove(text, out _);
                            }
                        }

                        Interlocked.Add(ref round1Counters.Translated, batchResult.Successes.Count);
                        Interlocked.Add(ref round1Counters.Failed, batchResult.FailedCount);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Add(ref round1Counters.Failed, batch.Length);
                        logger.LogWarning(ex, "预翻译批次失败({Count} 条文本), 游戏 {GameId}", batch.Length, gameId);
                    }

                    await PersistRoundProgressAsync(gameId, status, fromLang, toLang, textSignature,
                        round1Translations, round2Translations, round1Counters);
                    await ThrottledBroadcastStatus(gameId, status, round1Counters);
                });

            status.TranslatedTexts = Volatile.Read(ref round1Counters.Translated);
            status.FailedTexts = Volatile.Read(ref round1Counters.Failed);
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);

            logger.LogInformation("Round 1 完成: {Translated}/{Total} 翻译成功, {Failed} 失败, 游戏 {GameId}",
                status.TranslatedTexts, textList.Count, status.FailedTexts, gameId);
        }
        else
        {
            status.TranslatedTexts = checkpoint?.TranslatedTexts ?? round1Translations.Count;
            status.FailedTexts = checkpoint?.FailedTexts ?? Math.Max(0, textList.Count - round1Translations.Count);
            status.DynamicPatternCount = dynamicPatternCount;
            status.ExtractedTermCount = extractedTermCount;
        }

        if (aiSettings.EnableLlmPatternAnalysis
            && round1Translations.Count > 0
            && (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("patternAnalysis")))
        {
            ct.ThrowIfCancellationRequested();
            status.State = PreTranslationState.Running;
            status.CurrentRound = 1;
            status.CurrentPhase = "patternAnalysis";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            status.TranslatedTexts = round1Translations.Count;
            status.FailedTexts = Math.Max(0, textList.Count - round1Translations.Count);
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);

            try
            {
                var pairs = round1Translations
                    .Select(kv => (Original: kv.Key, Translation: kv.Value))
                    .ToList();
                logger.LogInformation("开始模式分析: {Count} 对翻译, 游戏 {GameId}", pairs.Count, gameId);
                dynamicPatternCount = await dynamicPatternService.AnalyzeDynamicFragmentsAsync(
                    gameId, pairs, ct,
                    onBatchProgress: (done, total) =>
                    {
                        status.PhaseProgress = done;
                        status.PhaseTotal = total;
                        _ = PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                            round1Translations, round2Translations);
                    });
                status.DynamicPatternCount = dynamicPatternCount;
                await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                    round1Translations, round2Translations);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM 动态模式分析失败, 游戏 {GameId}", gameId);
            }
        }

        if (aiSettings.EnableAutoTermExtraction
            && round1PersistableTranslations.Count > 0
            && (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("termExtraction")))
        {
            ct.ThrowIfCancellationRequested();
            status.State = PreTranslationState.Running;
            status.CurrentRound = 1;
            status.CurrentPhase = "termExtraction";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            status.TranslatedTexts = round1Translations.Count;
            status.FailedTexts = Math.Max(0, textList.Count - round1Translations.Count);
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);

            try
            {
                var pairs = round1PersistableTranslations
                    .Select(kv => (original: kv.Key, translation: kv.Value))
                    .ToList();
                logger.LogInformation("开始术语提取: {Count} 对翻译, 游戏 {GameId}", pairs.Count, gameId);
                var candidates = await termExtractionService.ExtractFromPairsAsync(
                    gameId, pairs, ct,
                    onBatchProgress: (done, total) =>
                    {
                        status.PhaseProgress = done;
                        status.PhaseTotal = total;
                        _ = PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                            round1Translations, round2Translations);
                    });
                extractedTermCount = candidates.Count;
                status.ExtractedTermCount = extractedTermCount;
                await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                    round1Translations, round2Translations);

                if (extractedTermCount > 0)
                {
                    await BroadcastEvent(gameId, "termExtractionComplete",
                        new { gameId, candidateCount = extractedTermCount });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "术语提取失败, 游戏 {GameId}", gameId);
            }
        }

        if (round1Translations.Count > 0 && extractedTermCount > 0)
        {
            if (!aiSettings.AutoApplyExtractedTerms
                && (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("termReview")))
            {
                await WaitForTermReviewAsync(gameId, status, fromLang, toLang, textSignature,
                    round1Translations, round2Translations, textList.Count, extractedTermCount, ct);
            }

            try
            {
                var applied = await termExtractionService.ApplyCandidatesAsync(gameId, null, ct);
                logger.LogInformation("已应用 {Count} 条提取术语, 游戏 {GameId}", applied, gameId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "应用提取术语失败, 游戏 {GameId}", gameId);
            }
        }

        if (aiSettings.EnableMultiRoundTranslation
            && round1Translations.Count > 0
            && extractedTermCount > 0
            && (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("round2")))
        {
            ct.ThrowIfCancellationRequested();
            status.State = PreTranslationState.Running;
            status.CurrentRound = 2;
            status.CurrentPhase = "round2";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            status.TranslatedTexts = round2Translations.Count;
            status.FailedTexts = 0;
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);
            await BroadcastEvent(gameId, "roundProgress",
                new { gameId, round = 2, phase = "translating" });

            logger.LogInformation("开始 Round 2(带术语重新翻译): 游戏 {GameId}", gameId);

            var pendingRound2Texts = textList.Where(text => !round2Translations.ContainsKey(text)).ToList();
            var round2Counters = new ProgressCounters
            {
                Translated = round2Translations.Count,
                Failed = 0
            };
            var round2Batches = pendingRound2Texts.Chunk(batchSize).ToList();

            await Parallel.ForEachAsync(round2Batches,
                new ParallelOptions { MaxDegreeOfParallelism = maxConc, CancellationToken = ct },
                async (batch, token) =>
                {
                    try
                    {
                        var batchList = batch.ToList();
                        var batchResult = await TranslatePreTranslationBatchResilientAsync(
                            gameId, batchList, fromLang, toLang, token);

                        foreach (var (text, translation, persistable) in batchResult.Successes)
                        {
                            round2Translations[text] = translation;
                            translations[text] = translation;
                            if (persistable)
                                finalPersistableTranslations[text] = translation;
                            else
                                finalPersistableTranslations.TryRemove(text, out _);
                        }

                        Interlocked.Add(ref round2Counters.Translated, batchResult.Successes.Count);
                        Interlocked.Add(ref round2Counters.Failed, batchResult.FailedCount);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Add(ref round2Counters.Failed, batch.Length);
                        logger.LogWarning(ex, "Round 2 预翻译批次失败({Count} 条文本), 游戏 {GameId}",
                            batch.Length, gameId);
                    }

                    await PersistRoundProgressAsync(gameId, status, fromLang, toLang, textSignature,
                        round1Translations, round2Translations, round2Counters);
                    await ThrottledBroadcastStatus(gameId, status, round2Counters);
                });

            foreach (var (key, value) in round2Translations)
                translations[key] = value;

            status.TranslatedTexts = Volatile.Read(ref round2Counters.Translated);
            status.FailedTexts = Volatile.Read(ref round2Counters.Failed);
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);

            logger.LogInformation("Round 2 完成: {Translated}/{Total} 翻译成功, {Failed} 失败, 游戏 {GameId}",
                status.TranslatedTexts, textList.Count, status.FailedTexts, gameId);
        }
        else
        {
            foreach (var (key, value) in round2Translations)
                translations[key] = value;
        }

        if (translations.Count > 0
            && (checkpoint is null || resumePhaseIndex <= GetPhaseIndex("writeCache")))
        {
            status.State = PreTranslationState.Running;
            status.CurrentPhase = "writeCache";
            status.PhaseProgress = 0;
            status.PhaseTotal = 0;
            status.CurrentRound = status.CurrentRound > 0 ? status.CurrentRound : 1;
            await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);

            var dynamicRegexEntries = GenerateDynamicRegexEntries(
                textList, translations, templateVars);
            await WriteTranslationCacheAsync(game.GamePath, toLang, gameId, translations, dynamicRegexEntries, ct);

            logger.LogInformation("预翻译完成: {Count}/{Total} 条翻译已写入缓存, 游戏 {GameId}",
                translations.Count, textList.Count, gameId);
        }

        if (finalPersistableTranslations.Count > 0)
        {
            try
            {
                var originals = finalPersistableTranslations.Keys.ToList();
                var translationValues = originals.Select(key => finalPersistableTranslations[key]).ToList();
                var finalRound = status.CurrentRound > 0 ? status.CurrentRound : 1;
                translationMemoryService.Add(gameId, originals, translationValues, finalRound, isFinal: true);
                await translationMemoryService.FlushAsync(gameId, ct);
                logger.LogInformation("翻译记忆库写入 {Count} 条, 游戏 {GameId}",
                    finalPersistableTranslations.Count, gameId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "写入翻译记忆库失败, 游戏 {GameId}", gameId);
            }
        }

        status.State = PreTranslationState.Completed;
        status.Error = null;
        status.CanResume = false;
        status.ResumeBlockedReason = null;
        status.CheckpointUpdatedAt = null;
        await DeleteCheckpointAsync(gameId, clearInactiveStatus: false, CancellationToken.None);
        await BroadcastStatus(gameId, status);

        trayService.ShowNotification("预翻译完成",
            $"《{game.Name}》已翻译 {translations.Count}/{textList.Count} 条文本");
    }

    private static Dictionary<string, string> FilterPersistableTranslations(
        IEnumerable<KeyValuePair<string, string>> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (original, translation) in source)
        {
            if (!RuntimePlaceholderProtector.HasExactRoundTrip(original, translation))
                continue;

            result[original] = translation;
        }

        return result;
    }

    private async Task WaitForTermReviewAsync(
        string gameId,
        PreTranslationStatus status,
        string fromLang,
        string toLang,
        string textSignature,
        ConcurrentDictionary<string, string> round1Translations,
        ConcurrentDictionary<string, string> round2Translations,
        int totalTexts,
        int extractedTermCount,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _termReviewCompletions[gameId] = tcs;

        status.State = PreTranslationState.AwaitingTermReview;
        status.CurrentRound = 1;
        status.CurrentPhase = "termReview";
        status.PhaseProgress = 0;
        status.PhaseTotal = 0;
        status.TranslatedTexts = round1Translations.Count;
        status.FailedTexts = Math.Max(0, totalTexts - round1Translations.Count);
        status.ExtractedTermCount = extractedTermCount;
        await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
            round1Translations, round2Translations);

        logger.LogInformation("等待术语审核: {Count} 条候选术语, 游戏 {GameId}",
            extractedTermCount, gameId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            await tcs.Task.WaitAsync(timeoutCts.Token);
            logger.LogInformation("术语审核完成，继续预翻译, 游戏 {GameId}", gameId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogInformation("术语审核超时，自动继续预翻译, 游戏 {GameId}", gameId);
        }
        finally
        {
            _termReviewCompletions.TryRemove(gameId, out _);
        }

        ct.ThrowIfCancellationRequested();
        status.State = PreTranslationState.Running;
        status.Error = null;
        await PersistAndBroadcastStatusAsync(gameId, status, fromLang, toLang, textSignature,
            round1Translations, round2Translations);
    }

    private async Task PersistRoundProgressAsync(
        string gameId,
        PreTranslationStatus status,
        string fromLang,
        string toLang,
        string textSignature,
        ConcurrentDictionary<string, string> round1Translations,
        ConcurrentDictionary<string, string> round2Translations,
        ProgressCounters counters)
    {
        status.TranslatedTexts = Volatile.Read(ref counters.Translated);
        status.FailedTexts = Volatile.Read(ref counters.Failed);
        await ThrottledPersistCheckpointAsync(gameId, status, fromLang, toLang, textSignature,
            round1Translations, round2Translations);
    }

    private async Task PersistAndBroadcastStatusAsync(
        string gameId,
        PreTranslationStatus status,
        string fromLang,
        string toLang,
        string textSignature,
        ConcurrentDictionary<string, string> round1Translations,
        ConcurrentDictionary<string, string> round2Translations)
    {
        await PersistCheckpointAsync(gameId, status, fromLang, toLang, textSignature,
            round1Translations, round2Translations);
        MarkCheckpointPersisted(gameId);
        await BroadcastStatus(gameId, status);
    }

    private async Task ThrottledPersistCheckpointAsync(
        string gameId,
        PreTranslationStatus status,
        string fromLang,
        string toLang,
        string textSignature,
        ConcurrentDictionary<string, string> round1Translations,
        ConcurrentDictionary<string, string> round2Translations)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = _lastCheckpointPersistTicks.GetOrAdd(gameId, 0L);
        if (now - last < CheckpointPersistIntervalTicks)
            return;
        if (!_lastCheckpointPersistTicks.TryUpdate(gameId, now, last))
            return;

        try
        {
            await PersistCheckpointAsync(gameId, status, fromLang, toLang, textSignature,
                round1Translations, round2Translations);
            MarkCheckpointPersisted(gameId);
        }
        catch
        {
            _lastCheckpointPersistTicks.TryUpdate(gameId, last, now);
            throw;
        }
    }

    private async Task PersistCheckpointAsync(
        string gameId,
        PreTranslationStatus status,
        string fromLang,
        string toLang,
        string textSignature,
        ConcurrentDictionary<string, string> round1Translations,
        ConcurrentDictionary<string, string> round2Translations)
    {
        var checkpoint = new PreTranslationCheckpoint
        {
            GameId = gameId,
            FromLang = fromLang,
            ToLang = toLang,
            TextSignature = textSignature,
            State = status.State,
            TotalTexts = status.TotalTexts,
            TranslatedTexts = status.TranslatedTexts,
            FailedTexts = status.FailedTexts,
            Error = status.Error,
            CurrentRound = status.CurrentRound,
            CurrentPhase = status.CurrentPhase,
            PhaseProgress = status.PhaseProgress,
            PhaseTotal = status.PhaseTotal,
            ExtractedTermCount = status.ExtractedTermCount,
            DynamicPatternCount = status.DynamicPatternCount,
            Round1Translations = SnapshotTranslations(round1Translations),
            Round2Translations = SnapshotTranslations(round2Translations),
            UpdatedAt = DateTime.UtcNow
        };

        await SaveCheckpointAsync(gameId, checkpoint, CancellationToken.None);

        status.FromLang = fromLang;
        status.ToLang = toLang;
        status.CanResume = false;
        status.ResumeBlockedReason = null;
        status.CheckpointUpdatedAt = checkpoint.UpdatedAt;
    }

    private async Task UpdateCheckpointStatusAsync(string gameId, PreTranslationStatus status)
    {
        var checkpoint = await LoadCheckpointAsync(gameId, CancellationToken.None);
        if (checkpoint is null)
            return;

        checkpoint.State = status.State;
        checkpoint.TotalTexts = status.TotalTexts;
        checkpoint.TranslatedTexts = status.TranslatedTexts;
        checkpoint.FailedTexts = status.FailedTexts;
        checkpoint.Error = status.Error;
        checkpoint.CurrentRound = status.CurrentRound;
        checkpoint.CurrentPhase = status.CurrentPhase;
        checkpoint.PhaseProgress = status.PhaseProgress;
        checkpoint.PhaseTotal = status.PhaseTotal;
        checkpoint.ExtractedTermCount = status.ExtractedTermCount;
        checkpoint.DynamicPatternCount = status.DynamicPatternCount;
        checkpoint.UpdatedAt = DateTime.UtcNow;

        await SaveCheckpointAsync(gameId, checkpoint, CancellationToken.None);
        MarkCheckpointPersisted(gameId);
        status.CheckpointUpdatedAt = checkpoint.UpdatedAt;
    }

    private async Task SaveCheckpointAsync(string gameId, PreTranslationCheckpoint checkpoint, CancellationToken ct)
    {
        var checkpointLock = _checkpointLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await checkpointLock.WaitAsync(ct);
        try
        {
            await FileHelper.WriteJsonAtomicAsync(appDataPaths.PreTranslationSessionFile(gameId),
                checkpoint, ct: ct);
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task<PreTranslationCheckpoint?> LoadCheckpointAsync(string gameId, CancellationToken ct)
    {
        var file = appDataPaths.PreTranslationSessionFile(gameId);
        if (!File.Exists(file))
            return null;

        var checkpointLock = _checkpointLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await checkpointLock.WaitAsync(ct);
        try
        {
            if (!File.Exists(file))
                return null;

            var json = await File.ReadAllTextAsync(file, ct);
            return JsonSerializer.Deserialize<PreTranslationCheckpoint>(json, FileHelper.DataJsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载预翻译检查点失败, 游戏 {GameId}", gameId);
            return null;
        }
        finally
        {
            checkpointLock.Release();
        }
    }

    private async Task<ResumeValidationResult> ValidateCheckpointAsync(
        string gameId,
        PreTranslationCheckpoint checkpoint,
        List<ExtractedText>? texts,
        CancellationToken ct)
    {
        texts ??= (await LoadExtractedTextsAsync(gameId, ct))?.Texts;
        if (texts is null)
        {
            return new ResumeValidationResult(
                TextList: null,
                TextSignature: null,
                Error: "提取缓存不存在，请先重新提取资产后再重新开始预翻译。");
        }

        var prepared = await PrepareTextListAsync(gameId, texts, expectedSignature: null, ct);
        if (!string.Equals(prepared.TextSignature, checkpoint.TextSignature, StringComparison.Ordinal))
        {
            return new ResumeValidationResult(
                TextList: prepared.TextList,
                TextSignature: prepared.TextSignature,
                Error: "提取文本或脚本清洗规则已变化，请重新开始预翻译。");
        }

        return new ResumeValidationResult(prepared.TextList, prepared.TextSignature, Error: null);
    }

    private async Task<AssetExtractionResult?> LoadExtractedTextsAsync(string gameId, CancellationToken ct)
    {
        var file = appDataPaths.ExtractedTextsFile(gameId);
        if (!File.Exists(file))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(file, ct);
            return JsonSerializer.Deserialize<AssetExtractionResult>(json, FileHelper.DataJsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载提取文本缓存失败, 游戏 {GameId}", gameId);
            return null;
        }
    }

    private async Task<(List<string> TextList, string TextSignature)> PrepareTextListAsync(
        string gameId,
        List<ExtractedText> texts,
        string? expectedSignature,
        CancellationToken ct)
    {
        var textList = await scriptTagService.FilterAndCleanAsync(gameId, texts, ct);
        var textSignature = ComputeTextSignature(textList);
        if (!string.IsNullOrEmpty(expectedSignature)
            && !string.Equals(textSignature, expectedSignature, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("提取文本或脚本清洗规则已变化，请重新开始预翻译。");
        }

        return (textList, textSignature);
    }

    private static string ComputeTextSignature(IEnumerable<string> texts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var text in texts)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(text));
            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int GetPhaseIndex(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
            return 0;

        for (var i = 0; i < PhaseOrder.Length; i++)
        {
            if (string.Equals(PhaseOrder[i], phase, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private static void ApplyCheckpointToStatus(
        PreTranslationStatus status,
        PreTranslationCheckpoint checkpoint,
        bool canResume,
        string? blockedReason)
    {
        status.State = checkpoint.State;
        status.TotalTexts = checkpoint.TotalTexts;
        status.TranslatedTexts = checkpoint.TranslatedTexts;
        status.FailedTexts = checkpoint.FailedTexts;
        status.Error = checkpoint.Error;
        status.CurrentRound = checkpoint.CurrentRound;
        status.CurrentPhase = checkpoint.CurrentPhase;
        status.PhaseProgress = checkpoint.PhaseProgress;
        status.PhaseTotal = checkpoint.PhaseTotal;
        status.ExtractedTermCount = checkpoint.ExtractedTermCount;
        status.DynamicPatternCount = checkpoint.DynamicPatternCount;
        status.CanResume = canResume;
        status.FromLang = checkpoint.FromLang;
        status.ToLang = checkpoint.ToLang;
        status.CheckpointUpdatedAt = checkpoint.UpdatedAt;
        status.ResumeBlockedReason = blockedReason;
    }

    internal static Dictionary<string, string> SnapshotTranslations(
        ConcurrentDictionary<string, string> source)
    {
        var snapshot = source.ToArray();
        Array.Sort(snapshot, static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

        var ordered = new Dictionary<string, string>(snapshot.Length, StringComparer.Ordinal);
        foreach (var (key, value) in snapshot)
            ordered[key] = value;

        return ordered;
    }

    private async Task<ResilientBatchResult> TranslatePreTranslationBatchResilientAsync(
        string gameId,
        IList<string> batch,
        string fromLang,
        string toLang,
        CancellationToken ct)
    {
        try
        {
            var result = await translationService.TranslateDetailedAsync(batch, fromLang, toLang, gameId, ct);
            var successes = new List<(string Text, string Translation, bool Persistable)>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
                successes.Add((batch[i], result.Translations[i], result.Persistable[i]));
            return new ResilientBatchResult(successes, FailedCount: 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (batch.Count > 1 && IsRecoverableBatchError(ex))
        {
            logger.LogWarning(ex, "预翻译批次失败({Count} 条文本)，拆分重试, 游戏 {GameId}", batch.Count, gameId);

            var midpoint = batch.Count / 2;
            var left = await TranslatePreTranslationBatchResilientAsync(
                gameId, batch.Take(midpoint).ToArray(), fromLang, toLang, ct);
            var right = await TranslatePreTranslationBatchResilientAsync(
                gameId, batch.Skip(midpoint).ToArray(), fromLang, toLang, ct);

            return new ResilientBatchResult(
                left.Successes.Concat(right.Successes).ToArray(),
                left.FailedCount + right.FailedCount);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预翻译批次失败({Count} 条文本), 游戏 {GameId}", batch.Count, gameId);
            return new ResilientBatchResult([], batch.Count);
        }
    }

    private static bool IsRecoverableBatchError(Exception ex) =>
        ex is TaskCanceledException or HttpRequestException or TimeoutException;

    private void MarkCheckpointPersisted(string gameId)
    {
        var now = DateTime.UtcNow.Ticks;
        _lastCheckpointPersistTicks[gameId] = now;
    }

    private List<(string Pattern, string Replacement)> GenerateDynamicRegexEntries(
        List<string> textList,
        ConcurrentDictionary<string, string> translations,
        List<(int Index, string Text, List<(int Start, int End, string Variable)> Variables)> templateVars)
    {
        var entries = new List<(string Original, string Translation, List<(int Start, int End, string Variable)> Variables)>();
        foreach (var (index, text, variables) in templateVars)
        {
            if (index >= 0 && index < textList.Count && translations.TryGetValue(text, out var translation))
                entries.Add((text, translation, variables));
        }

        if (entries.Count == 0)
            return [];

        return dynamicPatternService.GenerateRegexEntries(entries);
    }

    private async Task WriteTranslationCacheAsync(
        string gamePath,
        string toLang,
        string gameId,
        ConcurrentDictionary<string, string> translations,
        List<(string Pattern, string Replacement)> dynamicRegexEntries,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var enableCacheOptimization = settings.AiTranslation.EnablePreTranslationCache;

        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        Directory.CreateDirectory(translationDir);

        var cacheFilePath = Path.Combine(translationDir, "_PreTranslated.txt");
        var existing = new HashSet<string>();
        if (File.Exists(cacheFilePath))
        {
            foreach (var line in await File.ReadAllLinesAsync(cacheFilePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                var eqIdx = FindUnescapedEquals(line);
                if (eqIdx > 0)
                    existing.Add(line[..eqIdx]);
            }
        }

        var sb = new StringBuilder();
        if (!File.Exists(cacheFilePath))
            sb.AppendLine("// Pre-translated by XUnity Toolkit");

        foreach (var (original, translation) in translations)
        {
            var originalKey = enableCacheOptimization
                ? scriptTagService.NormalizeForCache(gameId, original)
                : original;
            if (string.IsNullOrEmpty(originalKey))
                continue;

            var encodedOriginal = EncodeForXUnity(originalKey);
            if (existing.Contains(encodedOriginal))
                continue;

            var encodedTranslation = EncodeForXUnity(translation);
            sb.AppendLine($"{encodedOriginal}={encodedTranslation}");
        }

        if (sb.Length > 0)
            await File.AppendAllTextAsync(cacheFilePath, sb.ToString(), ct);

        await WriteRegexPatternsAsync(gamePath, toLang, gameId,
            includeBasePatterns: enableCacheOptimization,
            dynamicRegexEntries,
            ct);
    }

    private async Task WriteRegexPatternsAsync(
        string gamePath,
        string toLang,
        string gameId,
        bool includeBasePatterns,
        List<(string Pattern, string Replacement)> dynamicRegexEntries,
        CancellationToken ct)
    {
        var dir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, "_PreTranslated_Regex.txt");
        var hasDynamicPatterns = dynamicRegexEntries.Count > 0;
        if (!includeBasePatterns && !hasDynamicPatterns)
            return;

        var sb = new StringBuilder();
        sb.AppendLine("// Pre-translation regex patterns generated by XUnity Toolkit");

        if (includeBasePatterns)
        {
            sb.AppendLine();
            sb.AppendLine("// Two-line concatenation (both groups are text)");
            sb.AppendLine(@"sr:""^([\S\s]+?)\n([\S\s]+)$""=$1\n$2");
            sb.AppendLine();
            sb.AppendLine("// Three-line concatenation");
            sb.AppendLine(@"sr:""^([\S\s]+?)\n([\S\s]+?)\n([\S\s]+)$""=$1\n$2\n$3");

            var customFile = appDataPaths.PreTranslationRegexFile(gameId);
            if (File.Exists(customFile))
            {
                sb.AppendLine();
                sb.AppendLine("// Custom patterns");
                var customContent = await File.ReadAllTextAsync(customFile, ct);
                sb.Append(customContent);
                if (!customContent.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                    sb.AppendLine();
            }
        }

        if (hasDynamicPatterns)
        {
            sb.AppendLine();
            sb.AppendLine("// Dynamic template variable patterns");
            foreach (var (pattern, replacement) in dynamicRegexEntries)
                sb.AppendLine($"r:\"{pattern}\"={replacement}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), ct);
        logger.LogInformation("动态正则写入 {Count} 条模式, 游戏 {GameId}",
            dynamicRegexEntries.Count, gameId);
    }

    private static string EncodeForXUnity(string text) => XUnityTranslationFormat.Encode(text);
    private static int FindUnescapedEquals(string line) => XUnityTranslationFormat.FindUnescapedEquals(line);

    private async Task ThrottledBroadcastStatus(
        string gameId,
        PreTranslationStatus status,
        ProgressCounters counters)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = _lastBroadcastTicks.GetOrAdd(gameId, 0L);
        if (now - last < TimeSpan.FromMilliseconds(200).Ticks)
            return;
        if (!_lastBroadcastTicks.TryUpdate(gameId, now, last))
            return;

        status.TranslatedTexts = Volatile.Read(ref counters.Translated);
        status.FailedTexts = Volatile.Read(ref counters.Failed);
        await BroadcastStatus(gameId, status);
    }

    private async Task BroadcastStatus(string gameId, PreTranslationStatus status)
    {
        try
        {
            await hubContext.Clients.Group($"pre-translation-{gameId}")
                .SendAsync("preTranslationUpdate", status);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预翻译状态推送失败, 游戏 {GameId}", gameId);
        }
    }

    private async Task BroadcastEvent(string gameId, string eventName, object data)
    {
        try
        {
            await hubContext.Clients.Group($"pre-translation-{gameId}")
                .SendAsync(eventName, data);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "预翻译事件推送失败 {Event}, 游戏 {GameId}", eventName, gameId);
        }
    }
}
