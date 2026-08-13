using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace LLMTranslate
{
    public sealed class LLMTranslateEndpoint : ITranslateEndpoint
    {
        private const string ConfigSection = "LLMTranslate";
        private const string ConfigUrlKey = "ToolkitUrl";
        private const string ConfigDiscoveryFileKey = "DiscoveryFile";
        private const string ConfigMaxConcurrencyKey = "MaxConcurrency";
        private const string ConfigDebugModeKey = "DebugMode";
        private const string ConfigGameIdKey = "GameId";
        private const string ConfigMaxTranslationsPerRequestKey = "MaxTranslationsPerRequest";
        private const string ConfigDisableSpamChecksKey = "DisableSpamChecks";
        private const string ConfigTranslationDelayKey = "TranslationDelay";
        private const string DefaultUrl = "http://127.0.0.1:51821";
        private const string EndpointVersion = "2.0.0";
        private const int RecoveryBudgetMilliseconds = 140000;
        private const int MinimumRequestBudgetMilliseconds = 30000;
        private const int MaximumRequestTimeoutMilliseconds = 120000;

        private readonly string _clientSessionId = Guid.NewGuid().ToString("N");
        private ToolkitConnectionManager _connection;
        private int _configuredMaxConcurrency = 10;
        private int _maxTranslationsPerRequest = 10;
        private bool _debugMode;
        private string _gameId = string.Empty;

        public string Id
        {
            get { return "LLMTranslate"; }
        }

        public string FriendlyName
        {
            get { return "AI Translation (LLM via XUnity Toolkit)"; }
        }

        public int MaxConcurrency
        {
            get { return _connection != null && _connection.IsConnected ? _configuredMaxConcurrency : 1; }
        }

        public int MaxTranslationsPerRequest
        {
            get { return _maxTranslationsPerRequest; }
        }

        public void Initialize(IInitializationContext context)
        {
            var configuredBaseUrl = context.GetOrCreateSetting<string>(ConfigSection, ConfigUrlKey, DefaultUrl);
            var defaultDiscoveryFile = System.IO.Path.Combine(
                System.IO.Path.Combine(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "XUnityToolkit"),
                    "runtime"),
                "toolbox-endpoint-v1.json");
            var discoveryFile = context.GetOrCreateSetting<string>(
                ConfigSection,
                ConfigDiscoveryFileKey,
                defaultDiscoveryFile);

            _configuredMaxConcurrency = context.GetOrCreateSetting<int>(ConfigSection, ConfigMaxConcurrencyKey, 10);
            if (_configuredMaxConcurrency < 1) _configuredMaxConcurrency = 1;
            if (_configuredMaxConcurrency > 20) _configuredMaxConcurrency = 20;

            _maxTranslationsPerRequest = context.GetOrCreateSetting<int>(ConfigSection, ConfigMaxTranslationsPerRequestKey, 10);
            if (_maxTranslationsPerRequest < 1) _maxTranslationsPerRequest = 1;
            if (_maxTranslationsPerRequest > 50) _maxTranslationsPerRequest = 50;

            _debugMode = context.GetOrCreateSetting<bool>(ConfigSection, ConfigDebugModeKey, false);
            _gameId = context.GetOrCreateSetting<string>(ConfigSection, ConfigGameIdKey, string.Empty);

            ServicePointManager.DefaultConnectionLimit = Math.Max(
                ServicePointManager.DefaultConnectionLimit,
                _configuredMaxConcurrency + 10);
            ServicePointManager.Expect100Continue = false;

            var disableSpamChecks = context.GetOrCreateSetting<bool>(ConfigSection, ConfigDisableSpamChecksKey, true);
            if (disableSpamChecks)
                context.DisableSpamChecks();

            var translationDelay = context.GetOrCreateSetting<float>(ConfigSection, ConfigTranslationDelayKey, 0.1f);
            if (translationDelay >= 0.1f)
                context.SetTranslationDelay(translationDelay);

            _connection = new ToolkitConnectionManager(
                configuredBaseUrl,
                discoveryFile,
                _gameId,
                _clientSessionId,
                EndpointVersion,
                Log,
                DebugLog);
            _connection.Start();

            Log("=== LLMTranslate 插件初始化 ===");
            Log("  连接模式: 本机直连（系统代理已禁用）");
            Log("  地址顺序: 发现文件 → ToolkitUrl → " + DefaultUrl);
            Log("  发现文件: " + discoveryFile);
            Log("  配置地址: " + configuredBaseUrl);
            Log("  正常并发连接数: " + _configuredMaxConcurrency + "；断线时: 1");
            Log("  每请求文本数: " + _maxTranslationsPerRequest);
            Log("  游戏 ID: " + (string.IsNullOrEmpty(_gameId) ? "(未设置)" : _gameId));
            Log("  端点版本: " + EndpointVersion);
            Log("  禁用防刷检查: " + (disableSpamChecks ? "是" : "否"));
            Log("  翻译延迟: " + translationDelay + " 秒");
        }

        public IEnumerator Translate(ITranslationContext context)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();
            var attempt = 0;
            var immediateProbeRequested = false;

            while (stopwatch.ElapsedMilliseconds < RecoveryBudgetMilliseconds)
            {
                var baseUrl = _connection.CurrentBaseUrl;
                if (string.IsNullOrEmpty(baseUrl))
                {
                    if (!immediateProbeRequested)
                    {
                        _connection.RequestImmediateProbe();
                        immediateProbeRequested = true;
                    }
                    if (RecoveryBudgetMilliseconds - stopwatch.ElapsedMilliseconds < MinimumRequestBudgetMilliseconds)
                    {
                        context.Fail("Toolkit is unavailable and there is not enough time left to send a translation request.");
                        yield break;
                    }

                    yield return null;
                    continue;
                }

                immediateProbeRequested = false;

                var remaining = RecoveryBudgetMilliseconds - (int)stopwatch.ElapsedMilliseconds;
                if (remaining < MinimumRequestBudgetMilliseconds)
                {
                    context.Fail("Toolkit connection recovered too late to safely send the translation request.");
                    yield break;
                }

                attempt++;
                var timeout = Math.Min(MaximumRequestTimeoutMilliseconds, remaining - 5000);
                var body = BuildRequestBody(
                    context.UntranslatedTexts,
                    context.SourceLanguage,
                    context.DestinationLanguage,
                    _gameId,
                    _clientSessionId,
                    requestId);
                var request = new XUnityWebRequest("POST", baseUrl + "/api/translate", body);
                request.Headers[HttpRequestHeader.ContentType] = "application/json";
                request.Headers[HttpRequestHeader.Accept] = "application/json";

                Log(string.Format(
                    "[请求] 第 {0} 次发送 {1} 条文本到 {2}: {3} → {4}",
                    attempt,
                    context.UntranslatedTexts.Length,
                    baseUrl,
                    context.SourceLanguage,
                    context.DestinationLanguage));
                LogRequestPreview(context.UntranslatedTexts);

                var client = new DirectXUnityWebClient(timeout);
                var response = client.Send(request);
                yield return response;

                if (response.Error != null)
                {
                    if (ToolkitConnectionManager.IsConnectivityFailure(response.Error))
                    {
                        var classification = ToolkitConnectionManager.ClassifyFailure(response.Error);
                        Log("[连接] 翻译请求中断: " + classification);
                        _connection.MarkTransportFailure(baseUrl, response.Error);

                        if (RecoveryBudgetMilliseconds - stopwatch.ElapsedMilliseconds >= MinimumRequestBudgetMilliseconds)
                            continue;
                    }

                    context.Fail("Toolkit request failed.", response.Error);
                    yield break;
                }

                var statusCode = (int)response.Code;
                if (statusCode < 200 || statusCode >= 300)
                {
                    context.Fail("Toolkit returned HTTP status " + statusCode + ".");
                    yield break;
                }

                var raw = response.Data;
                if (string.IsNullOrEmpty(raw))
                {
                    context.Fail("Toolkit returned an empty response.");
                    yield break;
                }

                DebugLog("[响应] " + (raw.Length > 200 ? raw.Substring(0, 200) + "..." : raw));
                var translations = ParseTranslationsArray(raw);
                if (translations == null || translations.Length != context.UntranslatedTexts.Length)
                {
                    context.Fail(string.Format(
                        "Translation count mismatch. Expected {0}, received {1}.",
                        context.UntranslatedTexts.Length,
                        translations == null ? "null" : translations.Length.ToString()));
                    yield break;
                }

                Log("[完成] 成功翻译 " + translations.Length + " 条文本");
                context.Complete(translations);
                yield break;
            }

            context.Fail("Toolkit recovery budget was exhausted.");
        }

        private void LogRequestPreview(string[] texts)
        {
            if (!_debugMode || texts == null)
                return;

            for (var i = 0; i < texts.Length && i < 3; i++)
            {
                var text = texts[i] ?? string.Empty;
                if (text.Length > 50) text = text.Substring(0, 50) + "...";
                DebugLog("  [" + i + "] " + text);
            }
        }

        private void Log(string message)
        {
            Console.WriteLine("[LLMTranslate] " + message);
        }

        private void DebugLog(string message)
        {
            if (_debugMode)
                Log(message);
        }

        private static string BuildRequestBody(
            string[] texts,
            string from,
            string to,
            string gameId,
            string clientSessionId,
            string requestId)
        {
            var sb = new StringBuilder();
            sb.Append("{\"texts\":");
            SerializeStringArray(sb, texts);
            sb.Append(",\"from\":\"");
            EscapeJsonString(sb, from);
            sb.Append("\",\"to\":\"");
            EscapeJsonString(sb, to);
            sb.Append("\"");
            AppendJsonProperty(sb, "gameId", gameId);
            AppendJsonProperty(sb, "clientSessionId", clientSessionId);
            AppendJsonProperty(sb, "requestId", requestId);
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendJsonProperty(StringBuilder sb, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            sb.Append(",\"");
            EscapeJsonString(sb, name);
            sb.Append("\":\"");
            EscapeJsonString(sb, value);
            sb.Append("\"");
        }

        private static void SerializeStringArray(StringBuilder sb, string[] values)
        {
            sb.Append("[");
            for (var i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\"");
                EscapeJsonString(sb, values[i]);
                sb.Append("\"");
            }
            sb.Append("]");
        }

        private static void EscapeJsonString(StringBuilder sb, string value)
        {
            if (value == null) return;
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            sb.Append(c);
                            sb.Append(value[++i]);
                        }
                        else if (c < 0x20 || char.IsSurrogate(c))
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
        }

        private static string[] ParseTranslationsArray(string json)
        {
            var key = "\"translations\"";
            var keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                key = "\"Translations\"";
                keyIndex = json.IndexOf(key, StringComparison.Ordinal);
                if (keyIndex < 0) return null;
            }

            var bracketStart = json.IndexOf('[', keyIndex + key.Length);
            if (bracketStart < 0) return null;

            var results = new List<string>();
            var pos = bracketStart + 1;
            while (pos < json.Length)
            {
                while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
                if (pos >= json.Length) return null;
                if (json[pos] == ']') break;
                if (json[pos] == ',')
                {
                    pos++;
                    continue;
                }
                if (json[pos] != '"') return null;
                pos++;

                var value = new StringBuilder();
                var closed = false;
                while (pos < json.Length)
                {
                    var c = json[pos++];
                    if (c == '"')
                    {
                        closed = true;
                        break;
                    }
                    if (c != '\\' || pos >= json.Length)
                    {
                        value.Append(c);
                        continue;
                    }

                    var escaped = json[pos++];
                    switch (escaped)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'u':
                            if (pos + 4 > json.Length) return null;
                            int code;
                            if (!int.TryParse(
                                json.Substring(pos, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out code))
                            {
                                return null;
                            }
                            value.Append((char)code);
                            pos += 4;
                            break;
                        default: value.Append(escaped); break;
                    }
                }

                if (!closed) return null;
                results.Add(value.ToString());
            }

            return results.ToArray();
        }
    }
}
