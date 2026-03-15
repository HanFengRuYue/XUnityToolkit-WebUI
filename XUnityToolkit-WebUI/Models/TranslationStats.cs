namespace XUnityToolkit_WebUI.Models;

public sealed record TranslationStats(
    long TotalTranslated,
    int Translating,
    int Queued,
    DateTime? LastRequestAt,
    long TotalTokensUsed,
    double AverageResponseTimeMs,
    double RequestsPerMinute,
    bool Enabled,
    IList<RecentTranslation> RecentTranslations,
    long TotalReceived,
    long TotalErrors,
    IList<TranslationError> RecentErrors,
    string? CurrentGameId
);

public sealed record RecentTranslation(
    string Original,
    string Translated,
    DateTime Timestamp,
    long TokensUsed,
    double ResponseTimeMs,
    string EndpointName,
    string? GameId
);

public sealed record TranslationError(
    string Message,
    DateTime Timestamp,
    string? EndpointName,
    string? GameId
);

public sealed record EndpointTestResult(
    string EndpointId,
    string EndpointName,
    bool Success,
    IList<string>? Translations,
    string? Error,
    double ResponseTimeMs
);

public sealed record GlossaryExtractionStats(
    bool Enabled,
    long TotalExtracted,
    int ActiveExtractions,
    long TotalExtractionCalls,
    long TotalErrors,
    IList<RecentExtraction> RecentExtractions
);

public sealed record RecentExtraction(
    string GameId,
    int TermsExtracted,
    DateTime Timestamp
);
