namespace UnityLocalizationToolkit_WebUI.Models;

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
)
{
    /// <summary>Total texts that had at least one matched term.</summary>
    public int TermMatchedTextCount { get; init; }
    /// <summary>Texts that passed audit during Phase 1 (natural translation).</summary>
    public int TermAuditPhase1PassCount { get; init; }
    /// <summary>Texts that passed audit during Phase 2 (placeholder translation).</summary>
    public int TermAuditPhase2PassCount { get; init; }
    /// <summary>Texts that needed Phase 3 force correction.</summary>
    public int TermAuditForceCorrectedCount { get; init; }
    public int TranslationMemoryHits { get; init; }
    public int TranslationMemoryFuzzyHits { get; init; }
    public int TranslationMemoryPatternHits { get; init; }
    public int TranslationMemoryMisses { get; init; }
    /// <summary>Current max concurrency (API request slots).</summary>
    public int MaxConcurrency { get; init; }
    public int DynamicPatternCount { get; init; }
    public int ExtractedTermCount { get; init; }
}

public sealed record RecentTranslation(
    string Original,
    string Translated,
    DateTime Timestamp,
    long TokensUsed,
    double ResponseTimeMs,
    string EndpointName,
    string? GameId
)
{
    /// <summary>Whether any terms were matched for this text.</summary>
    public bool HasTerms { get; init; }

    /// <summary>Whether any DNT entries were matched for this text.</summary>
    public bool HasDnt { get; init; }

    /// <summary>
    /// Term audit result: null=no audit, "phase1Pass"=first-pass pass,
    /// "phase2Pass"=placeholder-pass, "forceCorrected"=force-corrected, "failed"=audit failed
    /// </summary>
    public string? TermAuditResult { get; init; }

    /// <summary>
    /// Translation source: null=LLM, "tmExact", "tmFuzzy", "tmPattern"
    /// </summary>
    public string? TranslationSource { get; init; }
};

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

public sealed record PreTranslationCacheStats(
    int TotalPreTranslated,
    int CacheHits,
    int CacheMisses,
    int NewTexts,
    double HitRate,
    IList<CacheMissEntry> RecentMisses
);

public sealed record CacheMissEntry(
    string PreTranslatedKey,
    string RuntimeText,
    DateTime Timestamp
);
