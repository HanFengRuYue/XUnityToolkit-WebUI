using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace LLMTranslate
{
    public class LLMTranslateEndpoint : HttpEndpoint
    {
        private const string ConfigSection = "LLMTranslate";
        private const string ConfigUrlKey = "ToolkitUrl";
        private const string ConfigMaxConcurrencyKey = "MaxConcurrency";
        private const string ConfigDebugModeKey = "DebugMode";
        private const string ConfigGameIdKey = "GameId";
        private const string ConfigMaxTranslationsPerRequestKey = "MaxTranslationsPerRequest";
        private const string ConfigDisableSpamChecksKey = "DisableSpamChecks";
        private const string ConfigTranslationDelayKey = "TranslationDelay";
        private const string DefaultUrl = "http://127.0.0.1:51821";

        private string _translateUrl = DefaultUrl + "/api/translate";
        private int _maxConcurrency = 10;
        private int _maxTranslationsPerRequest = 10;
        private bool _debugMode = false;
        private string _gameId = "";

        public override string Id
        {
            get { return "LLMTranslate"; }
        }

        public override string FriendlyName
        {
            get { return "AI Translation (LLM via XUnity Toolkit)"; }
        }

        public override int MaxConcurrency
        {
            get { return _maxConcurrency; }
        }

        public override int MaxTranslationsPerRequest
        {
            get { return _maxTranslationsPerRequest; }
        }

        public override void Initialize(IInitializationContext context)
        {
            var baseUrl = context.GetOrCreateSetting<string>(ConfigSection, ConfigUrlKey, DefaultUrl);
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = DefaultUrl;

            _maxConcurrency = context.GetOrCreateSetting<int>(ConfigSection, ConfigMaxConcurrencyKey, 10);
            if (_maxConcurrency < 1) _maxConcurrency = 1;
            if (_maxConcurrency > 20) _maxConcurrency = 20;

            _maxTranslationsPerRequest = context.GetOrCreateSetting<int>(ConfigSection, ConfigMaxTranslationsPerRequestKey, 10);
            if (_maxTranslationsPerRequest < 1) _maxTranslationsPerRequest = 1;
            if (_maxTranslationsPerRequest > 50) _maxTranslationsPerRequest = 50;

            _debugMode = context.GetOrCreateSetting<bool>(ConfigSection, ConfigDebugModeKey, false);
            _gameId = context.GetOrCreateSetting<string>(ConfigSection, ConfigGameIdKey, "");

            _translateUrl = baseUrl.TrimEnd(new char[] { '/' }) + "/api/translate";

            // XUnity 的 HttpEndpoint 通过 ConnectionTrackingWebClient → WebClient → HttpWebRequest 发送请求。
            // Mono 的 ServicePointManager.DefaultConnectionLimit 默认为 2，严重限制并发连接数。
            // 重要：DefaultConnectionLimit 只影响之后新创建的 ServicePoint，
            // 必须用 FindServicePoint 直接设置目标主机的 ConnectionLimit。
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, _maxConcurrency + 10);
            ServicePointManager.Expect100Continue = false;

            try
            {
                var targetUri = new Uri(_translateUrl);
                var servicePoint = ServicePointManager.FindServicePoint(targetUri);
                servicePoint.ConnectionLimit = _maxConcurrency + 10;
                servicePoint.MaxIdleTime = 120000; // 120 秒空闲后关闭连接
                DebugLog("  ServicePoint 连接限制: " + servicePoint.ConnectionLimit);
            }
            catch (Exception ex)
            {
                DebugLog("  设置 ServicePoint 失败: " + ex.Message);
            }

            var disableSpamChecks = context.GetOrCreateSetting<bool>(ConfigSection, ConfigDisableSpamChecksKey, true);
            if (disableSpamChecks)
                context.DisableSpamChecks();

            var translationDelay = context.GetOrCreateSetting<float>(ConfigSection, ConfigTranslationDelayKey, 0.1f);
            if (translationDelay >= 0.1f)
                context.SetTranslationDelay(translationDelay);

            DebugLog("=== LLMTranslate 插件初始化 ===");
            DebugLog("  工具箱地址: " + _translateUrl);
            DebugLog("  并发连接数: " + _maxConcurrency);
            DebugLog("  每请求文本数: " + _maxTranslationsPerRequest);
            DebugLog("  最大同时翻译: " + (_maxConcurrency * _maxTranslationsPerRequest));
            DebugLog("  全局连接池默认: " + ServicePointManager.DefaultConnectionLimit);
            DebugLog("  游戏 ID: " + (string.IsNullOrEmpty(_gameId) ? "(未设置)" : _gameId));
            DebugLog("  禁用防刷检查: " + (disableSpamChecks ? "是" : "否"));
            DebugLog("  翻译延迟: " + translationDelay + " 秒");
            DebugLog("  调试模式: " + (_debugMode ? "开启" : "关闭"));
        }

        public override void OnCreateRequest(IHttpRequestCreationContext context)
        {
            var body = BuildRequestBody(
                context.UntranslatedTexts,
                context.SourceLanguage,
                context.DestinationLanguage,
                _gameId);

            DebugLog(string.Format("[请求] 发送 {0} 条文本到工具箱: {1} → {2}",
                context.UntranslatedTexts.Length,
                context.SourceLanguage,
                context.DestinationLanguage));

            if (_debugMode && context.UntranslatedTexts.Length > 0)
            {
                for (int i = 0; i < context.UntranslatedTexts.Length && i < 3; i++)
                {
                    var text = context.UntranslatedTexts[i];
                    if (text.Length > 50) text = text.Substring(0, 50) + "...";
                    DebugLog("  [" + i + "] " + text);
                }
                if (context.UntranslatedTexts.Length > 3)
                    DebugLog("  ... 还有 " + (context.UntranslatedTexts.Length - 3) + " 条");
            }

            var request = new XUnityWebRequest("POST", _translateUrl, body);
            request.Headers[HttpRequestHeader.ContentType] = "application/json";
            request.Headers[HttpRequestHeader.Accept] = "application/json";
            // 不设置 Connection: close — keep-alive 连接复用更高效。
            // Mono 的 close 实现有 CLOSE_WAIT 泄漏问题，会永久占用连接槽。

            context.Complete(request);
        }

        public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
        {
            var raw = context.Response.Data;
            if (string.IsNullOrEmpty(raw))
            {
                DebugLog("[错误] 工具箱返回空响应");
                context.Fail("Empty response from toolkit backend.");
                return;
            }

            DebugLog("[响应] 收到工具箱响应: " + (raw.Length > 200 ? raw.Substring(0, 200) + "..." : raw));

            var translations = ParseTranslationsArray(raw);

            if (translations == null || translations.Length != context.UntranslatedTexts.Length)
            {
                DebugLog(string.Format("[错误] 翻译数量不匹配: 期望 {0}, 实际 {1}",
                    context.UntranslatedTexts.Length,
                    translations != null ? translations.Length.ToString() : "null"));
                context.Fail("Translation count mismatch.");
                return;
            }

            DebugLog(string.Format("[完成] 成功翻译 {0} 条文本", translations.Length));
            if (_debugMode && translations.Length > 0)
            {
                for (int i = 0; i < translations.Length && i < 3; i++)
                {
                    var text = translations[i];
                    if (text.Length > 50) text = text.Substring(0, 50) + "...";
                    DebugLog("  [" + i + "] " + text);
                }
            }

            context.Complete(translations);
        }

        private void DebugLog(string message)
        {
            if (!_debugMode) return;
            Console.WriteLine("[LLMTranslate] " + message);
        }

        // --- JSON helpers (no external JSON library available in .NET 3.5) ---

        private static string BuildRequestBody(string[] texts, string from, string to, string gameId)
        {
            var sb = new StringBuilder();
            sb.Append("{\"texts\":");
            SerializeStringArray(sb, texts);
            sb.Append(",\"from\":\"");
            EscapeJsonString(sb, from);
            sb.Append("\",\"to\":\"");
            EscapeJsonString(sb, to);
            sb.Append("\"");
            if (!string.IsNullOrEmpty(gameId))
            {
                sb.Append(",\"gameId\":\"");
                EscapeJsonString(sb, gameId);
                sb.Append("\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static void SerializeStringArray(StringBuilder sb, string[] values)
        {
            sb.Append("[");
            for (int i = 0; i < values.Length; i++)
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
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
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
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
        }

        /// <summary>
        /// Parse the response JSON: {"translations":["t1","t2",...]}
        /// Purpose-built parser for this specific shape.
        /// </summary>
        private static string[] ParseTranslationsArray(string json)
        {
            // Find the "translations" key (case-insensitive search not needed - our backend controls the key)
            var key = "\"translations\"";
            int keyIndex = json.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                // Try PascalCase as fallback
                key = "\"Translations\"";
                keyIndex = json.IndexOf(key, StringComparison.Ordinal);
                if (keyIndex < 0) return null;
            }

            // Find the opening bracket
            int bracketStart = json.IndexOf('[', keyIndex + key.Length);
            if (bracketStart < 0) return null;

            // Parse array of strings
            var results = new List<string>();
            int pos = bracketStart + 1;

            while (pos < json.Length)
            {
                // Skip whitespace
                while (pos < json.Length && (json[pos] == ' ' || json[pos] == '\t' || json[pos] == '\n' || json[pos] == '\r'))
                    pos++;

                if (pos >= json.Length) return null;

                // End of array
                if (json[pos] == ']') break;

                // Skip comma
                if (json[pos] == ',')
                {
                    pos++;
                    continue;
                }

                // Expect opening quote
                if (json[pos] != '"') return null;
                pos++;

                // Read string value with escape handling
                var valueSb = new StringBuilder();
                while (pos < json.Length)
                {
                    char c = json[pos];
                    if (c == '\\' && pos + 1 < json.Length)
                    {
                        char next = json[pos + 1];
                        switch (next)
                        {
                            case '"': valueSb.Append('"'); pos += 2; break;
                            case '\\': valueSb.Append('\\'); pos += 2; break;
                            case 'n': valueSb.Append('\n'); pos += 2; break;
                            case 'r': valueSb.Append('\r'); pos += 2; break;
                            case 't': valueSb.Append('\t'); pos += 2; break;
                            case 'b': valueSb.Append('\b'); pos += 2; break;
                            case 'f': valueSb.Append('\f'); pos += 2; break;
                            case 'u':
                                if (pos + 6 <= json.Length)
                                {
                                    var hex = json.Substring(pos + 2, 4);
                                    int code;
                                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                                        valueSb.Append((char)code);
                                    pos += 6;
                                }
                                else
                                {
                                    pos += 2;
                                }
                                break;
                            default: valueSb.Append(next); pos += 2; break;
                        }
                    }
                    else if (c == '"')
                    {
                        pos++; // skip closing quote
                        break;
                    }
                    else
                    {
                        valueSb.Append(c);
                        pos++;
                    }
                }

                results.Add(valueSb.ToString());
            }

            return results.ToArray();
        }
    }
}
