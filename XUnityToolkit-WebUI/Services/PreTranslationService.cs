using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PreTranslationService(
    LlmTranslationService translationService,
    GameLibraryService gameLibrary,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<PreTranslationService> logger)
{
    private readonly ConcurrentDictionary<string, PreTranslationStatus> _statuses = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = [];
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = [];

    private const int BatchSize = 10;

    public PreTranslationStatus GetStatus(string gameId)
    {
        return _statuses.GetOrAdd(gameId, id => new PreTranslationStatus { GameId = id });
    }

    public async Task<PreTranslationStatus> StartPreTranslationAsync(
        string gameId, List<ExtractedText> texts, string fromLang, string toLang)
    {
        var gameLock = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gameLock.WaitAsync();
        try
        {
            var status = GetStatus(gameId);

            if (status.State == PreTranslationState.Running)
                throw new InvalidOperationException("预翻译任务已在运行中");

            status.State = PreTranslationState.Running;
            status.TotalTexts = texts.Count;
            status.TranslatedTexts = 0;
            status.FailedTexts = 0;
            status.Error = null;

            var cts = new CancellationTokenSource();
            _cancellations[gameId] = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecutePreTranslationAsync(gameId, texts, fromLang, toLang, status, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    status.State = PreTranslationState.Cancelled;
                    status.Error = "预翻译已取消";
                    await BroadcastStatus(gameId, status);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "预翻译失败: 游戏 {GameId}", gameId);
                    status.State = PreTranslationState.Failed;
                    status.Error = ex.Message;
                    await BroadcastStatus(gameId, status);
                }
                finally
                {
                    _cancellations.TryRemove(gameId, out _);
                }
            });

            return status;
        }
        finally
        {
            gameLock.Release();
        }
    }

    public void Cancel(string gameId)
    {
        if (_cancellations.TryGetValue(gameId, out var cts))
            cts.Cancel();
    }

    private async Task ExecutePreTranslationAsync(
        string gameId, List<ExtractedText> texts, string fromLang, string toLang,
        PreTranslationStatus status, CancellationToken ct)
    {
        var game = await gameLibrary.GetByIdAsync(gameId, ct)
            ?? throw new KeyNotFoundException($"Game {gameId} not found.");

        await BroadcastStatus(gameId, status);

        var translations = new Dictionary<string, string>();
        var textList = texts.Select(t => t.Text).ToList();

        // Process in batches
        for (int i = 0; i < textList.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = textList.Skip(i).Take(BatchSize).ToList();

            try
            {
                var results = await translationService.TranslateAsync(
                    batch, fromLang, toLang, gameId, ct);

                for (int j = 0; j < batch.Count; j++)
                {
                    translations[batch[j]] = results[j];
                    status.TranslatedTexts++;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "批次翻译失败 ({Batch}/{Total})", i / BatchSize + 1,
                    (textList.Count + BatchSize - 1) / BatchSize);

                // Count failed texts but continue
                status.FailedTexts += batch.Count;
            }

            await BroadcastStatus(gameId, status);
        }

        // Write translation cache file
        if (translations.Count > 0)
        {
            await WriteTranslationCacheAsync(game.GamePath, toLang, translations, ct);
            logger.LogInformation("预翻译完成: {Count}/{Total} 条翻译已写入缓存, 游戏 {GameId}",
                translations.Count, textList.Count, gameId);
        }

        status.State = PreTranslationState.Completed;
        await BroadcastStatus(gameId, status);
    }

    /// <summary>
    /// Write translations to XUnity.AutoTranslator cache file format.
    /// Format: encoded_original=encoded_translation (one per line)
    /// </summary>
    private async Task WriteTranslationCacheAsync(
        string gamePath, string toLang, Dictionary<string, string> translations,
        CancellationToken ct)
    {
        var translationDir = Path.Combine(gamePath, "BepInEx", "Translation", toLang, "Text");
        Directory.CreateDirectory(translationDir);

        var filePath = Path.Combine(translationDir, "_PreTranslated.txt");

        // Load existing translations to avoid duplicates
        var existing = new HashSet<string>();
        if (File.Exists(filePath))
        {
            foreach (var line in await File.ReadAllLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var eqIdx = FindUnescapedEquals(line);
                if (eqIdx > 0)
                    existing.Add(line[..eqIdx]);
            }
        }

        var sb = new StringBuilder();
        if (!File.Exists(filePath))
            sb.AppendLine("// Pre-translated by XUnity Toolkit");

        foreach (var (original, translation) in translations)
        {
            var encodedOriginal = EncodeForXUnity(original);
            if (existing.Contains(encodedOriginal)) continue;

            var encodedTranslation = EncodeForXUnity(translation);
            sb.AppendLine($"{encodedOriginal}={encodedTranslation}");
        }

        await File.AppendAllTextAsync(filePath, sb.ToString(), ct);
    }

    /// <summary>
    /// Encode a string for XUnity translation cache file.
    /// </summary>
    private static string EncodeForXUnity(string text)
    {
        var sb = new StringBuilder(text.Length + 10);
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '=': sb.Append("\\="); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Find the first unescaped '=' in a line.
    /// </summary>
    private static int FindUnescapedEquals(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; } // Skip escaped char
            if (line[i] == '=') return i;
        }
        return -1;
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
            logger.LogWarning(ex, "预翻译状态推送失败: 游戏 {GameId}", gameId);
        }
    }
}
