using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services;

internal static class LlmEndpointResolver
{
    public static List<ApiEndpointConfig> BuildEffectiveEndpoints(
        AiTranslationSettings settings,
        ApiEndpointConfig? runtimeLocalEndpoint)
    {
        var endpoints = settings.Endpoints
            .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey))
            .Select(Clone)
            .ToList();

        if (!string.Equals(settings.ActiveMode, "local", StringComparison.OrdinalIgnoreCase))
            return endpoints;

        if (runtimeLocalEndpoint is null)
            return endpoints;

        endpoints.RemoveAll(e =>
            string.Equals(e.Id, runtimeLocalEndpoint.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.ApiKey, "local", StringComparison.OrdinalIgnoreCase));

        endpoints.Insert(0, Clone(runtimeLocalEndpoint));
        return endpoints;
    }

    private static ApiEndpointConfig Clone(ApiEndpointConfig endpoint) => new()
    {
        Id = endpoint.Id,
        Name = endpoint.Name,
        Provider = endpoint.Provider,
        ApiBaseUrl = endpoint.ApiBaseUrl,
        ApiKey = endpoint.ApiKey,
        ModelName = endpoint.ModelName,
        Priority = endpoint.Priority,
        Enabled = endpoint.Enabled
    };
}
