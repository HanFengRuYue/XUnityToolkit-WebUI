using System.Text;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Services.CharacterSets;

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

    /// <summary>Common punctuation ranges (always included). ~200 characters.</summary>
    public static HashSet<int> CommonPunctuation()
    {
        var set = new HashSet<int>();
        // General Punctuation: typographic marks only (avoid non-renderable format chars)
        int[] generalPunct =
        [
            0x2010, 0x2011, 0x2012, 0x2013, 0x2014, 0x2015, // hyphens, dashes
            0x2018, 0x2019, 0x201A, 0x201B, 0x201C, 0x201D, 0x201E, 0x201F, // quotes
            0x2020, 0x2021, 0x2022, 0x2025, 0x2026, // daggers, bullets, ellipsis
            0x2030, 0x2032, 0x2033, 0x2035, // per mille, primes
            0x2039, 0x203A, 0x203B, 0x203C, 0x203E, // angle quotes, reference mark
        ];
        foreach (var c in generalPunct) set.Add(c);
        // CJK Symbols and Punctuation: 。、「」『』【】〈〉《》
        for (int c = 0x3000; c <= 0x303F; c++)
            set.Add(c);
        // CJK Compatibility Forms (vertical variants)
        for (int c = 0xFE30; c <= 0xFE4F; c++)
            set.Add(c);
        // Fullwidth ASCII variants: ！？，．：；（）
        for (int c = 0xFF01; c <= 0xFF60; c++)
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
        new() { Id = "CJK_Common", Name = "CJK 常用", Description = "CJK 统一汉字基本集 (U+4E00–U+9FFF)，标点自动包含", CharacterCount = 0x9FFF - 0x4E00 + 1 },
        new() { Id = "CJK_Full", Name = "CJK 扩展", Description = "CJK 基本集 + 扩展 A/B（含增补平面），标点自动包含", CharacterCount = (0x9FFF - 0x4E00 + 1) + (0x4DBF - 0x3400 + 1) + (0x2A6DF - 0x20000 + 1) },
        new() { Id = "Japanese", Name = "日文常用", Description = "平假名 + 片假名 + 常用汉字，标点自动包含", CharacterCount = Japanese().Count },
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
