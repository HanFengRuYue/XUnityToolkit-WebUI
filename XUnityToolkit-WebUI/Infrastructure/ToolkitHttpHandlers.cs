namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Keeps proxy policy scoped to the protocol client that needs it. Cloud clients deliberately
/// retain normal system-proxy behavior.
/// </summary>
public static class ToolkitHttpHandlers
{
    public static SocketsHttpHandler CreateLoopbackHandler() => new()
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(2),
    };

    public static SocketsHttpHandler CreateCloudLlmHandler() => new()
    {
        UseProxy = true,
        MaxConnectionsPerServer = 200,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}
