using System.Collections.Concurrent;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Coalesces retries from the same game-plugin request so a lost local HTTP response cannot
/// trigger a second LLM call. Requests without protocol IDs retain the legacy cancellation path.
/// </summary>
public sealed class TranslationRequestCoordinator(IHostApplicationLifetime lifetime)
{
    private const int MaxEntries = 1000;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(130);
    private readonly ConcurrentDictionary<string, Entry> _entries = [];

    public async Task<T> ExecuteAsync<T>(
        string? clientSessionId,
        string? requestId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellation)
    {
        var key = BuildKey(clientSessionId, requestId);
        if (key is null)
            return await operation(requestCancellation);

        CleanupExpired();
        EnsureCapacityFor(key);

        var entry = _entries.GetOrAdd(key, _ => new Entry(
            DateTime.UtcNow,
            new Lazy<Task<object>>(
                async () => (object)(await ExecuteIndependentAsync(operation))!,
                LazyThreadSafetyMode.ExecutionAndPublication)));

        var result = await entry.Task.Value.WaitAsync(requestCancellation);
        return (T)result;
    }

    private async Task<T> ExecuteIndependentAsync<T>(Func<CancellationToken, Task<T>> operation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.ApplicationStopping);
        timeout.CancelAfter(ExecutionTimeout);
        return await operation(timeout.Token);
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var pair in _entries)
        {
            if (pair.Value.CreatedAtUtc < cutoff)
                _entries.TryRemove(pair.Key, out _);
        }
    }

    private void EnsureCapacityFor(string key)
    {
        while (_entries.Count >= MaxEntries && !_entries.ContainsKey(key))
        {
            var oldest = _entries.MinBy(pair => pair.Value.CreatedAtUtc);
            if (oldest.Key is null || !_entries.TryRemove(oldest.Key, out _))
                break;
        }
    }

    private static string? BuildKey(string? sessionId, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(requestId))
            return null;

        sessionId = sessionId.Trim();
        requestId = requestId.Trim();
        if (sessionId.Length > 128 || requestId.Length > 128)
            return null;
        return $"{sessionId}:{requestId}";
    }

    private sealed record Entry(DateTime CreatedAtUtc, Lazy<Task<object>> Task);
}
