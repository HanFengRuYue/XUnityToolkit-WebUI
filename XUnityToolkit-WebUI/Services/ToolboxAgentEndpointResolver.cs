using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed record ToolboxAgentEndpointResolution(
    ApiEndpointConfig? Endpoint,
    string? Error,
    bool IsAutomatic);

/// <summary>
/// Single source of truth for the cloud endpoint used by chat, plugin diagnosis and repair planning.
/// It is deliberately independent from the real-time translation switch and active mode.
/// </summary>
public sealed class ToolboxAgentEndpointResolver(AppSettingsService settingsService)
{
    public async Task<ToolboxAgentEndpointResolution> ResolveAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAsync(ct);
        return Resolve(settings.AiTranslation);
    }

    internal static ToolboxAgentEndpointResolution Resolve(AiTranslationSettings ai)
    {
        var available = GetAvailableCloudEndpoints(ai);
        var configuredId = string.IsNullOrWhiteSpace(ai.AgentEndpointId)
            ? null
            : ai.AgentEndpointId.Trim();

        if (configuredId is not null)
        {
            var configured = available.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, configuredId, StringComparison.Ordinal));
            return configured is null
                ? new ToolboxAgentEndpointResolution(
                    null,
                    "工具箱智能体所选云端端点已禁用、被删除或配置无效，请在 AI 提供商设置中重新选择。",
                    false)
                : new ToolboxAgentEndpointResolution(configured, null, false);
        }

        var automatic = EndpointSelector.SelectBestEndpoint(available);
        return automatic is null
            ? new ToolboxAgentEndpointResolution(
                null,
                "当前没有已启用且配置有效的云端 AI 端点，工具箱智能体无法运行。",
                true)
            : new ToolboxAgentEndpointResolution(automatic, null, true);
    }

    internal static List<ApiEndpointConfig> GetAvailableCloudEndpoints(AiTranslationSettings ai) =>
        ai.Endpoints
            .Where(endpoint => endpoint.Enabled
                               && !string.IsNullOrWhiteSpace(endpoint.ApiKey)
                               && !string.Equals(endpoint.ApiKey, "local", StringComparison.OrdinalIgnoreCase))
            .ToList();
}
