using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using XUnityToolkit_WebUI.Hubs;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class LlmTranslationService(
    IHttpClientFactory httpClientFactory,
    AppSettingsService settingsService,
    IHubContext<InstallProgressHub> hubContext,
    ILogger<LlmTranslationService> logger)
{
    private long _totalTranslated;
    private int _inProgress;
    private long _lastRequestTicks; // 0 = never

    public TranslationStats GetStats() => new(
        TotalTranslated: Interlocked.Read(ref _totalTranslated),
        InProgress: Volatile.Read(ref _inProgress),
        LastRequestAt: Interlocked.Read(ref _lastRequestTicks) is var ticks and > 0
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null
    );

    public async Task<IList<string>> TranslateAsync(IList<string> texts, string from, string to, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _inProgress);
        _ = BroadcastStats();

        try
        {
            var settings = await settingsService.GetAsync(ct);
            var ai = settings.AiTranslation;

            if (string.IsNullOrWhiteSpace(ai.ApiKey))
                throw new InvalidOperationException("AI 翻译 API Key 未配置，请在设置页面配置");

            var result = ai.Provider switch
            {
                LlmProvider.OpenAI => await CallOpenAiCompatAsync(ai, texts, from, to, GetOpenAiBaseUrl(ai), ct),
                LlmProvider.Custom => await CallOpenAiCompatAsync(ai, texts, from, to, ai.ApiBaseUrl, ct),
                LlmProvider.Claude => await CallClaudeAsync(ai, texts, from, to, ct),
                LlmProvider.Gemini => await CallGeminiAsync(ai, texts, from, to, ct),
                _ => throw new NotSupportedException($"未支持的 LLM 提供商: {ai.Provider}")
            };

            Interlocked.Add(ref _totalTranslated, texts.Count);
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _inProgress);
            _ = BroadcastStats();
        }
    }

    private async Task BroadcastStats()
    {
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

    private async Task<IList<string>> CallOpenAiCompatAsync(
        AiTranslationSettings ai, IList<string> texts, string from, string to,
        string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("API Base URL 未配置");

        var model = string.IsNullOrWhiteSpace(ai.ModelName) ? "gpt-4o-mini" : ai.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to);

        var body = new
        {
            model,
            temperature = ai.Temperature,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = JsonSerializer.Serialize(texts) }
            }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ai.ApiKey);

        var resp = await client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("OpenAI API 错误 {Status}: {Body}", resp.StatusCode, json);
            throw new HttpRequestException($"LLM API 返回错误: {resp.StatusCode}");
        }

        var node = JsonNode.Parse(json);
        var content = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("LLM 响应格式无效");

        return ParseTranslationArray(content, texts.Count);
    }

    private async Task<IList<string>> CallClaudeAsync(
        AiTranslationSettings ai, IList<string> texts, string from, string to, CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(ai.ApiBaseUrl)
            ? "https://api.anthropic.com/v1"
            : ai.ApiBaseUrl;
        var model = string.IsNullOrWhiteSpace(ai.ModelName) ? "claude-haiku-4-5-20251001" : ai.ModelName;
        var endpoint = baseUrl.TrimEnd('/') + "/messages";
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to);

        var body = new
        {
            model,
            max_tokens = 4096,
            temperature = ai.Temperature,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = JsonSerializer.Serialize(texts) } }
        };

        var client = httpClientFactory.CreateClient("LLM");
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", ai.ApiKey);
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

        return ParseTranslationArray(content, texts.Count);
    }

    private async Task<IList<string>> CallGeminiAsync(
        AiTranslationSettings ai, IList<string> texts, string from, string to, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(ai.ModelName) ? "gemini-2.0-flash" : ai.ModelName;
        var baseUrl = string.IsNullOrWhiteSpace(ai.ApiBaseUrl)
            ? "https://generativelanguage.googleapis.com/v1beta"
            : ai.ApiBaseUrl;
        var endpoint = $"{baseUrl.TrimEnd('/')}/models/{model}:generateContent?key={ai.ApiKey}";
        var systemPrompt = BuildSystemPrompt(ai.SystemPrompt, from, to);

        var userText = systemPrompt + "\n\n" + JsonSerializer.Serialize(texts);
        var body = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig = new { temperature = ai.Temperature }
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

        return ParseTranslationArray(content, texts.Count);
    }

    private static string GetOpenAiBaseUrl(AiTranslationSettings ai) =>
        string.IsNullOrWhiteSpace(ai.ApiBaseUrl) ? "https://api.openai.com/v1" : ai.ApiBaseUrl;

    private static string BuildSystemPrompt(string template, string from, string to) =>
        template.Replace("{from}", from).Replace("{to}", to);

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
}
