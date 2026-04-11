using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class LlmEndpointResolverTests
{
    [Fact]
    public void BuildEffectiveEndpoints_ReplacesPersistedLocalEndpointInLocalMode()
    {
        var settings = new AiTranslationSettings
        {
            ActiveMode = "local",
            Endpoints =
            [
                new ApiEndpointConfig
                {
                    Id = "local-1",
                    Name = "本地模型",
                    Provider = LlmProvider.Custom,
                    ApiBaseUrl = "http://127.0.0.1:58381/v1",
                    ApiKey = "local",
                    ModelName = "",
                    Priority = 8,
                    Enabled = true
                }
            ]
        };

        var runtimeLocalEndpoint = new ApiEndpointConfig
        {
            Id = "local-1",
            Name = "本地模型",
            Provider = LlmProvider.Custom,
            ApiBaseUrl = "http://127.0.0.1:53266/v1",
            ApiKey = "local",
            ModelName = "Qwen3.5-9B-Q4_K_M.gguf",
            Priority = 8,
            Enabled = true
        };

        var endpoints = LlmEndpointResolver.BuildEffectiveEndpoints(settings, runtimeLocalEndpoint);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("http://127.0.0.1:53266/v1", endpoint.ApiBaseUrl);
        Assert.Equal("Qwen3.5-9B-Q4_K_M.gguf", endpoint.ModelName);
    }

    [Fact]
    public void BuildEffectiveEndpoints_PreservesConfiguredCloudEndpointsOutsideLocalMode()
    {
        var settings = new AiTranslationSettings
        {
            ActiveMode = "cloud",
            Endpoints =
            [
                new ApiEndpointConfig
                {
                    Id = "cloud-1",
                    Name = "云端",
                    Provider = LlmProvider.OpenAI,
                    ApiBaseUrl = "https://api.openai.com/v1",
                    ApiKey = "secret",
                    ModelName = "gpt-4o-mini",
                    Priority = 5,
                    Enabled = true
                }
            ]
        };

        var endpoints = LlmEndpointResolver.BuildEffectiveEndpoints(settings, runtimeLocalEndpoint: null);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("https://api.openai.com/v1", endpoint.ApiBaseUrl);
        Assert.Equal("gpt-4o-mini", endpoint.ModelName);
    }
}
