using UnityLocalizationToolkit_WebUI.Models;
using UnityLocalizationToolkit_WebUI.Services;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Services;

public sealed class LlmTranslationServiceJapaneseResidualTests
{
    [Fact]
    public void ApplyJapaneseResidualPersistenceGuard_ShouldMarkSuspiciousJaToZhResultAsNonPersistable()
    {
        var originals = new[] { "私の得意プレイね" };
        var translations = new[] { "这是我的得意プレイ呢" };
        var persistable = new[] { true };
        List<TermEntry>?[] perTextTerms = [null];

        var issues = LlmTranslationService.ApplyJapaneseResidualPersistenceGuard(
            translations, "ja", "zh", perTextTerms, persistable);

        var issue = Assert.Single(issues);
        Assert.Equal(0, issue.Index);
        Assert.Contains("プレイ", issue.ResidualPreview);
        Assert.False(persistable[0]);

        var collected = LlmTranslationService.CollectPersistableTranslations(
            originals, translations, persistable);

        Assert.Empty(collected.Originals);
        Assert.Empty(collected.Translations);
    }

    [Fact]
    public void ApplyJapaneseResidualPersistenceGuard_ShouldKeepAllowedDntJapaneseTermsPersistable()
    {
        var translations = new[] { "请按スキル键继续" };
        var persistable = new[] { true };
        List<TermEntry>?[] perTextTerms =
        [
            [
                new TermEntry
                {
                    Original = "スキル",
                    Type = TermType.DoNotTranslate
                }
            ]
        ];

        var issues = LlmTranslationService.ApplyJapaneseResidualPersistenceGuard(
            translations, "ja", "zh", perTextTerms, persistable);

        Assert.Empty(issues);
        Assert.True(persistable[0]);
    }

    [Fact]
    public void ApplyJapaneseResidualPersistenceGuard_ShouldSkipNonJaToZhPairs()
    {
        var translations = new[] { "这是我的得意プレイ呢" };
        var persistable = new[] { true };
        List<TermEntry>?[] perTextTerms = [null];

        var issues = LlmTranslationService.ApplyJapaneseResidualPersistenceGuard(
            translations, "en", "zh", perTextTerms, persistable);

        Assert.Empty(issues);
        Assert.True(persistable[0]);
    }
}
