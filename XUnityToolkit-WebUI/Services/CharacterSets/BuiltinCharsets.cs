using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services.CharacterSets;

/// <summary>
/// Static methods for enumerating built-in character sets.
/// All methods return HashSet&lt;int&gt; of Unicode codepoints.
/// </summary>
public static class BuiltinCharsets
{
    /// <summary>ASCII printable range (always included). 95 characters.</summary>
    public static HashSet<int> Ascii()
    {
        var set = new HashSet<int>();
        for (int c = 0x20; c <= 0x7E; c++)
            set.Add(c);
        return set;
    }

    /// <summary>GB2312 character set. ~7,500 characters.</summary>
    public static HashSet<int> GB2312()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb2312 = Encoding.GetEncoding("gb2312");
        var set = new HashSet<int>();
        var buf = new byte[2];

        for (int hi = 0xA1; hi <= 0xF7; hi++)
        {
            for (int lo = 0xA1; lo <= 0xFE; lo++)
            {
                buf[0] = (byte)hi;
                buf[1] = (byte)lo;
                var decoded = gb2312.GetString(buf);
                if (decoded.Length == 1 && decoded[0] != '\uFFFD')
                    set.Add(decoded[0]);
            }
        }
        return set;
    }

    /// <summary>GBK character set. ~21,000 characters.</summary>
    public static HashSet<int> GBK()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding("gbk");
        var set = new HashSet<int>();
        var buf = new byte[2];

        for (int hi = 0x81; hi <= 0xFE; hi++)
        {
            for (int lo = 0x40; lo <= 0xFE; lo++)
            {
                if (lo == 0x7F) continue;
                buf[0] = (byte)hi;
                buf[1] = (byte)lo;
                var decoded = gbk.GetString(buf);
                if (decoded.Length == 1 && decoded[0] != '\uFFFD')
                    set.Add(decoded[0]);
            }
        }
        return set;
    }

    /// <summary>CJK Unified Ideographs (Basic Block). U+4E00-U+9FFF. ~20,992 characters.</summary>
    public static HashSet<int> CjkCommon()
    {
        var set = new HashSet<int>();
        for (int c = 0x4E00; c <= 0x9FFF; c++)
            set.Add(c);
        return set;
    }

    /// <summary>
    /// CJK Common + Extension A + Extension B. ~48,000 characters.
    /// Extension B (U+20000-U+2A6DF) contains supplementary plane codepoints.
    /// </summary>
    public static HashSet<int> CjkFull()
    {
        var set = new HashSet<int>();
        for (int c = 0x4E00; c <= 0x9FFF; c++)
            set.Add(c);
        for (int c = 0x3400; c <= 0x4DBF; c++)
            set.Add(c);
        for (int c = 0x20000; c <= 0x2A6DF; c++)
            set.Add(c);
        return set;
    }

    /// <summary>
    /// Japanese common characters: Hiragana + Katakana + Joyo Kanji. ~3,000 characters.
    /// </summary>
    public static HashSet<int> Japanese()
    {
        var set = new HashSet<int>();
        for (int c = 0x3040; c <= 0x309F; c++)
            set.Add(c);
        for (int c = 0x30A0; c <= 0x30FF; c++)
            set.Add(c);
        for (int c = 0x31F0; c <= 0x31FF; c++)
            set.Add(c);
        for (int c = 0xFF65; c <= 0xFF9F; c++)
            set.Add(c);
        for (int c = 0x4E00; c <= 0x9FFF; c++)
            set.Add(c);
        return set;
    }

    private static readonly Lazy<List<CharsetInfo>> _charsetInfos = new(() =>
    [
        new() { Id = "GB2312", Name = "GB2312", Description = "简体中文 GB2312 标准字符集", CharacterCount = GB2312().Count },
        new() { Id = "GBK", Name = "GBK", Description = "简体中文 GBK 扩展字符集（包含 GB2312）", CharacterCount = GBK().Count },
        new() { Id = "CJK_Common", Name = "CJK 常用", Description = "CJK 统一汉字基本集 (U+4E00–U+9FFF)", CharacterCount = 0x9FFF - 0x4E00 + 1 },
        new() { Id = "CJK_Full", Name = "CJK 扩展", Description = "CJK 基本集 + 扩展 A/B（含增补平面）", CharacterCount = (0x9FFF - 0x4E00 + 1) + (0x4DBF - 0x3400 + 1) + (0x2A6DF - 0x20000 + 1) },
        new() { Id = "Japanese", Name = "日文常用", Description = "平假名 + 片假名 + 常用汉字", CharacterCount = Japanese().Count },
    ]);

    /// <summary>Returns metadata for all available built-in charsets.</summary>
    public static List<CharsetInfo> GetAllCharsetInfos() => _charsetInfos.Value;

    /// <summary>Superset relationships for generating warnings.</summary>
    public static readonly Dictionary<string, HashSet<string>> SupersetOf = new()
    {
        ["GBK"] = ["GB2312"],
        ["CJK_Full"] = ["CJK_Common"],
    };
}
