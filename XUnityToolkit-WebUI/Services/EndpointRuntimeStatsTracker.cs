using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

internal sealed class EndpointRuntimeStatsTracker
{
    private sealed record EndpointMetadata(
        string Id,
        string Name,
        LlmProvider Provider,
        string ModelName,
        int Priority);

    private EndpointMetadata _metadata;
    private long _inFlight;
    private long _successfulCalls;
    private long _errorCount;
    private long _totalResponseTimeMs;
    private long _lastUsedTicks;

    public EndpointRuntimeStatsTracker(ApiEndpointConfig endpoint)
    {
        _metadata = CreateMetadata(endpoint);
    }

    public long SuccessfulCalls => Interlocked.Read(ref _successfulCalls);
    public long ErrorCount => Interlocked.Read(ref _errorCount);
    public long TotalCalls => SuccessfulCalls + ErrorCount;
    public double AverageResponseTimeMs
    {
        get
        {
            var successfulCalls = SuccessfulCalls;
            return successfulCalls > 0
                ? (double)Interlocked.Read(ref _totalResponseTimeMs) / successfulCalls
                : 0;
        }
    }

    public void BeginRequest(ApiEndpointConfig endpoint)
    {
        Volatile.Write(ref _metadata, CreateMetadata(endpoint));
        Interlocked.Increment(ref _inFlight);
        Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
    }

    public void EndRequest()
    {
        while (true)
        {
            var current = Interlocked.Read(ref _inFlight);
            if (current <= 0)
                return;
            if (Interlocked.CompareExchange(ref _inFlight, current - 1, current) == current)
                return;
        }
    }

    public void RecordSuccess(double responseTimeMs)
    {
        Interlocked.Increment(ref _successfulCalls);
        Interlocked.Add(ref _totalResponseTimeMs, (long)Math.Max(0, responseTimeMs));
    }

    public void RecordError() => Interlocked.Increment(ref _errorCount);

    public EndpointRuntimeStats GetSnapshot()
    {
        var metadata = Volatile.Read(ref _metadata);
        var lastUsedTicks = Interlocked.Read(ref _lastUsedTicks);
        return new EndpointRuntimeStats(
            EndpointId: metadata.Id,
            EndpointName: metadata.Name,
            Provider: metadata.Provider,
            ModelName: metadata.ModelName,
            Priority: metadata.Priority,
            InFlight: (int)Math.Max(0, Interlocked.Read(ref _inFlight)),
            SuccessfulCalls: SuccessfulCalls,
            ErrorCount: ErrorCount,
            AverageResponseTimeMs: Math.Round(AverageResponseTimeMs, 1),
            LastUsedAt: lastUsedTicks > 0 ? new DateTime(lastUsedTicks, DateTimeKind.Utc) : null);
    }

    private static EndpointMetadata CreateMetadata(ApiEndpointConfig endpoint) => new(
        endpoint.Id,
        string.IsNullOrWhiteSpace(endpoint.Name) ? endpoint.Provider.ToString() : endpoint.Name.Trim(),
        endpoint.Provider,
        endpoint.ModelName?.Trim() ?? string.Empty,
        endpoint.Priority);
}
