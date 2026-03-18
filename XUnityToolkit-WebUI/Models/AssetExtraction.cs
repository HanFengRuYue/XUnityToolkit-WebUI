namespace XUnityToolkit_WebUI.Models;

public sealed class AssetExtractionResult
{
    public string GameId { get; set; } = string.Empty;
    public List<ExtractedText> Texts { get; init; } = [];
    public string? DetectedLanguage { get; set; }
    public int TotalAssetsScanned { get; set; }
    public int TotalTextsExtracted { get; set; }
    public DateTime ExtractedAt { get; init; } = DateTime.UtcNow;
}

public sealed class ExtractedText
{
    public required string Text { get; init; }
    public required string Source { get; init; } // e.g. "TextAsset:Prologue" or "MonoBehaviour:DialogueData"
    public required string AssetFile { get; init; } // e.g. "sharedassets0.assets"
}

public sealed class AssetExtractionProgress
{
    public int TotalFiles { get; set; }
    public int ScannedFiles { get; set; }
    public int ExtractedTexts { get; set; }
    public string? CurrentFile { get; set; }
    public string? Phase { get; set; } // "scanning" | "extracting" | "detecting"
}

public sealed class PreTranslationStatus
{
    public required string GameId { get; init; }
    public PreTranslationState State { get; set; } = PreTranslationState.Idle;
    public int TotalTexts { get; set; }
    public int TranslatedTexts { get; set; }
    public int FailedTexts { get; set; }
    public string? Error { get; set; }
    public int CurrentRound { get; set; }
    public string? CurrentPhase { get; set; } // "round1", "patternAnalysis", "termExtraction", "termReview", "round2", "writeCache"
    public int PhaseProgress { get; set; }  // current batch within non-translation phases
    public int PhaseTotal { get; set; }     // total batches within non-translation phases
    public int ExtractedTermCount { get; set; }
    public int DynamicPatternCount { get; set; }
}

public enum PreTranslationState
{
    Idle,
    Running,
    Completed,
    Failed,
    Cancelled,
    AwaitingTermReview
}
