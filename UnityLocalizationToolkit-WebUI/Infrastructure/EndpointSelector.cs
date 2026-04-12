using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Infrastructure;

/// <summary>
/// Shared endpoint selection logic for services that call LLM APIs.
/// </summary>
public static class EndpointSelector
{
    /// <summary>
    /// Select the highest-priority enabled endpoint with a valid API key.
    /// </summary>
    public static ApiEndpointConfig? SelectBestEndpoint(IList<ApiEndpointConfig> endpoints)
    {
        return endpoints
            .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey))
            .OrderByDescending(e => e.Priority)
            .FirstOrDefault();
    }

    /// <summary>
    /// Select a specific endpoint by ID, falling back to highest-priority available.
    /// </summary>
    public static ApiEndpointConfig? SelectEndpoint(IList<ApiEndpointConfig> endpoints, string? preferredId)
    {
        var enabled = endpoints
            .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey))
            .ToList();
        if (enabled.Count == 0) return null;

        if (!string.IsNullOrEmpty(preferredId))
        {
            var specific = enabled.FirstOrDefault(e => e.Id == preferredId);
            if (specific is not null) return specific;
        }

        return enabled.OrderByDescending(e => e.Priority).First();
    }
}
