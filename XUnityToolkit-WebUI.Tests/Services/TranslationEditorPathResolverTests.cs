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
    public void ResolveDefaultTranslationFilePath_ShouldRejectTraversal()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TranslationEditorPathResolver.ResolveDefaultTranslationFilePath(
                _root,
                @"..\..\outside.txt",
                "zh"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
