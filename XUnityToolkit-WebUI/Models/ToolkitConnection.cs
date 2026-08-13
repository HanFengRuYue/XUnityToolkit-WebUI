namespace XUnityToolkit_WebUI.Models;

public sealed record ToolkitEndpointDiscoveryRecord(
    int SchemaVersion,
    string Product,
    int ProtocolVersion,
    string InstanceId,
    int ProcessId,
    string BaseUrl,
    int PreferredPort,
    int ActualPort,
    bool UsedFallback,
    string? FallbackReason,
    DateTime StartedAtUtc);

public sealed record PluginConnectionSummary(
    int ConnectedCount,
    List<string> ConnectedGameIds,
    DateTime? LastHeartbeatAt,
    bool HasLegacyConnections);

public sealed record ToolkitConnectionInfo(
    int PreferredPort,
    int ActualPort,
    string BaseUrl,
    bool UsedFallback,
    string? FallbackReason,
    bool RestartRequired,
    int DiscoveryProtocolVersion,
    bool LoopbackSelfTestSucceeded,
    string? LoopbackSelfTestError,
    PluginConnectionSummary PluginConnection);

public sealed record PluginConnectionInfo(
    string GameId,
    string SessionId,
    string? EndpointVersion,
    bool DiscoveryEnabled,
    bool DirectConnection,
    string BaseUrl,
    DateTime LastSeenAtUtc);
