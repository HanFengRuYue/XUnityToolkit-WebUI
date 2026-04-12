namespace UnityLocalizationToolkit_WebUI.Models;

public sealed class AddGameResponse
{
    public bool NeedsExeSelection { get; init; }
    public Game? Game { get; init; }
}
