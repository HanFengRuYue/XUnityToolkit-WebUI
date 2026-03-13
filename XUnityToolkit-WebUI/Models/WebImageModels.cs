namespace XUnityToolkit_WebUI.Models;

public sealed class WebImageResult
{
    public string ThumbUrl { get; set; } = "";
    public string FullUrl { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Title { get; set; }
}
