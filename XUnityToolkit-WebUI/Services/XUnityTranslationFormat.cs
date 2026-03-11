using System.Text;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Shared encode/decode for XUnity.AutoTranslator translation cache file format.
/// Format: encoded_original=encoded_translation (one per line).
/// Escapes: \ → \\, newline → \n, CR → \r, = → \=
/// </summary>
internal static class XUnityTranslationFormat
{
    /// <summary>Encode a string for XUnity translation cache file.</summary>
    internal static string Encode(string text)
    {
        var sb = new StringBuilder(text.Length + 10);
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '=': sb.Append("\\="); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Decode a string from XUnity translation cache file format.</summary>
    internal static string Decode(string encoded)
    {
        var sb = new StringBuilder(encoded.Length);
        for (int i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '\\' && i + 1 < encoded.Length)
            {
                var next = encoded[i + 1];
                switch (next)
                {
                    case '\\': sb.Append('\\'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case '=': sb.Append('='); i++; break;
                    default: sb.Append(encoded[i]); break;
                }
            }
            else
            {
                sb.Append(encoded[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Find the first unescaped '=' in a line.</summary>
    internal static int FindUnescapedEquals(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '=') return i;
        }
        return -1;
    }

    /// <summary>
    /// Parse translation file lines into a list of (original, translation) pairs.
    /// Returns decoded strings.
    /// </summary>
    internal static List<TranslationEntryDto> ParseLines(IEnumerable<string> lines)
    {
        var entries = new List<TranslationEntryDto>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
            var eqIdx = FindUnescapedEquals(line);
            if (eqIdx < 0) continue;

            var encodedOriginal = line[..eqIdx];
            var encodedTranslation = line[(eqIdx + 1)..];
            entries.Add(new TranslationEntryDto(
                Decode(encodedOriginal),
                Decode(encodedTranslation)));
        }
        return entries;
    }

    /// <summary>
    /// Serialize entries to XUnity translation cache format lines.
    /// </summary>
    internal static string SerializeEntries(IReadOnlyList<TranslationEntryDto> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Edited by XUnity Toolkit");
        foreach (var entry in entries)
        {
            sb.Append(Encode(entry.Original));
            sb.Append('=');
            sb.AppendLine(Encode(entry.Translation));
        }
        return sb.ToString();
    }
}

internal record TranslationEntryDto(string Original, string Translation);
