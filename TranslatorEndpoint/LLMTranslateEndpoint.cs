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
        private const string DefaultUrl = "http://localhost:51821";

        private string _translateUrl;

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
            get { return 1; }
        }

        public override int MaxTranslationsPerRequest
        {
            get { return 10; }
        }

        public override void Initialize(IInitializationContext context)
        {
            var baseUrl = context.GetOrCreateSetting<string>(ConfigSection, ConfigUrlKey, DefaultUrl);
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = DefaultUrl;

            _translateUrl = baseUrl.TrimEnd(new char[] { '/' }) + "/api/translate";
        }

        public override void OnCreateRequest(IHttpRequestCreationContext context)
        {
            var body = BuildRequestBody(
                context.UntranslatedTexts,
                context.SourceLanguage,
                context.DestinationLanguage);

            var request = new XUnityWebRequest("POST", _translateUrl, body);
            request.Headers[HttpRequestHeader.ContentType] = "application/json";
            request.Headers[HttpRequestHeader.Accept] = "application/json";

            context.Complete(request);
        }

        public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
        {
            var raw = context.Response.Data;
            if (string.IsNullOrEmpty(raw))
            {
                context.Fail("Empty response from toolkit backend.");
                return;
            }

            var translations = ParseTranslationsArray(raw);

            if (translations == null || translations.Length != context.UntranslatedTexts.Length)
            {
                context.Fail("Translation count mismatch.");
                return;
            }

            context.Complete(translations);
        }

        // --- JSON helpers (no external JSON library available in .NET 3.5) ---

        private static string BuildRequestBody(string[] texts, string from, string to)
        {
            var sb = new StringBuilder();
            sb.Append("{\"texts\":");
            SerializeStringArray(sb, texts);
            sb.Append(",\"from\":\"");
            EscapeJsonString(sb, from);
            sb.Append("\",\"to\":\"");
            EscapeJsonString(sb, to);
            sb.Append("\"}");
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
                                if (pos + 5 < json.Length)
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
