using System.Collections.Concurrent;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public sealed class PluginConnectionRegistry(TimeProvider timeProvider)
{
    public static readonly TimeSpan ConnectedTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, PluginConnectionInfo> _connections = [];

    public void RecordHeartbeat(
        string gameId,
        string? sessionId,
        string? endpointVersion,
        bool discoveryEnabled,
        bool directConnection,
        string baseUrl)
    {
        if (!Guid.TryParse(gameId, out _))
            return;

        var normalizedSession = Limit(sessionId, 64) ?? "legacy";
        var key = $"{gameId}:{normalizedSession}";
        _connections[key] = new PluginConnectionInfo(
            gameId,
            normalizedSession,
            Limit(endpointVersion, 64),
            discoveryEnabled,
            directConnection,
            baseUrl,
            timeProvider.GetUtcNow().UtcDateTime);
        CleanupExpired();
    }

    public bool HasRecentHeartbeat(string gameId, DateTime since) =>
        _connections.Values.Any(connection =>
            connection.GameId == gameId && connection.LastSeenAtUtc >= since);

    public PluginConnectionInfo? GetLatest(string gameId) =>
        _connections.Values
            .Where(connection => connection.GameId == gameId)
            .OrderByDescending(connection => connection.LastSeenAtUtc)
            .FirstOrDefault();

    public PluginConnectionSummary GetSummary()
    {
        CleanupExpired();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - ConnectedTtl;
        var active = _connections.Values
            .Where(connection => connection.LastSeenAtUtc >= cutoff)
            .ToList();
        return new PluginConnectionSummary(
            active.Count,
            active.Select(connection => connection.GameId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToList(),
            _connections.Values.Select(connection => (DateTime?)connection.LastSeenAtUtc).Max(),
            active.Any(connection => connection.SessionId == "legacy"));
    }

    private void CleanupExpired()
    {
        var cutoff = timeProvider.GetUtcNow().UtcDateTime - Retention;
        foreach (var pair in _connections)
        {
            if (pair.Value.LastSeenAtUtc < cutoff)
                _connections.TryRemove(pair.Key, out _);
        }
    }

    private static string? Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
