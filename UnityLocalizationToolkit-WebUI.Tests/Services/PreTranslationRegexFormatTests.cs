using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Services;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Services;

public sealed class PreTranslationRegexFormatTests
{
    [Fact]
    public void Parse_ShouldRecognizeLegacySections()
    {
        const string content = """
// Two-line concatenation (both groups are text)
sr:"^([\S\s]+?)\n([\S\s]+)$"=$1\n$2
// Custom patterns
r:"^Name: (.+)$"=名称: $1
// Dynamic template variable patterns
r:"^HP: (\d+)$"=生命值: $1
""";

        var parsed = PreTranslationRegexFormat.Parse(content);

        Assert.Collection(parsed.Rules,
            rule =>
            {
                Assert.Equal(PreTranslationRegexFormat.BaseSection, rule.Section);
                Assert.Equal("sr", rule.Kind);
            },
            rule =>
            {
                Assert.Equal(PreTranslationRegexFormat.CustomSection, rule.Section);
                Assert.Equal(@"^Name: (.+)$", rule.Pattern);
            },
            rule =>
            {
                Assert.Equal(PreTranslationRegexFormat.DynamicSection, rule.Section);
                Assert.Equal(@"^HP: (\d+)$", rule.Pattern);
            });
    }

    [Fact]
    public void SerializeManaged_ShouldRoundTripRules()
    {
        var rules = new[]
        {
            new PreTranslationRegexRule("base-0", PreTranslationRegexFormat.BaseSection, "sr", @"^A$", "B"),
            new PreTranslationRegexRule("custom-0", PreTranslationRegexFormat.CustomSection, "r", @"^C$", "D"),
            new PreTranslationRegexRule("dynamic-0", PreTranslationRegexFormat.DynamicSection, "r", @"^E$", "F"),
        };

        var serialized = PreTranslationRegexFormat.SerializeManaged(rules);
        var reparsed = PreTranslationRegexFormat.Parse(serialized);

        Assert.Equal(3, reparsed.Rules.Count);
        Assert.Equal(
            rules.Select(static rule => (rule.Section, rule.Kind, rule.Pattern, rule.Replacement)),
            reparsed.Rules.Select(static rule => (rule.Section, rule.Kind, rule.Pattern, rule.Replacement)));
    }

    [Fact]
    public void RebuildManagedFile_ShouldPreserveCustomAndRefreshGeneratedSections()
    {
        var existingContent = PreTranslationRegexFormat.SerializeManaged(new[]
        {
            new PreTranslationRegexRule("base-0", PreTranslationRegexFormat.BaseSection, "sr", @"^OLD$", "OLD"),
            new PreTranslationRegexRule("custom-0", PreTranslationRegexFormat.CustomSection, "r", @"^KEEP$", "保留"),
            new PreTranslationRegexRule("dynamic-0", PreTranslationRegexFormat.DynamicSection, "r", @"^OLD_DYNAMIC$", "旧动态"),
        });

        var rebuilt = PreTranslationRegexFormat.RebuildManagedFile(
            existingContent,
            includeBaseRules: true,
            dynamicRules: new[]
            {
                new PreTranslationRegexRule("dynamic-0", PreTranslationRegexFormat.DynamicSection, "r", @"^NEW_DYNAMIC$", "新动态"),
            });

        var reparsed = PreTranslationRegexFormat.Parse(rebuilt);

        Assert.Contains(reparsed.Rules, static rule =>
            rule.Section == PreTranslationRegexFormat.CustomSection && rule.Pattern == @"^KEEP$");
        Assert.DoesNotContain(reparsed.Rules, static rule => rule.Pattern == @"^OLD$");
        Assert.DoesNotContain(reparsed.Rules, static rule => rule.Pattern == @"^OLD_DYNAMIC$");
        Assert.Contains(reparsed.Rules, static rule => rule.Pattern == @"^NEW_DYNAMIC$");
        Assert.Contains(reparsed.Rules, static rule =>
            rule.Section == PreTranslationRegexFormat.BaseSection
            && rule.Pattern == @"^([\S\s]+?)\n([\S\s]+)$");
    }

    [Fact]
    public void XUnityTranslationFormat_ShouldRoundTripPreTranslatedEntries()
    {
        var entries = new[]
        {
            new TranslationEntryDto("Hello", "你好"),
            new TranslationEntryDto("Line\\nBreak", "换行"),
        };

        var serialized = XUnityTranslationFormat.SerializeEntries(entries);
        var reparsed = XUnityTranslationFormat.ParseLines(serialized.Split(["\r\n", "\n"], StringSplitOptions.None));

        Assert.Equal(
            entries.Select(static entry => (entry.Original, entry.Translation)),
            reparsed.Select(static entry => (entry.Original, entry.Translation)));
    }
}
