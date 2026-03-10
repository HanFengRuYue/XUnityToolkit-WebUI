namespace XUnityToolkit_WebUI.Models;

public sealed record TranslationStats(
    long TotalTranslated,
    int InProgress,
    DateTime? LastRequestAt
);
