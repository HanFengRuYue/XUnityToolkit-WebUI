namespace UnityLocalizationToolkit_WebUI.Models;

public sealed class ScriptTagRule
{
    public string Pattern { get; set; } = "";
    public ScriptTagAction Action { get; set; } = ScriptTagAction.Extract;
    public string? Description { get; set; }
    public bool IsBuiltin { get; set; }
}
