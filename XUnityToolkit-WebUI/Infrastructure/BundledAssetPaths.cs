namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Resolves paths to bundled plugin ZIPs and llama.cpp binaries
/// that are shipped alongside the executable in the bundled/ directory.
/// </summary>
public sealed class BundledAssetPaths
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "bundled");

    public string BepInEx5Directory => Path.Combine(_root, "bepinex5");
    public string BepInEx6Directory => Path.Combine(_root, "bepinex6");
    public string XUnityDirectory => Path.Combine(_root, "xunity");
    public string LlamaDirectory => Path.Combine(_root, "llama");
    public string FontsDirectory => Path.Combine(_root, "fonts");

    public string? FindBepInEx5Zip(string archPattern)
    {
        if (!Directory.Exists(BepInEx5Directory)) return null;
        return Directory.GetFiles(BepInEx5Directory, $"BepInEx_win_{archPattern}_*.zip")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    public string? FindBepInEx6Zip(string archPattern)
    {
        if (!Directory.Exists(BepInEx6Directory)) return null;
        return Directory.GetFiles(BepInEx6Directory, $"BepInEx-Unity.IL2CPP-win-{archPattern}-*.zip")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }

    public string[] GetXUnityZips() =>
        Directory.Exists(XUnityDirectory)
            ? Directory.GetFiles(XUnityDirectory, "XUnity.AutoTranslator-*.zip")
            : [];

    public string? FindLlamaZip(string pattern)
    {
        if (!Directory.Exists(LlamaDirectory)) return null;
        return Directory.GetFiles(LlamaDirectory, pattern).FirstOrDefault();
    }
}
