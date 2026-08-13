using System.Text.Json.Nodes;
using System.Text.Json;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class LlmApiAdapterTests
{
    [Fact]
    public void BuildOpenAiCompatibleRequest_UsesResponsesAndDisablesDeepSeekThinking()
    {
        var endpoint = new ApiEndpointConfig
        {
            Provider = LlmProvider.DeepSeek,
            ApiFormat = LlmApiFormat.Responses,
            ReasoningEffort = LlmReasoningEffort.None
        };

        var request = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            endpoint, "deepseek-v4-flash", "system", "input", 0.3);

        Assert.Equal("/responses", request.RelativePath);
        Assert.Equal("system", request.Body["instructions"]?.GetValue<string>());
        Assert.Equal("input", request.Body["input"]?.GetValue<string>());
        Assert.Equal("none", request.Body["reasoning"]?["effort"]?.GetValue<string>());
        Assert.Null(request.Body["messages"]);
        Assert.Null(request.Body["store"]);
    }

    [Fact]
    public void BuildOpenAiCompatibleRequest_PreservesLegacyChatFormatByDefault()
    {
        var endpoint = new ApiEndpointConfig { Provider = LlmProvider.OpenAI };

        var request = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            endpoint, "gpt-4o-mini", "system", "input", 0.3);

        Assert.Equal("/chat/completions", request.RelativePath);
        Assert.NotNull(request.Body["messages"]);
        Assert.Null(request.Body["reasoning_effort"]);
    }

    [Fact]
    public void BuildOpenAiCompatibleRequest_MapsProviderSpecificChatThinkingFields()
    {
        var deepSeek = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.DeepSeek,
                ReasoningEffort = LlmReasoningEffort.None
            },
            "deepseek-v4-flash", "system", "input", 0.3);

        var qwen = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.Qwen,
                ReasoningEffort = LlmReasoningEffort.None
            },
            "qwen3.7-plus", "system", "input", 0.3);

        var glm = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.GLM,
                ReasoningEffort = LlmReasoningEffort.High
            },
            "glm-5.2", "system", "input", 0.3);

        Assert.Equal("disabled", deepSeek.Body["thinking"]?["type"]?.GetValue<string>());
        Assert.False(qwen.Body["enable_thinking"]?.GetValue<bool>());
        Assert.Equal("enabled", glm.Body["thinking"]?["type"]?.GetValue<string>());
        Assert.Equal("high", glm.Body["reasoning_effort"]?.GetValue<string>());
    }

    [Fact]
    public void BuildOpenAiCompatibleRequest_HandlesKimiToggleableAndAlwaysThinkingModels()
    {
        var k2 = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.Kimi,
                ReasoningEffort = LlmReasoningEffort.None
            },
            "kimi-k2.6", "system", "input", 0.3);

        var k3 = LlmApiAdapter.BuildOpenAiCompatibleRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.Kimi,
                ReasoningEffort = LlmReasoningEffort.None
            },
            "kimi-k3", "system", "input", 0.3);

        Assert.Equal("disabled", k2.Body["thinking"]?["type"]?.GetValue<string>());
        Assert.Null(k2.Body["reasoning_effort"]);
        Assert.Null(k2.Body["temperature"]);
        Assert.Null(k3.Body["thinking"]);
        Assert.Equal("low", k3.Body["reasoning_effort"]?.GetValue<string>());
        Assert.Null(k3.Body["temperature"]);
    }

    [Fact]
    public void ExtractOpenAiCompatibleText_SkipsReasoningItemsAndCountsResponsesTokens()
    {
        var response = JsonNode.Parse("""
            {
              "output": [
                { "type": "reasoning", "content": [{ "type": "reasoning_text", "text": "hidden" }] },
                { "type": "message", "content": [{ "type": "output_text", "text": "[\"译文\"]" }] }
              ],
              "usage": { "input_tokens": 11, "output_tokens": 7 }
            }
            """);

        var content = LlmApiAdapter.ExtractOpenAiCompatibleText(response, LlmApiFormat.Responses);
        var tokens = LlmApiAdapter.ExtractOpenAiCompatibleTokens(response);

        Assert.Equal("[\"译文\"]", content);
        Assert.Equal(18, tokens);
    }

    [Fact]
    public void ClaudeAdapter_DisablesThinkingAndSkipsThinkingBlocks()
    {
        var endpoint = new ApiEndpointConfig
        {
            Provider = LlmProvider.Claude,
            ReasoningEffort = LlmReasoningEffort.None
        };
        var body = LlmApiAdapter.BuildClaudeRequest(
            endpoint, "claude-sonnet-5", "system", "input", 0.3);
        var response = JsonNode.Parse("""
            {
              "content": [
                { "type": "thinking", "thinking": "hidden" },
                { "type": "text", "text": "[\"译文\"]" }
              ]
            }
            """);

        Assert.Equal("disabled", body["thinking"]?["type"]?.GetValue<string>());
        Assert.Null(body["temperature"]);
        Assert.Equal("[\"译文\"]", LlmApiAdapter.ExtractClaudeText(response));
    }

    [Fact]
    public void GeminiAdapter_UsesMinimumThinkingForGeminiThreeAndSkipsThoughtParts()
    {
        var endpoint = new ApiEndpointConfig
        {
            Provider = LlmProvider.Gemini,
            ReasoningEffort = LlmReasoningEffort.None
        };
        var body = LlmApiAdapter.BuildGeminiRequest(
            endpoint, "gemini-3.6-flash", "system", "input", 0.3);
        var response = JsonNode.Parse("""
            {
              "candidates": [{
                "content": { "parts": [
                  { "thought": true, "text": "hidden" },
                  { "text": "[\"译文\"]" }
                ]}
              }]
            }
            """);

        Assert.Equal(
            "minimal",
            body["generationConfig"]?["thinkingConfig"]?["thinkingLevel"]?.GetValue<string>());
        Assert.Null(body["generationConfig"]?["temperature"]);
        Assert.Equal("[\"译文\"]", LlmApiAdapter.ExtractGeminiText(response));
    }


    [Fact]
    public void GeminiAdapter_DisablesThinkingForGeminiTwoPointFiveFlash()
    {
        var body = LlmApiAdapter.BuildGeminiRequest(
            new ApiEndpointConfig
            {
                Provider = LlmProvider.Gemini,
                ReasoningEffort = LlmReasoningEffort.None
            },
            "gemini-2.5-flash", "system", "input", 0.3);

        Assert.Equal(0, body["generationConfig"]?["thinkingConfig"]?["thinkingBudget"]?.GetValue<int>());
    }

    [Fact]
    public void ApiEndpointConfig_OldSavedJsonKeepsLegacyProtocolDefaults()
    {
        var endpoint = JsonSerializer.Deserialize<ApiEndpointConfig>(
            """{"Provider":"DeepSeek","ModelName":"deepseek-v3"}""",
            FileHelper.DataJsonOptions);

        Assert.NotNull(endpoint);
        Assert.Equal(LlmApiFormat.ChatCompletions, endpoint.ApiFormat);
        Assert.Equal(LlmReasoningEffort.Default, endpoint.ReasoningEffort);
    }
}
