using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class EndpointRuntimeStatsTrackerTests
{
    [Fact]
    public void Snapshot_TracksConcurrentRequestsAndRuntimeMetadata()
    {
        var endpoint = CreateEndpoint();
        var tracker = new EndpointRuntimeStatsTracker(endpoint);

        tracker.BeginRequest(endpoint);
        tracker.BeginRequest(endpoint);

        var active = tracker.GetSnapshot();
        Assert.Equal("OpenAI 主线路", active.EndpointName);
        Assert.Equal(LlmProvider.OpenAI, active.Provider);
        Assert.Equal("gpt-5.6-luna", active.ModelName);
        Assert.Equal(8, active.Priority);
        Assert.Equal(2, active.InFlight);
        Assert.NotNull(active.LastUsedAt);

        tracker.EndRequest();
        tracker.EndRequest();
        tracker.EndRequest();

        Assert.Equal(0, tracker.GetSnapshot().InFlight);
    }

    [Fact]
    public void Snapshot_SeparatesSuccessfulAndFailedCallsAndAveragesSuccessfulLatency()
    {
        var tracker = new EndpointRuntimeStatsTracker(CreateEndpoint());

        tracker.RecordSuccess(100.4);
        tracker.RecordSuccess(201.6);
        tracker.RecordError();

        var snapshot = tracker.GetSnapshot();
        Assert.Equal(2, snapshot.SuccessfulCalls);
        Assert.Equal(1, snapshot.ErrorCount);
        Assert.Equal(150.5, snapshot.AverageResponseTimeMs);
        Assert.Equal(3, tracker.TotalCalls);
    }

    [Fact]
    public void BeginRequest_RefreshesRenamedEndpointMetadata()
    {
        var endpoint = CreateEndpoint();
        var tracker = new EndpointRuntimeStatsTracker(endpoint);
        endpoint.Name = "OpenAI 备用线路";
        endpoint.ModelName = "gpt-5.6-mini";
        endpoint.Priority = 3;

        tracker.BeginRequest(endpoint);
        tracker.EndRequest();

        var snapshot = tracker.GetSnapshot();
        Assert.Equal("OpenAI 备用线路", snapshot.EndpointName);
        Assert.Equal("gpt-5.6-mini", snapshot.ModelName);
        Assert.Equal(3, snapshot.Priority);
    }

    private static ApiEndpointConfig CreateEndpoint() => new()
    {
        Id = "openai-main",
        Name = "OpenAI 主线路",
        Provider = LlmProvider.OpenAI,
        ModelName = "gpt-5.6-luna",
        Priority = 8,
    };
}
