namespace UnityLocalizationToolkit_WebUI.Models;

public sealed class BatchAddResult
{
    public List<Game> Added { get; init; } = [];
    public List<BatchSkippedItem> Skipped { get; init; } = [];
}

public sealed class BatchSkippedItem
{
    public required string FolderName { get; init; }
    public required string Reason { get; init; }
}
