using XUnityToolkit_WebUI.Services;
using Xunit;

namespace XUnityToolkit_WebUI.Tests.Services;

public sealed class TranslationEditorPathResolverTests : IDisposable
{
    private readonly string _root;

    public TranslationEditorPathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"xut-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ListAvailablePreTranslationLanguages_ShouldScanMatchingFiles()
    {
        CreateTranslationFile("zh", "_PreTranslated.txt");
        CreateTranslationFile("en", "_PreTranslated_Regex.txt");
        Directory.CreateDirectory(Path.Combine(_root, "BepInEx", "Translation", "bad..lang", "Text"));

        var languages = TranslationEditorPathResolver.ListAvailablePreTranslationLanguages(
            _root,
            "_PreTranslated.txt",
            "_PreTranslated_Regex.txt");

        Assert.Equal(["en", "zh"], languages);
    }

    [Fact]
    public void ResolvePreTranslationLanguage_ShouldPreferRequestedThenDefaultThenDetected()
    {
        CreateTranslationFile("ko", "_PreTranslated.txt");

        var requested = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
            _root,
            requestedLang: "ja",
            defaultLang: "zh",
            "_PreTranslated.txt");
        var fallback = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
            _root,
            requestedLang: null,
            defaultLang: "zh",
            "_PreTranslated.txt");
        var detected = TranslationEditorPathResolver.ResolvePreTranslationLanguage(
            _root,
            requestedLang: null,
            defaultLang: null,
            "_PreTranslated.txt");

        Assert.Equal("ja", requested);
        Assert.Equal("zh", fallback);
        Assert.Equal("ko", detected);
    }

    [Fact]
    public void ResolveDefaultTranslationFilePath_ShouldRejectTraversal()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                _root,
                @"..\..\outside.txt",
                "zh"));
    }

    private void CreateTranslationFile(string language, string fileName)
    {
        var directory = Path.Combine(_root, "BepInEx", "Translation", language, "Text");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "// test");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
