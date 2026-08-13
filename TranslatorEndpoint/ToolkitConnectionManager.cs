using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace LLMTranslate
{
    internal sealed class ToolkitConnectionManager
    {
        private const string DefaultBaseUrl = "http://127.0.0.1:51821";
        private const int ConnectedIntervalMilliseconds = 10000;
        private const int DisconnectedIntervalMilliseconds = 2000;
        private const int ProbeTimeoutMilliseconds = 600;

        private readonly object _gate = new object();
        private readonly string _configuredBaseUrl;
        private readonly string _discoveryFile;
        private readonly string _gameId;
        private readonly string _sessionId;
        private readonly string _endpointVersion;
        private readonly Action<string> _log;
        private readonly Action<string> _debugLog;
        private Timer _timer;
        private string _currentBaseUrl;
        private string _lastFailure;
        private string _lastDiscoveryDiagnostic;
        private int _connected;
        private int _probeRunning;

        public ToolkitConnectionManager(
            string configuredBaseUrl,
            string discoveryFile,
            string gameId,
            string sessionId,
            string endpointVersion,
            Action<string> log,
            Action<string> debugLog)
        {
            _configuredBaseUrl = NormalizeBaseUrl(configuredBaseUrl) ?? DefaultBaseUrl;
            _discoveryFile = discoveryFile;
            _gameId = gameId ?? string.Empty;
            _sessionId = sessionId;
            _endpointVersion = endpointVersion;
            _log = log;
            _debugLog = debugLog;
        }

        public bool IsConnected
        {
            get { return Interlocked.CompareExchange(ref _connected, 0, 0) == 1; }
        }

        public string CurrentBaseUrl
        {
            get
            {
                lock (_gate)
                    return _currentBaseUrl;
            }
        }

        public void Start()
        {
            _timer = new Timer(ProbeCallback, null, 0, Timeout.Infinite);
        }

        public void RequestImmediateProbe()
        {
            var timer = _timer;
            if (timer == null || Interlocked.CompareExchange(ref _probeRunning, 0, 0) != 0)
                return;
            try { timer.Change(0, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        public void MarkTransportFailure(string baseUrl, Exception error)
        {
            lock (_gate)
            {
                if (!string.Equals(_currentBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            SetDisconnected(ClassifyFailure(error));
            RequestImmediateProbe();
        }

        public static bool IsConnectivityFailure(Exception error)
        {
            var web = error as WebException;
            if (web == null)
                return false;

            switch (web.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.Timeout:
                    return true;
                default:
                    return false;
            }
        }

        public static string ClassifyFailure(Exception error)
        {
            var web = error as WebException;
            if (web == null)
                return "UnexpectedConnectionError";

            switch (web.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                    return "ConnectionRefusedOrUnavailable";
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.SendFailure:
                    return "ConnectionInterrupted";
                case WebExceptionStatus.Timeout:
                    return "TimeoutPossibleTunnelOrSecuritySoftware";
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "UnexpectedProxyInterference";
                case WebExceptionStatus.NameResolutionFailure:
                    return "InvalidLocalAddress";
                case WebExceptionStatus.ProtocolError:
                    return "UnexpectedHttpService";
                default:
                    return "TransportFailure:" + web.Status;
            }
        }

        private void ProbeCallback(object state)
        {
            if (Interlocked.Exchange(ref _probeRunning, 1) != 0)
                return;

            try
            {
                ProbeCandidates();
            }
            catch (Exception ex)
            {
                SetDisconnected(ClassifyFailure(ex));
            }
            finally
            {
                Interlocked.Exchange(ref _probeRunning, 0);
                var timer = _timer;
                if (timer != null)
                {
                    try
                    {
                        timer.Change(
                            IsConnected ? ConnectedIntervalMilliseconds : DisconnectedIntervalMilliseconds,
                            Timeout.Infinite);
                    }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private void ProbeCandidates()
        {
            var candidates = new List<Candidate>();
            Candidate discovered;
            if (TryReadDiscovery(out discovered))
                candidates.Add(discovered);
            AddCandidate(candidates, _configuredBaseUrl, null, false);
            AddCandidate(candidates, DefaultBaseUrl, null, false);

            string lastFailure = "ToolkitUnavailable";
            for (var i = 0; i < candidates.Count; i++)
            {
                string validatedBaseUrl;
                string failure;
                if (TryPing(candidates[i], out validatedBaseUrl, out failure))
                {
                    SetConnected(validatedBaseUrl, candidates[i].FromDiscovery);
                    return;
                }
                lastFailure = failure;
            }

            SetDisconnected(lastFailure);
        }

        private bool TryPing(Candidate candidate, out string validatedBaseUrl, out string failure)
        {
            validatedBaseUrl = null;
            failure = "ToolkitUnavailable";
            try
            {
                var pingUrl = candidate.BaseUrl + "/api/translate/ping"
                    + "?gameId=" + Uri.EscapeDataString(_gameId)
                    + "&sessionId=" + Uri.EscapeDataString(_sessionId)
                    + "&endpointVersion=" + Uri.EscapeDataString(_endpointVersion)
                    + "&discovery=" + (candidate.FromDiscovery ? "true" : "false")
                    + "&direct=true";

                using (var client = new DirectWebClient(ProbeTimeoutMilliseconds))
                {
                    var response = client.DownloadString(pingUrl);
                    var product = SimpleJson.ExtractString(response, "product");
                    var protocolVersion = SimpleJson.ExtractInt(response, "protocolVersion");
                    var instanceId = SimpleJson.ExtractString(response, "instanceId");
                    var responseBaseUrl = NormalizeBaseUrl(SimpleJson.ExtractString(response, "baseUrl"));

                    if (!string.Equals(product, "XUnityToolkit", StringComparison.Ordinal)
                        || protocolVersion != 1
                        || responseBaseUrl == null)
                    {
                        failure = "UnexpectedHttpService";
                        if (candidate.FromDiscovery)
                            ReportDiscoveryDiagnostic("InvalidOrStaleDiscoveryProduct");
                        return false;
                    }

                    if (!string.IsNullOrEmpty(candidate.ExpectedInstanceId)
                        && !string.Equals(instanceId, candidate.ExpectedInstanceId, StringComparison.Ordinal))
                    {
                        failure = "StaleDiscoveryInstance";
                        ReportDiscoveryDiagnostic(failure);
                        return false;
                    }

                    if (candidate.FromDiscovery)
                        ClearDiscoveryDiagnostic();
                    validatedBaseUrl = responseBaseUrl;
                    return true;
                }
            }
            catch (Exception ex)
            {
                failure = ClassifyFailure(ex);
                if (candidate.FromDiscovery)
                {
                    failure = "StaleOrUnavailableDiscovery:" + failure;
                    ReportDiscoveryDiagnostic(failure);
                }
                _debugLog("[连接] 探测失败 " + candidate.BaseUrl + ": " + failure);
                return false;
            }
        }

        private bool TryReadDiscovery(out Candidate candidate)
        {
            candidate = null;
            if (string.IsNullOrEmpty(_discoveryFile) || !File.Exists(_discoveryFile))
                return false;

            try
            {
                var json = File.ReadAllText(_discoveryFile);
                if (SimpleJson.ExtractInt(json, "schemaVersion") != 1
                    || !string.Equals(SimpleJson.ExtractString(json, "product"), "XUnityToolkit", StringComparison.Ordinal)
                    || SimpleJson.ExtractInt(json, "protocolVersion") != 1)
                {
                    ReportDiscoveryDiagnostic("InvalidDiscoveryFile:UnsupportedSchemaOrProtocol");
                    return false;
                }

                var baseUrl = NormalizeBaseUrl(SimpleJson.ExtractString(json, "baseUrl"));
                var instanceId = SimpleJson.ExtractString(json, "instanceId");
                if (baseUrl == null || string.IsNullOrEmpty(instanceId))
                {
                    ReportDiscoveryDiagnostic("InvalidDiscoveryFile:EndpointOrInstanceMissing");
                    return false;
                }

                candidate = new Candidate(baseUrl, instanceId, true);
                return true;
            }
            catch (Exception ex)
            {
                ReportDiscoveryDiagnostic("InvalidDiscoveryFile:" + ex.GetType().Name);
                return false;
            }
        }

        private void ReportDiscoveryDiagnostic(string diagnostic)
        {
            bool changed;
            lock (_gate)
            {
                changed = !string.Equals(_lastDiscoveryDiagnostic, diagnostic, StringComparison.Ordinal);
                _lastDiscoveryDiagnostic = diagnostic;
            }

            if (changed)
                _log("[连接] 发现文件无效或已过期: " + diagnostic);
        }

        private void ClearDiscoveryDiagnostic()
        {
            lock (_gate)
                _lastDiscoveryDiagnostic = null;
        }

        private void SetConnected(string baseUrl, bool fromDiscovery)
        {
            bool changed;
            lock (_gate)
            {
                changed = _connected == 0
                    || !string.Equals(_currentBaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
                _currentBaseUrl = baseUrl;
                _lastFailure = null;
                Interlocked.Exchange(ref _connected, 1);
            }

            if (changed)
            {
                _log("[连接] 已连接工具箱: " + baseUrl
                    + "；自动发现=" + (fromDiscovery ? "是" : "否")
                    + "；本机直连=是（系统代理已禁用）");
            }
        }

        private void SetDisconnected(string failure)
        {
            bool changed;
            lock (_gate)
            {
                changed = _connected != 0 || !string.Equals(_lastFailure, failure, StringComparison.Ordinal);
                _currentBaseUrl = null;
                _lastFailure = failure;
                Interlocked.Exchange(ref _connected, 0);
            }

            if (changed)
                _log("[连接] 工具箱暂不可用，后台等待恢复: " + failure);
        }

        private static void AddCandidate(List<Candidate> candidates, string baseUrl, string instanceId, bool fromDiscovery)
        {
            var normalized = NormalizeBaseUrl(baseUrl);
            if (normalized == null)
                return;
            for (var i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].BaseUrl, normalized, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            candidates.Add(new Candidate(normalized, instanceId, fromDiscovery));
        }

        internal static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri)
                || uri.Scheme != Uri.UriSchemeHttp
                || uri.Host != "127.0.0.1"
                || uri.Port <= 0
                || uri.Port > 65535
                || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length != 0))
            {
                return null;
            }

            return "http://127.0.0.1:" + uri.Port;
        }

        private sealed class Candidate
        {
            public Candidate(string baseUrl, string expectedInstanceId, bool fromDiscovery)
            {
                BaseUrl = baseUrl;
                ExpectedInstanceId = expectedInstanceId;
                FromDiscovery = fromDiscovery;
            }

            public string BaseUrl { get; private set; }
            public string ExpectedInstanceId { get; private set; }
            public bool FromDiscovery { get; private set; }
        }
    }

    internal sealed class DirectWebClient : WebClient
    {
        private readonly int _timeoutMilliseconds;

        public DirectWebClient(int timeoutMilliseconds)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
            Proxy = null;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Proxy = null;
            request.Timeout = _timeoutMilliseconds;
            var http = request as HttpWebRequest;
            if (http != null)
                http.ReadWriteTimeout = _timeoutMilliseconds;
            return request;
        }
    }

    internal sealed class DirectXUnityWebClient : XUnityWebClient
    {
        private readonly int _timeoutMilliseconds;

        public DirectXUnityWebClient(int timeoutMilliseconds)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
            Proxy = null;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Proxy = null;
            request.Timeout = _timeoutMilliseconds;
            var http = request as HttpWebRequest;
            if (http != null)
                http.ReadWriteTimeout = _timeoutMilliseconds;
            return request;
        }
    }

    internal static class SimpleJson
    {
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            var marker = "\"" + key + "\"";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                return null;
            index = json.IndexOf(':', index + marker.Length);
            if (index < 0)
                return null;
            index++;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            if (index >= json.Length || json[index] != '"')
                return null;
            index++;

            var result = new System.Text.StringBuilder();
            while (index < json.Length)
            {
                var c = json[index++];
                if (c == '"')
                    return result.ToString();
                if (c != '\\' || index >= json.Length)
                {
                    result.Append(c);
                    continue;
                }

                var escaped = json[index++];
                switch (escaped)
                {
                    case '"': result.Append('"'); break;
                    case '\\': result.Append('\\'); break;
                    case '/': result.Append('/'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= json.Length)
                        {
                            int code;
                            if (int.TryParse(json.Substring(index, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out code))
                            {
                                result.Append((char)code);
                            }
                            index += 4;
                        }
                        break;
                    default: result.Append(escaped); break;
                }
            }
            return null;
        }

        public static int ExtractInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
                return -1;
            var marker = "\"" + key + "\"";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                return -1;
            index = json.IndexOf(':', index + marker.Length);
            if (index < 0)
                return -1;
            index++;
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
            var start = index;
            while (index < json.Length && char.IsDigit(json[index])) index++;
            int value;
            return index > start && int.TryParse(json.Substring(start, index - start), out value)
                ? value
                : -1;
        }
    }
}
