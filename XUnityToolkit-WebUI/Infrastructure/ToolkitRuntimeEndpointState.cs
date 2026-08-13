using System.Net;
using System.Net.Sockets;

namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Authoritative runtime address for the local XUnity protocol. The configured port is a
/// preference; ActualPort is populated by the socket binder before Kestrel starts listening.
/// </summary>
public sealed class ToolkitRuntimeEndpointState
{
    public const int DiscoverySchemaVersion = 1;
    public const int ProtocolVersion = 1;
    public const string ProductName = "XUnityToolkit";

    private readonly object _gate = new();
    private int _actualPort;
    private bool _usedFallback;
    private string? _fallbackReason;
    private bool _started;
    private bool _loopbackSelfTestSucceeded;
    private string? _loopbackSelfTestError;

    public ToolkitRuntimeEndpointState(int preferredPort)
    {
        if (preferredPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(preferredPort));

        PreferredPort = preferredPort;
        InstanceId = Guid.NewGuid().ToString("N");
        StartedAtUtc = DateTime.UtcNow;
    }

    public int PreferredPort { get; }
    public string InstanceId { get; }
    public DateTime StartedAtUtc { get; }

    public int ActualPort
    {
        get { lock (_gate) return _actualPort; }
    }

    public bool UsedFallback
    {
        get { lock (_gate) return _usedFallback; }
    }

    public string? FallbackReason
    {
        get { lock (_gate) return _fallbackReason; }
    }

    public bool IsStarted
    {
        get { lock (_gate) return _started; }
    }

    public string BaseUrl
    {
        get
        {
            lock (_gate)
            {
                if (_actualPort <= 0)
                    throw new InvalidOperationException("Kestrel 尚未绑定本机端口。");
                return $"http://127.0.0.1:{_actualPort}";
            }
        }
    }

    public bool LoopbackSelfTestSucceeded
    {
        get { lock (_gate) return _loopbackSelfTestSucceeded; }
    }

    public string? LoopbackSelfTestError
    {
        get { lock (_gate) return _loopbackSelfTestError; }
    }

    /// <summary>
    /// Kestrel socket transport callback. Binding happens here, so preferred-port fallback does
    /// not suffer from the check-then-bind race of a separate port availability probe.
    /// </summary>
    public Socket CreateBoundListenSocket(EndPoint endpoint)
    {
        if (endpoint is not IPEndPoint ipEndpoint || !IPAddress.Loopback.Equals(ipEndpoint.Address))
            throw new InvalidOperationException("工具箱只允许绑定 IPv4 环回地址 127.0.0.1。");

        var socket = CreateSocket();
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, PreferredPort));
            RecordBoundSocket(socket, usedFallback: false, fallbackReason: null);
            return socket;
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
        {
            socket.Dispose();
            socket = CreateSocket();
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                RecordBoundSocket(socket, usedFallback: true,
                    fallbackReason: ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                        ? "PreferredPortInUse"
                        : "PreferredPortReservedOrDenied");
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void MarkStarted()
    {
        lock (_gate)
        {
            if (_actualPort <= 0)
                throw new InvalidOperationException("Kestrel 未报告实际监听端口。");
            _started = true;
        }
    }

    public void RecordLoopbackSelfTest(bool succeeded, string? error)
    {
        lock (_gate)
        {
            _loopbackSelfTestSucceeded = succeeded;
            _loopbackSelfTestError = succeeded ? null : error;
        }
    }

    private static Socket CreateSocket()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            ExclusiveAddressUse = true,
        };
        return socket;
    }

    private void RecordBoundSocket(Socket socket, bool usedFallback, string? fallbackReason)
    {
        var actual = socket.LocalEndPoint as IPEndPoint
            ?? throw new InvalidOperationException("无法读取 Kestrel 实际监听端点。");

        lock (_gate)
        {
            _actualPort = actual.Port;
            _usedFallback = usedFallback;
            _fallbackReason = fallbackReason;
        }
    }
}
