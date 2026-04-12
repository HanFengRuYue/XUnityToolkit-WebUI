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
            get { return "AI Translation (LLM via UnityLocalizationToolkit)"; }
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

            // XUnity 鐨?HttpEndpoint 閫氳繃 ConnectionTrackingWebClient 鈫?WebClient 鈫?HttpWebRequest 鍙戦€佽姹傘€?
            // Mono 鐨?ServicePointManager.DefaultConnectionLimit 榛樿涓?2锛屼弗閲嶉檺鍒跺苟鍙戣繛鎺ユ暟銆?
            // 閲嶈锛欴efaultConnectionLimit 鍙奖鍝嶄箣鍚庢柊鍒涘缓鐨?ServicePoint锛?
            // 蹇呴』鐢?FindServicePoint 鐩存帴璁剧疆鐩爣涓绘満鐨?ConnectionLimit銆?
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, _maxConcurrency + 10);
            ServicePointManager.Expect100Continue = false;

            try
            {
                var targetUri = new Uri(_translateUrl);
                var servicePoint = ServicePointManager.FindServicePoint(targetUri);
                servicePoint.ConnectionLimit = _maxConcurrency + 10;
                servicePoint.MaxIdleTime = 120000; // 120 绉掔┖闂插悗鍏抽棴杩炴帴
                DebugLog("  ServicePoint 杩炴帴闄愬埗: " + servicePoint.ConnectionLimit);
            }
            catch (Exception ex)
            {
                DebugLog("  璁剧疆 ServicePoint 澶辫触: " + ex.Message);
            }

            var disableSpamChecks = context.GetOrCreateSetting<bool>(ConfigSection, ConfigDisableSpamChecksKey, true);
            if (disableSpamChecks)
                context.DisableSpamChecks();

            var translationDelay = context.GetOrCreateSetting<float>(ConfigSection, ConfigTranslationDelayKey, 0.1f);
            if (translationDelay >= 0.1f)
                context.SetTranslationDelay(translationDelay);

            Log("=== LLMTranslate 鎻掍欢鍒濆鍖?===");
            Log("  宸ュ叿绠卞湴鍧€: " + _translateUrl);
            Log("  骞跺彂杩炴帴鏁? " + _maxConcurrency);
            Log("  姣忚姹傛枃鏈暟: " + _maxTranslationsPerRequest);
            Log("  鏈€澶у悓鏃剁炕璇? " + (_maxConcurrency * _maxTranslationsPerRequest));
            DebugLog("  鍏ㄥ眬杩炴帴姹犻粯璁? " + ServicePointManager.DefaultConnectionLimit);
            Log("  娓告垙 ID: " + (string.IsNullOrEmpty(_gameId) ? "(鏈缃?" : _gameId));
            Log("  绂佺敤闃插埛妫€鏌? " + (disableSpamChecks ? "鏄? : "鍚?));
            Log("  缈昏瘧寤惰繜: " + translationDelay + " 绉?);
            Log("  璋冭瘯妯″紡: " + (_debugMode ? "寮€鍚? : "鍏抽棴"));

            // Connectivity ping 鈥?notify toolbox that the plugin has loaded
            try
            {
                var pingUrl = baseUrl.TrimEnd(new char[] { '/' }) + "/api/translate/ping";
                if (!string.IsNullOrEmpty(_gameId))
                    pingUrl += "?gameId=" + Uri.EscapeDataString(_gameId);
                var pingClient = new WebClient();
                pingClient.DownloadStringCompleted += (s, e) => ((WebClient)s).Dispose();
                try { pingClient.DownloadStringAsync(new Uri(pingUrl)); }
                catch { pingClient.Dispose(); throw; }
                Log("  杩為€氭€ф祴璇曞凡鍙戦€? " + pingUrl);
            }
            catch (Exception ex)
            {
                Log("  杩為€氭€ф祴璇曞彂閫佸け璐? " + ex.Message);
            }
        }

        public override void OnCreateRequest(IHttpRequestCreationContext context)
        {
            var body = BuildRequestBody(
                context.UntranslatedTexts,
                context.SourceLanguage,
                context.DestinationLanguage,
                _gameId);

            Log(string.Format("[璇锋眰] 鍙戦€?{0} 鏉℃枃鏈埌宸ュ叿绠? {1} 鈫?{2}",
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
                    DebugLog("  ... 杩樻湁 " + (context.UntranslatedTexts.Length - 3) + " 鏉?);
            }

            var request = new XUnityWebRequest("POST", _translateUrl, body);
            request.Headers[HttpRequestHeader.ContentType] = "application/json";
            request.Headers[HttpRequestHeader.Accept] = "application/json";
            // 涓嶈缃?Connection: close 鈥?keep-alive 杩炴帴澶嶇敤鏇撮珮鏁堛€?
            // Mono 鐨?close 瀹炵幇鏈?CLOSE_WAIT 娉勬紡闂锛屼細姘镐箙鍗犵敤杩炴帴妲姐€?

            context.Complete(request);
        }

        public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
        {
            var raw = context.Response.Data;
            if (string.IsNullOrEmpty(raw))
            {
                Log("[閿欒] 宸ュ叿绠辫繑鍥炵┖鍝嶅簲");
                context.Fail("Empty response from toolkit backend.");
                return;
            }

            DebugLog("[鍝嶅簲] 鏀跺埌宸ュ叿绠卞搷搴? " + (raw.Length > 200 ? raw.Substring(0, 200) + "..." : raw));

            var translations = ParseTranslationsArray(raw);

            if (translations == null || translations.Length != context.UntranslatedTexts.Length)
            {
                Log(string.Format("[閿欒] 缈昏瘧鏁伴噺涓嶅尮閰? 鏈熸湜 {0}, 瀹為檯 {1}",
                    context.UntranslatedTexts.Length,
                    translations != null ? translations.Length.ToString() : "null"));
                context.Fail("Translation count mismatch.");
                return;
            }

            Log(string.Format("[瀹屾垚] 鎴愬姛缈昏瘧 {0} 鏉℃枃鏈?, translations.Length));
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

        private void Log(string message)
        {
            Console.WriteLine("[LLMTranslate] " + message);
        }

        private void DebugLog(string message)
        {
            if (!_debugMode) return;
            Log(message);
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
                        if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            sb.Append(c);
                            sb.Append(value[++i]);
                        }
                        else if (c < 0x20 || char.IsSurrogate(c))
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
