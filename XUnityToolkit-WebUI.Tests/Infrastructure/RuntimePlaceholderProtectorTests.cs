using XUnityToolkit_WebUI.Infrastructure;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Infrastructure;

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
        var protectedResult = RuntimePlaceholderProtector.Protect(["值：【SPECIAL_01】"]);

        var protectedText = Assert.Single(protectedResult.Texts);
        Assert.Equal("值：{{XU_RT_0}}", protectedText);

        var restored = RuntimePlaceholderProtector.Restore(protectedText, protectedResult.Mapping);
        Assert.Equal("值：【SPECIAL_01】", restored);
    }

    [Fact]
    public void Restore_ShouldAcceptFullWidthBracesAndSpacingVariants()
    {
        var protectedResult = RuntimePlaceholderProtector.Protect(["[SPECIAL_01]"]);

        var restored = RuntimePlaceholderProtector.Restore("｛｛ xu_rt_0 ｝｝", protectedResult.Mapping);
        Assert.Equal("[SPECIAL_01]", restored);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldFailOnBracketStyleMismatch()
    {
        var valid = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Value: [SPECIAL_01]",
            "值：[SPECIAL_01]");
        var invalid = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Value: [SPECIAL_01]",
            "值：【SPECIAL_01】");

        Assert.True(valid);
        Assert.False(invalid);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldFailWhenCandidateHallucinatesPlaceholder()
    {
        var accepted = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Plain text",
            "普通文本");
        var rejected = RuntimePlaceholderProtector.HasExactRoundTrip(
            "Plain text",
            "普通文本 [SPECIAL_01]");

        Assert.True(accepted);
        Assert.False(rejected);
    }

    [Fact]
    public void HasExactRoundTrip_ShouldRejectBrokenTranslationMemoryCandidate()
    {
        var source = "So then, what exactly are you having problems with?.= [SPECIAL_01]";
        var cachedTranslation = "那么，你到底在烦恼些什么呢？【SPECIAL_01】";

        Assert.False(RuntimePlaceholderProtector.HasExactRoundTrip(source, cachedTranslation));
    }

    [Fact]
    public void TryRestoreAndValidate_ShouldRejectBrokenRuntimePlaceholder()
    {
        var protectedResult = RuntimePlaceholderProtector.Protect(["[SPECIAL_01]"]);

        var success = RuntimePlaceholderProtector.TryRestoreAndValidate(
            "[SPECIAL_01]",
            "{{XU_RT_0}}",
            protectedResult.Mapping,
            out var restored);
        var failure = RuntimePlaceholderProtector.TryRestoreAndValidate(
            "[SPECIAL_01]",
            "SPECIAL_01",
            protectedResult.Mapping,
            out var brokenRestored);

        Assert.True(success);
        Assert.Equal("[SPECIAL_01]", restored);
        Assert.False(failure);
        Assert.Equal("SPECIAL_01", brokenRestored);
    }
}
