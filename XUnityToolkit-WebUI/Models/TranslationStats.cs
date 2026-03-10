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
    IList<TranslationError> RecentErrors
);

public sealed record RecentTranslation(
    string Original,
    string Translated,
    DateTime Timestamp,
    long TokensUsed,
    double ResponseTimeMs,
    string EndpointName
);

public sealed record TranslationError(
    string Message,
    DateTime Timestamp,
    string? EndpointName
);

public sealed record EndpointTestResult(
    string EndpointId,
    string EndpointName,
    bool Success,
    IList<string>? Translations,
    string? Error,
    double ResponseTimeMs
);
