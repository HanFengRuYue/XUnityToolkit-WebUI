namespace XUnityToolkit_WebUI.Models;

public sealed class GlossaryEntry
{
    public string Original { get; set; } = "";
    public string Translation { get; set; } = "";
    public bool IsRegex { get; set; }
}
