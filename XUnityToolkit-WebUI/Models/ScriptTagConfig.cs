namespace XUnityToolkit_WebUI.Models;

public sealed class ScriptTagConfig
{
    public int PresetVersion { get; set; }
    public List<ScriptTagRule> Rules { get; set; } = [];
}
