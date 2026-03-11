using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class LlmTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    GlossaryService glossaryService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<LlmTranslationService> logger)
{
    // ── Core stats (thread-safe) ──
    private long _totalTranslated;
    private long _totalReceived; // total incoming translation requests
    private long _totalErrors;   // total failed translations
    private long _translating;   // actively calling LLM API (inside semaphore)
    private long _queued;        // waiting for semaphore slot
    private long _lastRequestTicks; // 0 = never
    private long _totalTokensUsed;
    private long _totalResponseTimeMs;
    private long _totalRequests;
    private bool _enabled = true;

    // ── Per-endpoint runtime stats ──
    private readonly ConcurrentDictionary<string, EndpointStats> _endpointStats = new();

    // ── Recent translations circular buffer ──
    private readonly ConcurrentQueue<RecentTranslation> _recentTranslations = new();
    private const int MaxRecentTranslations = 10;

    // ── Recent errors circular buffer ──
    private readonly ConcurrentQueue<TranslationError> _recentErrors = new();
    private const int MaxRecentErrors = 50;

    // ── Requests-per-minute tracking ──
    private readonly ConcurrentQueue<long> _requestTimestamps = new();

    // ── Concurrency control ──
    private SemaphoreSlim? _semaphore; // Read via Volatile.Read at capture site for ARM safety
    private int _currentMaxConcurrency;

    // ── BroadcastStats throttle ──
    private long _lastBroadcastTicks;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled);
        set => Volatile.Write(ref _enabled, value);
    }

    public TranslationStats GetStats()
    {
        // Clean old timestamps (older than 60s) for RPM calculation
        var cutoff = DateTime.UtcNow.AddMinutes(-1).Ticks;
        while (_requestTimestamps.TryPeek(out var ts) && ts < cutoff)
            _requestTimestamps.TryDequeue(out _);

        var totalReqs = Interlocked.Read(ref _totalRequests);
        var avgMs = totalReqs > 0
            ? (double)Interlocked.Read(ref _totalResponseTimeMs) / totalReqs
            : 0;

        return new TranslationStats(
            TotalTranslated: Interlocked.Read(ref _totalTranslated),
            Translating: (int)Interlocked.Read(ref _translating),
            Queued: (int)Interlocked.Read(ref _queued),
            LastRequestAt: Interlocked.Read(ref _lastRequestTicks) is var ticks and > 0
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null,
            TotalTokensUsed: Interlocked.Read(ref _totalTokensUsed),
            AverageResponseTimeMs: Math.Round(avgMs, 1),
            RequestsPerMinute: _requestTimestamps.Count,
            Enabled: _enabled,
            RecentTranslations: _recentTranslations.ToArray(),
            TotalReceived: Interlocked.Read(ref _totalReceived),
            TotalErrors: Interlocked.Read(ref _totalErrors),
            RecentErrors: _recentErrors.ToArray()
        );
    }

    /// <summary>
    /// Record a translation error for display in the web UI.
    /// </summary>
    public void RecordError(string message, string? endpointName = null)
    {
        Interlocked.Increment(ref _totalErrors);
        _recentErrors.Enqueue(new TranslationError(message, DateTime.UtcNow, endpointName));
        while (_recentErrors.Count > MaxRecentErrors)
            _recentErrors.TryDequeue(out _);
        _ = BroadcastStats(force: true);
    }

    public async Task<IList<string>> TranslateAsync(
        IList<string> texts, string from, string to,
        string? gameId = null, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        Interlocked.Add(ref _totalReceived, texts.Count);
        Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
        _ = BroadcastStats();

        if (!_enabled)
            throw new InvalidOperationException("AI 翻译功能已停用");

        try
        {
            var settings = await settingsService.GetAsync(ct);
            var ai = settings.AiTranslation;

            var enabledEndpoints = ai.Endpoints.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey)).ToList();
            if (enabledEndpoints.Count == 0)
                throw new InvalidOperationException("没有可用的 AI 提供商，请在 AI 翻译页面配置至少一个提供商");

            // Load glossary for the game (if available)
            List<GlossaryEntry>? glossary = null;
            if (!string.IsNullOrEmpty(gameId))
            {
                glossary = await glossaryService.GetAsync(gameId, ct);
                if (glossary.Count == 0) glossary = null;
            }

            // Ensure semaphore matches configured concurrency
            var maxConc = Math.Clamp(ai.MaxConcurrency, 1, 100);
            EnsureSemaphore(maxConc);
            var semaphore = Volatile.Read(ref _semaphore)!; // Acquire-read for ARM safety

            // Parallel per-text translation
            var tasks = new Task<(string translated, long tokens, double ms, string endpointName)>[texts.Count];
            for (int i = 0; i < texts.Count; i++)
            {
                var text = texts[i];
                tasks[i] = TranslateSingleAsync(text, from, to, ai, enabledEndpoints, glossary, semaphore, ct);
            }

            var results = await Task.WhenAll(tasks);

            // Aggregate results
            var translations = new string[texts.Count];
            for (int i = 0; i < results.Length; i++)
            {
                var (translated, tokens, ms, endpointName) = results[i];
                translations[i] = translated;

                // Apply glossary post-processing
                if (glossary is not null)
                    translations[i] = ApplyGlossaryPostProcess(translations[i], glossary);

                // Record recent translation
                var recent = new RecentTranslation(
                    texts[i], translations[i], DateTime.UtcNow, tokens, Math.Round(ms, 1), endpointName);
                _recentTranslations.Enqueue(recent);
                while (_recentTranslations.Count > MaxRecentTranslations)
                    _recentTranslations.TryDequeue(out _);
            }

            Interlocked.Add(ref _totalTranslated, texts.Count);
            return translations;
        }
        finally
        {
            _ = BroadcastStats(force: true);
        }
    }

    private async Task<(string translated, long tokens, double ms, string endpointName)> TranslateSingleAsync(
        string text, string from, string to,
        AiTranslationSettings ai, List<ApiEndpointConfig> endpoints,
        List<GlossaryEntry>? glossary, SemaphoreSlim semaphore, CancellationToken ct)
    {
        Interlocked.Increment(ref _queued);
        _ = BroadcastStats();

        try
        {
            // Timeout prevents HTTP connections from hanging indefinitely when
            // the game-side DLL sends more concurrent requests than the semaphore allows.
            var acquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(60), ct);
            if (!acquired)
            {
                Interlocked.Decrement(ref _queued);
                _ = BroadcastStats();
                logger.LogWarning("翻译队列超时: 等待超过 60 秒，当前排队 {Queued}", Interlocked.Read(ref _queued));
                throw new InvalidOperationException("翻译队列已满，请稍后重试");
            }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _queued);
            _ = BroadcastStats();
            throw;
        }

        // Acquired semaphore: transition from queued to translating
        Interlocked.Decrement(ref _queued);
        Interlocked.Increment(ref _translating);
        _ = BroadcastStats();

        ApiEndpointConfig? chosenEndpoint = null;
        try
        {
            chosenEndpoint = SelectEndpoint(endpoints);
            var sw = Stopwatch.StartNew();

            var (result, tokens) = await CallProviderAsync(chosenEndpoint, ai, [text], from, to, glossary, ct);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            // Update per-endpoint stats
            var stats = _endpointStats.GetOrAdd(chosenEndpoint.Id, _ => new EndpointStats());
            stats.RecordSuccess(elapsedMs);

            // Update global stats
            Interlocked.Add(ref _totalTokensUsed, tokens);
            Interlocked.Add(ref _totalResponseTimeMs, (long)elapsedMs);
            Interlocked.Increment(ref _totalRequests);
            _requestTimestamps.Enqueue(DateTime.UtcNow.Ticks);

            var translated = result.Count > 0 ? result[0] : text;
            return (translated, tokens, elapsedMs, chosenEndpoint.Name);
        }
        catch (Exception ex)
        {
            if (chosenEndpoint is not null)
            {
                var stats = _endpointStats.GetOrAdd(chosenEndpoint.Id, _ => new EndpointStats());
                stats.RecordError();
                logger.LogWarning(ex, "提供商 {Name} 翻译失败", chosenEndpoint.Name);
            }
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _translating);
            semaphore.Release(); // Release the same instance we acquired
            _ = BroadcastStats();
        }
    }

    // ── Load Balancer ──

    private ApiEndpointConfig SelectEndpoint(List<ApiEndpointConfig> endpoints)
    {
        if (endpoints.Count == 1) return endpoints[0];

        ApiEndpointConfig? best = null;
        double bestScore = double.MinValue;

        foreach (var ep in endpoints)
        {
            var stats = _endpointStats.GetOrAdd(ep.Id, _ => new EndpointStats());
            var score = CalculateScore(ep.Priority, stats);
            if (score > bestScore)
            {
                bestScore = score;
                best = ep;
            }
        }

        return best ?? endpoints[0];
    }

    private static double CalculateScore(int priority, EndpointStats stats)
    {
        var errorRate = stats.TotalCalls > 0 ? (double)stats.ErrorCount / stats.TotalCalls : 0;
        var avgMs = stats.AverageResponseTimeMs;

        // Priority weight + speed bonus - error penalty
        return priority * 10.0
            - errorRate * 50.0
            + 1000.0 / (avgMs + 100.0);
    }

    // ── Provider dispatch ──

    private async Task<(IList<string> translations, long tokens)> CallProviderAsync(
        ApiEndpointConfig endpoint, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<GlossaryEntry>? glossary, CancellationToken ct)
    {
        return endpoint.Provider switch
        {
            LlmProvider.OpenAI => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.DeepSeek => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.Qwen => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.GLM => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.Kimi => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.Custom => await CallOpenAiCompatAsync(endpoint, ai, texts, from, to, glossary,
                endpoint.ApiBaseUrl, ct),
            LlmProvider.Claude => await CallClaudeAsync(endpoint, ai, texts, from, to, glossary, ct),
            LlmProvider.Gemini => await CallGeminiAsync(endpoint, ai, texts, from, to, glossary, ct),
            _ => throw new NotSupportedException($"未支持的 LLM 提供商: {endpoint.Provider}")
        };
    }

    private static string GetDefaultBaseUrl(ApiEndpointConfig ep)
    {
        if (!string.IsNullOrWhiteSpace(ep.ApiBaseUrl)) return ep.ApiBaseUrl;
        return ep.Provider switch
        {
            LlmProvider.OpenAI => "https://api.openai.com/v1",
            LlmProvider.DeepSeek => "https://api.deepseek.com",
            LlmProvider.Qwen => "https://dashscope.aliyuncs.com/compatible-mode/v1",
            LlmProvider.GLM => "https://open.bigmodel.cn/api/paas/v4",
            LlmProvider.Kimi => "https://api.moonshot.cn/v1",
            _ => ep.ApiBaseUrl
        };
    }

    private static string GetDefaultModel(ApiEndpointConfig ep) => ep.Provider switch
    {
        LlmProvider.OpenAI => "gpt-4o-mini",
        LlmProvider.Claude => "claude-haiku-4-5-20251001",
        LlmProvider.Gemini => "gemini-2.0-flash",
        LlmProvider.DeepSeek => "deepseek-chat",
        LlmProvider.Qwen => "qwen-plus",
        LlmProvider.GLM => "glm-4-flash",
        LlmProvider.Kimi => "moonshot-v1-auto",
        _ => ""
    };

    // ── Raw LLM call (used by GlossaryExtractionService) ──

    /// <summary>
    /// Call an LLM endpoint with arbitrary system prompt and user content.
    /// Returns the raw text content and token count.
    /// </summary>
    public async Task<(string content, long tokens)> CallLlmRawAsync(
        ApiEndpointConfig endpoint, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        return endpoint.Provider switch
        {
            LlmProvider.OpenAI or LlmProvider.DeepSeek or LlmProvider.Qwen
                or LlmProvider.GLM or LlmProvider.Kimi
                => await CallOpenAiCompatRawAsync(endpoint, systemPrompt, userContent,
                    temperature, GetDefaultBaseUrl(endpoint), ct),
            LlmProvider.Custom => await CallOpenAiCompatRawAsync(endpoint, systemPrompt, userContent,
                temperature, endpoint.ApiBaseUrl, ct),
            LlmProvider.Claude => await CallClaudeRawAsync(endpoint, systemPrompt, userContent, temperature, ct),
            LlmProvider.Gemini => await CallGeminiRawAsync(endpoint, systemPrompt, userContent, temperature, ct),
            _ => throw new NotSupportedException($"未支持的 LLM 提供商: {endpoint.Provider}")
        };
    }

    // ── OpenAI-compatible (OpenAI, DeepSeek, Qwen, GLM, Kimi, Custom) ──

    private async Task<(string content, long tokens)> CallOpenAiCompatRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("API Base URL 未配置");

        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";

        var body = new
        {
            model,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ep.ApiKey);

        var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("{Provider} API 错误 {Status}: {Body}", ep.Provider, resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("LLM 响应格式无效");

        var tokens = ExtractOpenAiTokens(node);
        return (content, tokens);
    }

    private async Task<(IList<string>, long)> CallOpenAiCompatAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<GlossaryEntry>? glossary, string baseUrl, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallOpenAiCompatRawAsync(ep, systemPrompt, userContent, ai.Temperature, baseUrl, ct);
        return (ParseTranslationArray(content, texts.Count), tokens);
    }

    // ── Claude ──

    private async Task<(string content, long tokens)> CallClaudeRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(ep.ApiBaseUrl) ? "https://api.anthropic.com/v1" : ep.ApiBaseUrl;
        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/messages";

        var body = new
        {
            model,
            max_tokens = 4096,
            temperature,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userContent } }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", ep.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Claude API 错误 {Status}: {Body}", resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["content"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Claude 响应格式无效");

        var inputTokens = node?["usage"]?["input_tokens"]?.GetValue<long>() ?? 0;
        var outputTokens = node?["usage"]?["output_tokens"]?.GetValue<long>() ?? 0;
        return (content, inputTokens + outputTokens);
    }

    private async Task<(IList<string>, long)> CallClaudeAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<GlossaryEntry>? glossary, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallClaudeRawAsync(ep, systemPrompt, userContent, ai.Temperature, ct);
        return (ParseTranslationArray(content, texts.Count), tokens);
    }

    // ── Gemini ──

    private async Task<(string content, long tokens)> CallGeminiRawAsync(
        ApiEndpointConfig ep, string systemPrompt, string userContent,
        double temperature, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(ep.ModelName) ? GetDefaultModel(ep) : ep.ModelName;
        var baseUrl = string.IsNullOrWhiteSpace(ep.ApiBaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : ep.ApiBaseUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/models/{model}:generateContent?key={ep.ApiKey}";

        var combinedContent = systemPrompt + "\n\n" + userContent;
        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = combinedContent } } } },
            generationConfig = new { temperature }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("Gemini API 错误 {Status}: {Body}", resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Gemini 响应格式无效");

        var promptTokens = node?["usageMetadata"]?["promptTokenCount"]?.GetValue<long>() ?? 0;
        var candidateTokens = node?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<long>() ?? 0;
        return (content, promptTokens + candidateTokens);
    }

    private async Task<(IList<string>, long)> CallGeminiAsync(
        ApiEndpointConfig ep, AiTranslationSettings ai,
        IList<string> texts, string from, string to,
        List<GlossaryEntry>? glossary, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to, glossary);
        var userContent = JsonSerializer.Serialize(texts);
        var (content, tokens) = await CallGeminiRawAsync(ep, systemPrompt, userContent, ai.Temperature, ct);
        return (ParseTranslationArray(content, texts.Count), tokens);
    }

    // ── System prompt + glossary injection ──

    private static string BuildSystemPrompt(string template, string from, string to, List<GlossaryEntry>? glossary)
    {
        var prompt = template.Replace("{from}", from).Replace("{to}", to);

        if (glossary is { Count: > 0 })
        {
            var sb = new StringBuilder(prompt);
            sb.Append("\n\nGlossary (use these exact translations):\n");
            foreach (var entry in glossary)
            {
                if (entry.IsRegex)
                    sb.Append($"  Pattern: /{entry.Original}/ → {entry.Translation}\n");
                else
                    sb.Append($"  {entry.Original} → {entry.Translation}\n");
            }
            prompt = sb.ToString();
        }

        return prompt;
    }

    // ── Glossary post-processing ──

    private static string ApplyGlossaryPostProcess(string translated, List<GlossaryEntry> glossary)
    {
        foreach (var entry in glossary)
        {
            if (string.IsNullOrEmpty(entry.Original)) continue;

            if (entry.IsRegex)
            {
                try
                {
                    translated = Regex.Replace(translated, entry.Original, entry.Translation,
                        RegexOptions.None, TimeSpan.FromMilliseconds(100));
                }
                catch
                {
                    // Invalid regex — skip silently
                }
            }
            else
            {
                translated = translated.Replace(entry.Original, entry.Translation);
            }
        }
        return translated;
    }

    // ── Token extraction helpers ──

    private static long ExtractOpenAiTokens(JsonNode? node)
    {
        var usage = node?["usage"];
        if (usage is null) return 0;
        var total = usage["total_tokens"]?.GetValue<long>();
        if (total.HasValue) return total.Value;
        var prompt = usage["prompt_tokens"]?.GetValue<long>() ?? 0;
        var completion = usage["completion_tokens"]?.GetValue<long>() ?? 0;
        return prompt + completion;
    }

    // ── Response parsing ──

    private static IList<string> ParseTranslationArray(string content, int expectedCount)
    {
        var json = content.Trim();

        // LLM may wrap JSON in markdown code blocks
        if (json.StartsWith("```"))
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
                json = json[start..(end + 1)];
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(json);
            if (result is not null)
                return result;
        }
        catch
        {
            // Fall through to fallback
        }

        // Fallback: return raw content for all slots
        return Enumerable.Repeat(content, expectedCount).ToList();
    }

    // ── Concurrency ──

    private readonly object _semaphoreLock = new();

    private void EnsureSemaphore(int maxConcurrency)
    {
        lock (_semaphoreLock)
        {
            if (_semaphore is not null && _currentMaxConcurrency == maxConcurrency) return;
            var old = _semaphore;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _currentMaxConcurrency = maxConcurrency;
            // Defer disposal: in-flight tasks still hold references to the old semaphore.
            // Wait longer than the LLM HTTP timeout (120s) before disposing.
            if (old is not null)
                _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(_ => old.Dispose());
        }
    }

    // ── SignalR ──

    /// <summary>
    /// Throttled broadcast: skips if last broadcast was less than 200ms ago.
    /// High-concurrency translation can trigger hundreds of state changes per second;
    /// throttling reduces SignalR overhead while keeping the dashboard responsive.
    /// </summary>
    private async Task BroadcastStats(bool force = false)
    {
        if (!force)
        {
            var now = DateTime.UtcNow.Ticks;
            var last = Interlocked.Read(ref _lastBroadcastTicks);
            if (now - last < TimeSpan.FromMilliseconds(200).Ticks)
                return;
            // CAS: only the thread that wins the race proceeds to broadcast
            if (Interlocked.CompareExchange(ref _lastBroadcastTicks, now, last) != last)
                return;
        }

        try
        {
            await hubContext.Clients.Group("ai-translation")
                .SendAsync("statsUpdate", GetStats());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI 翻译统计推送失败");
        }
    }

    // ── Test translation ──

    public async Task<IList<EndpointTestResult>> TestTranslateAsync(
        IList<ApiEndpointConfig> endpoints, string systemPrompt, double temperature, CancellationToken ct)
    {
        if (endpoints.Count == 0)
            throw new InvalidOperationException("没有要测试的 AI 提供商");

        string[] testTexts = ["Hello, world!", "Good morning"];
        var ai = new AiTranslationSettings { SystemPrompt = systemPrompt, Temperature = temperature };

        var tasks = endpoints.Select(async ep =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var (translations, _) = await CallProviderAsync(ep, ai, testTexts, "en", "zh", null, ct);
                sw.Stop();
                return new EndpointTestResult(ep.Id, ep.Name, true, translations, null, Math.Round(sw.Elapsed.TotalMilliseconds, 1));
            }
            catch (Exception ex)
            {
                return new EndpointTestResult(ep.Id, ep.Name, false, null, ex.Message, 0);
            }
        });

        return await Task.WhenAll(tasks);
    }

    // ── Model list fetching ──

    public async Task<IList<string>> FetchModelsAsync(LlmProvider provider, string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        return provider switch
        {
            LlmProvider.OpenAI or LlmProvider.DeepSeek or LlmProvider.Qwen or LlmProvider.Kimi or LlmProvider.Custom
                => await FetchOpenAiModelsAsync(provider, apiBaseUrl, apiKey, ct),
            LlmProvider.Gemini => await FetchGeminiModelsAsync(apiBaseUrl, apiKey, ct),
            _ => [] // Claude, GLM — no model list API
        };
    }

    private async Task<IList<string>> FetchOpenAiModelsAsync(LlmProvider provider, string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? GetDefaultBaseUrl(new ApiEndpointConfig { Provider = provider })
            : apiBaseUrl;
        var endpoint = baseUrl.TrimEnd('/') + "/models";

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(json);
        var data = node?["data"]?.AsArray();
        if (data is null) return [];

        return data
            .Select(m => m?["id"]?.GetValue<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderBy(id => id)
            .ToList()!;
    }

    private async Task<IList<string>> FetchGeminiModelsAsync(string apiBaseUrl, string apiKey, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : apiBaseUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/models?key={apiKey}";

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);

        var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return [];

        var json = await resp.Content.ReadAsStringAsync(ct);
        var node = JsonNode.Parse(json);
        var models = node?["models"]?.AsArray();
        if (models is null) return [];

        return models
            .Select(m => m?["name"]?.GetValue<string>()?.Replace("models/", ""))
            .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("gemini"))
            .OrderBy(name => name)
            .ToList()!;
    }

    // ── Per-endpoint stats tracker ──

    private sealed class EndpointStats
    {
        private long _totalCalls;
        private long _errorCount;
        private long _totalMs;

        public long TotalCalls => Interlocked.Read(ref _totalCalls);
        public long ErrorCount => Interlocked.Read(ref _errorCount);
        public double AverageResponseTimeMs
        {
            get
            {
                var total = Interlocked.Read(ref _totalCalls) - Interlocked.Read(ref _errorCount);
                return total > 0 ? (double)Interlocked.Read(ref _totalMs) / total : 500;
            }
        }

        public void RecordSuccess(double ms)
        {
            Interlocked.Increment(ref _totalCalls);
            Interlocked.Add(ref _totalMs, (long)ms);
        }

        public void RecordError()
        {
            Interlocked.Increment(ref _totalCalls);
            Interlocked.Increment(ref _errorCount);
        }
    }
}
