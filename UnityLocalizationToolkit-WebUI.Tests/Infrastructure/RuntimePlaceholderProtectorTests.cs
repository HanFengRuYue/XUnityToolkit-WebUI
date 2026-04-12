using UnityLocalizationToolkit_WebUI.Infrastructure;
using Xunit;

namespace UnityLocalizationToolkit_WebUI.Tests.Infrastructure;

public sealed class RuntimePlaceholderProtectorTests
{
    [Fact]
    public void ProtectAndRestore_ShouldRoundTripAsciiSpecialPlaceholder()
    {
        var protectedResult = RuntimePlaceholderProtector.Protect(["Value: [SPECIAL_01]"]);

        var protectedText = Assert.Single(protectedResult.Texts);
        Assert.Equal("Value: {{XU_RT_0}}", protectedText);

        var restored = RuntimePlaceholderProtector.Restore(protectedText, protectedResult.Mapping);
        Assert.Equal("Value: [SPECIAL_01]", restored);
    }

    [Fact]
    public void ProtectAndRestore_ShouldRoundTripFullWidthSpecialPlaceholder()
    {
        const string source = "Value:\u3010SPECIAL_01\u3011";

        var protectedResult = RuntimePlaceholderProtector.Protect([source]);

        var protectedText = Assert.Single(protectedResult.Texts);
        Assert.Equal("Value:{{XU_RT_0}}", protectedText);

        var restored = RuntimePlaceholderProtector.Restore(protectedText, protectedResult.Mapping);
        Assert.Equal(source, restored);
    }

    [Fact]
    public void ProtectAndRestore_ShouldRoundTripCurlyTemplatePlaceholders()
    {
        const string source = "Hello {PLAYER}, meet {NpcName} and {QUEST_ID}.";

        var protectedResult = RuntimePlaceholderProtector.Protect([source]);

        var protectedText = Assert.Single(protectedResult.Texts);
        Assert.Equal("Hello {{XU_RT_0}}, meet {{XU_RT_1}} and {{XU_RT_2}}.", protectedText);

        var restored = RuntimePlaceholderProtector.Restore(protectedText, protectedResult.Mapping);
        Assert.Equal(source, restored);
    }

    [Fact]
    public void Restore_ShouldAcceptFullWidthBracesAndSpacingVariants()
    {
        var protectedResult = RuntimePlaceholderProtector.Protect(["[SPECIAL_01]"]);

        var restored = RuntimePlaceholderProtector.Restore("\uFF5B\uFF5B xu_rt_0 \uFF5D\uFF5D", protectedResult.Mapping);
        Assert.Equal("[SPECIAL_01]", restored);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldFailOnBracketStyleMismatch()
    {
        var valid = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Value: [SPECIAL_01]",
            "Translated: [SPECIAL_01]");
        var invalid = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Value: [SPECIAL_01]",
            "Translated: \u3010SPECIAL_01\u3011");

        Assert.True(valid);
        Assert.False(invalid);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldFailOnCurlyPlaceholderCaseOrCountMismatch()
    {
        Assert.False(RuntimePlaceholderProtector.HasExactRoundTrip(
            "Hello {PLAYER}",
            "Hello {player}"));

        Assert.False(RuntimePlaceholderProtector.HasExactRoundTrip(
            "{PLAYER} vs {PLAYER}",
            "{PLAYER} vs {USER}"));
    }

    [Fact]
    public void HasExactRoundTrip_ShouldFailWhenCandidateHallucinatesPlaceholder()
    {
        var accepted = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Plain text",
            "Translated text");
        var rejected = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Plain text",
            "Translated text [SPECIAL_01]");

        Assert.True(accepted);
        Assert.False(rejected);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldRejectBrokenTranslationMemoryCandidate()
    {
        var source = "So then, what exactly are you having problems with?.= [SPECIAL_01]";
        var cachedTranslation = "So then, what exactly are you having problems with?.= \u3010SPECIAL_01\u3011";

        Assert.False(RuntimePlaceholderProtector.HasExactRoundTrip(source, cachedTranslation));
    }

    [Fact]
    public void TryRestoreAndValidate_ShouldRejectBrokenRuntimePlaceholder()
    {
        var protectedResult = RuntimePlaceholderProtector.Protect(["{PLAYER}"]);

        var success = RuntimePlaceholderProtector.TryRestoreAndValidate(
            "{PLAYER}",
            "{{XU_RT_0}}",
            protectedResult.Mapping,
            out var restored);
        var failure = RuntimePlaceholderProtector.TryRestoreAndValidate(
            "{PLAYER}",
            "{USER}",
            protectedResult.Mapping,
            out var brokenRestored);

        Assert.True(success);
        Assert.Equal("{PLAYER}", restored);
        Assert.False(failure);
        Assert.Equal("{USER}", brokenRestored);
    }

    [Fact]
    public void Protect_ShouldIgnoreJsonAndSpacedBraces()
    {
        const string source = "{\"name\":\"a\"} { player }";

        var protectedResult = RuntimePlaceholderProtector.Protect([source]);

        Assert.False(protectedResult.HasProtectedTokens);
        Assert.Equal(source, Assert.Single(protectedResult.Texts));
    }
}
