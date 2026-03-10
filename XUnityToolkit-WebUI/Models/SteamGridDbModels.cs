namespace XUnityToolkit_WebUI.Models;

public sealed class SteamGridDbSearchResult
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Verified { get; set; }
}

public sealed class SteamGridDbImage
{
    public int Id { get; set; }
    public string Url { get; set; } = "";
    public string Thumb { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string Style { get; set; } = "";
    public string Mime { get; set; } = "";
}

public sealed class CoverMeta
{
    public string ContentType { get; set; } = "image/jpeg";
    public string Source { get; set; } = "upload";
    public string? SourceUrl { get; set; }
    public int? SteamGridDbGameId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

public record CoverInfo(bool HasCover, string? Source, int? SteamGridDbGameId);

public sealed class SteamStoreSearchResult
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TinyImage { get; set; } = "";
}
