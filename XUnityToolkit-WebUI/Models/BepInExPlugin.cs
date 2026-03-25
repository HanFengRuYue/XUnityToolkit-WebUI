namespace XUnityToolkit_WebUI.Models;

public sealed class BepInExPlugin
{
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public bool Enabled { get; init; }
    public long FileSize { get; init; }
    public string? PluginGuid { get; init; }
    public string? PluginName { get; init; }
    public string? PluginVersion { get; init; }
    public bool IsToolkitManaged { get; init; }
    public string? ConfigFileName { get; init; }
}
