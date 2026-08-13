using System.Text.Json.Nodes;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

internal sealed record LlmApiRequest(string RelativePath, JsonObject Body);

/// <summary>
/// Converts the toolkit's provider-neutral endpoint settings into each provider's wire format.
/// Keeping this logic separate also makes request and response contracts directly testable.
/// </summary>
internal static class LlmApiAdapter
{
    internal static LlmApiRequest BuildOpenAiCompatibleRequest(
        ApiEndpointConfig endpoint,
        string model,
        string systemPrompt,
        string userContent,
        double temperature,
        double? minP = null,
        double? repeatPenalty = null,
        int? maxTokens = null)
    {
        if (endpoint.ApiFormat == LlmApiFormat.Responses)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["instructions"] = systemPrompt,
                ["input"] = userContent
            };

            // OpenAI and Qwen persist Responses by default. Translation requests are deliberately stateless.
            // DeepSeek's Responses implementation is always stateless and does not expose this request field.
            if (endpoint.Provider is LlmProvider.OpenAI or LlmProvider.Qwen)
                body["store"] = false;

            AddResponsesReasoning(body, endpoint.Provider, endpoint.ReasoningEffort);

            if (ShouldSendTemperature(endpoint, model, isLocal: false))
                body["temperature"] = temperature;
            if (maxTokens.HasValue)
                body["max_output_tokens"] = maxTokens.Value;

            return new LlmApiRequest("/responses", body);
        }

        var chatBody = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = userContent }
            }
        };

        var isLocal = minP.HasValue || repeatPenalty.HasValue || maxTokens.HasValue;
        if (ShouldSendTemperature(endpoint, model, isLocal))
            chatBody["temperature"] = temperature;

        AddChatReasoning(chatBody, endpoint.Provider, model, endpoint.ReasoningEffort);

        if (isLocal)
        {
            chatBody["min_p"] = minP ?? 0.05;
            chatBody["repeat_penalty"] = repeatPenalty ?? 1.0;
            chatBody["max_tokens"] = maxTokens ?? 4096;
        }

        return new LlmApiRequest("/chat/completions", chatBody);
    }

    internal static JsonObject BuildClaudeRequest(
        ApiEndpointConfig endpoint,
        string model,
        string systemPrompt,
        string userContent,
        double temperature)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 4096,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = userContent }
            }
        };

        if (endpoint.ReasoningEffort == LlmReasoningEffort.None && !IsAlwaysThinkingClaudeModel(model))
        {
            body["thinking"] = new JsonObject { ["type"] = "disabled" };
        }
        else if (endpoint.ReasoningEffort != LlmReasoningEffort.Default)
        {
            body["thinking"] = new JsonObject { ["type"] = "adaptive" };
            body["output_config"] = new JsonObject
            {
                ["effort"] = ToClaudeEffort(
                    endpoint.ReasoningEffort == LlmReasoningEffort.None
                        ? LlmReasoningEffort.Low
                        : endpoint.ReasoningEffort)
            };
        }

        // Claude 5 models use adaptive reasoning/effort instead of custom sampling values.
        if (!IsClaudeFiveModel(model) && endpoint.ReasoningEffort is LlmReasoningEffort.Default or LlmReasoningEffort.None)
            body["temperature"] = temperature;

        return body;
    }

    internal static JsonObject BuildGeminiRequest(
        ApiEndpointConfig endpoint,
        string model,
        string systemPrompt,
        string userContent,
        double temperature)
    {
        var generationConfig = new JsonObject();

        // Gemini 3.x chooses sampling internally and rejects legacy sampling controls.
        if (!model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            generationConfig["temperature"] = temperature;

        if (endpoint.ReasoningEffort != LlmReasoningEffort.Default)
        {
            var thinkingConfig = new JsonObject();
            if (model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase))
            {
                thinkingConfig["thinkingBudget"] = ToGeminiThinkingBudget(
                    endpoint.ReasoningEffort,
                    canDisable: model.StartsWith("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Current Gemini 3 models cannot fully disable thinking; minimal is their lowest level.
                thinkingConfig["thinkingLevel"] = ToGeminiThinkingLevel(endpoint.ReasoningEffort);
            }
            generationConfig["thinkingConfig"] = thinkingConfig;
        }

        return new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = userContent } }
                }
            },
            ["generationConfig"] = generationConfig
        };
    }

    internal static string ExtractOpenAiCompatibleText(JsonNode? node, LlmApiFormat format)
    {
        if (format == LlmApiFormat.ChatCompletions)
        {
            return node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                ?? throw new InvalidOperationException("LLM Chat Completions 响应格式无效");
        }

        if (node?["output_text"] is JsonValue outputTextValue
            && outputTextValue.TryGetValue<string>(out var topLevelText)
            && !string.IsNullOrEmpty(topLevelText))
        {
            return topLevelText;
        }

        if (node?["output"] is JsonArray output)
        {
            var texts = new List<string>();
            foreach (var item in output)
            {
                if (!string.Equals(item?["type"]?.GetValue<string>(), "message", StringComparison.Ordinal))
                    continue;

                if (item?["content"] is not JsonArray content)
                    continue;

                foreach (var part in content)
                {
                    if (string.Equals(part?["type"]?.GetValue<string>(), "output_text", StringComparison.Ordinal)
                        && part?["text"] is JsonValue textValue
                        && textValue.TryGetValue<string>(out var text))
                    {
                        texts.Add(text);
                    }
                }
            }

            if (texts.Count > 0)
                return string.Concat(texts);
        }

        throw new InvalidOperationException("LLM Responses 响应中没有 output_text");
    }

    internal static string ExtractClaudeText(JsonNode? node)
    {
        if (node?["content"] is JsonArray content)
        {
            var texts = new List<string>();
            foreach (var part in content)
            {
                if (string.Equals(part?["type"]?.GetValue<string>(), "text", StringComparison.Ordinal)
                    && part?["text"] is JsonValue textValue
                    && textValue.TryGetValue<string>(out var text))
                {
                    texts.Add(text);
                }
            }

            if (texts.Count > 0)
                return string.Concat(texts);
        }

        throw new InvalidOperationException("Claude 响应中没有文本内容");
    }

    internal static string ExtractGeminiText(JsonNode? node)
    {
        if (node?["candidates"]?[0]?["content"]?["parts"] is JsonArray parts)
        {
            var texts = new List<string>();
            foreach (var part in parts)
            {
                var isThought = part?["thought"] is JsonValue thoughtValue
                    && thoughtValue.TryGetValue<bool>(out var thought)
                    && thought;
                if (!isThought
                    && part?["text"] is JsonValue textValue
                    && textValue.TryGetValue<string>(out var text))
                {
                    texts.Add(text);
                }
            }

            if (texts.Count > 0)
                return string.Concat(texts);
        }

        throw new InvalidOperationException("Gemini 响应中没有最终文本");
    }

    internal static long ExtractOpenAiCompatibleTokens(JsonNode? node)
    {
        var usage = node?["usage"];
        if (usage is null) return 0;

        var total = usage["total_tokens"]?.GetValue<long>();
        if (total.HasValue) return total.Value;

        var input = usage["input_tokens"]?.GetValue<long>()
            ?? usage["prompt_tokens"]?.GetValue<long>()
            ?? 0;
        var output = usage["output_tokens"]?.GetValue<long>()
            ?? usage["completion_tokens"]?.GetValue<long>()
            ?? 0;
        return input + output;
    }

    private static void AddResponsesReasoning(
        JsonObject body,
        LlmProvider provider,
        LlmReasoningEffort effort)
    {
        if (effort == LlmReasoningEffort.Default)
            return;

        body["reasoning"] = new JsonObject
        {
            ["effort"] = ToResponsesEffort(provider, effort)
        };
    }

    private static void AddChatReasoning(
        JsonObject body,
        LlmProvider provider,
        string model,
        LlmReasoningEffort effort)
    {
        if (effort == LlmReasoningEffort.Default)
            return;

        switch (provider)
        {
            case LlmProvider.DeepSeek:
                body["thinking"] = new JsonObject { ["type"] = effort == LlmReasoningEffort.None ? "disabled" : "enabled" };
                if (effort != LlmReasoningEffort.None)
                    body["reasoning_effort"] = ToThreeLevelEffort(effort);
                break;
            case LlmProvider.Qwen:
                body["enable_thinking"] = effort != LlmReasoningEffort.None;
                break;
            case LlmProvider.GLM:
                body["thinking"] = new JsonObject { ["type"] = effort == LlmReasoningEffort.None ? "disabled" : "enabled" };
                body["reasoning_effort"] = ToWireEffort(effort);
                break;
            case LlmProvider.Kimi:
                if (model.StartsWith("kimi-k3", StringComparison.OrdinalIgnoreCase))
                {
                    body["reasoning_effort"] = ToThreeLevelEffort(
                        effort == LlmReasoningEffort.None ? LlmReasoningEffort.Low : effort);
                }
                else if (model.StartsWith("kimi-k2.7-code", StringComparison.OrdinalIgnoreCase))
                {
                    body["thinking"] = new JsonObject { ["type"] = "enabled" };
                }
                else
                {
                    body["thinking"] = new JsonObject { ["type"] = effort == LlmReasoningEffort.None ? "disabled" : "enabled" };
                }
                break;
            default:
                body["reasoning_effort"] = ToResponsesEffort(provider, effort);
                break;
        }
    }

    private static bool ShouldSendTemperature(ApiEndpointConfig endpoint, string model, bool isLocal)
    {
        if (isLocal)
            return true;

        if (endpoint.ReasoningEffort is not (LlmReasoningEffort.Default or LlmReasoningEffort.None))
            return false;

        if (endpoint.Provider == LlmProvider.Kimi
            && (model.StartsWith("kimi-k2", StringComparison.OrdinalIgnoreCase)
                || model.StartsWith("kimi-k3", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return endpoint.Provider != LlmProvider.OpenAI
            || !model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
            || endpoint.ReasoningEffort == LlmReasoningEffort.None;
    }

    private static string ToResponsesEffort(LlmProvider provider, LlmReasoningEffort effort)
    {
        if (provider == LlmProvider.OpenAI && effort == LlmReasoningEffort.Minimal)
            return "low";
        return ToWireEffort(effort);
    }

    private static string ToWireEffort(LlmReasoningEffort effort) => effort switch
    {
        LlmReasoningEffort.None => "none",
        LlmReasoningEffort.Minimal => "minimal",
        LlmReasoningEffort.Low => "low",
        LlmReasoningEffort.Medium => "medium",
        LlmReasoningEffort.High => "high",
        LlmReasoningEffort.XHigh => "xhigh",
        LlmReasoningEffort.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    private static string ToThreeLevelEffort(LlmReasoningEffort effort) => effort switch
    {
        LlmReasoningEffort.Minimal or LlmReasoningEffort.Low => "low",
        LlmReasoningEffort.Max => "max",
        _ => "high"
    };

    private static string ToClaudeEffort(LlmReasoningEffort effort) => effort switch
    {
        LlmReasoningEffort.Minimal => "low",
        LlmReasoningEffort.Low => "low",
        LlmReasoningEffort.Medium => "medium",
        LlmReasoningEffort.High => "high",
        LlmReasoningEffort.XHigh => "xhigh",
        LlmReasoningEffort.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    private static string ToGeminiThinkingLevel(LlmReasoningEffort effort) => effort switch
    {
        LlmReasoningEffort.None or LlmReasoningEffort.Minimal => "minimal",
        LlmReasoningEffort.Low => "low",
        LlmReasoningEffort.Medium => "medium",
        _ => "high"
    };

    private static int ToGeminiThinkingBudget(LlmReasoningEffort effort, bool canDisable) => effort switch
    {
        LlmReasoningEffort.None => canDisable ? 0 : 128,
        LlmReasoningEffort.Minimal => 128,
        LlmReasoningEffort.Low => 1024,
        LlmReasoningEffort.Medium => 4096,
        LlmReasoningEffort.High => 8192,
        LlmReasoningEffort.XHigh => 16384,
        LlmReasoningEffort.Max => 24576,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    private static bool IsClaudeFiveModel(string model)
        => model.StartsWith("claude-fable-5", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude-opus-5", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude-sonnet-5", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlwaysThinkingClaudeModel(string model)
        => model.StartsWith("claude-fable-5", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("claude-mythos", StringComparison.OrdinalIgnoreCase);
}
