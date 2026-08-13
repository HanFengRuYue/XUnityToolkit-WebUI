namespace XUnityToolkit_WebUI.Models;

public enum HealthStatus { Healthy, Warning, Error, Unknown }

public enum PluginAnalysisState { NotRun, Running, Completed, Stale, Unavailable, Failed }

public enum DiagnosticSeverity { Info, Warning, Error }

public enum DiagnosticConfidence { Low, Medium, High }

public record HealthCheckDetail(
    string Category,
    string Excerpt,
    string? Suggestion
);

public record HealthCheckItem(
    string Id,
    string Label,
    HealthStatus Status,
    string? Detail,
    List<HealthCheckDetail>? Details = null
);

public record PluginDiagnosticEvidence(
    string ArtifactId,
    string Label,
    string? RelativePath,
    int StartLine,
    int EndLine,
    string Excerpt
);

public record PluginDiagnosticFinding(
    string Id,
    DiagnosticSeverity Severity,
    DiagnosticConfidence Confidence,
    string Category,
    string Title,
    string Explanation,
    List<string> SuggestedActions,
    List<PluginDiagnosticEvidence> Evidence
);

public record ReviewedDiagnosticArtifact(
    string Id,
    string Label,
    string Kind,
    string? RelativePath,
    bool Truncated,
    string? SelectionReason
);

public record PluginDiagnosticAnalysis(
    string Summary,
    List<PluginDiagnosticFinding> Findings,
    List<ReviewedDiagnosticArtifact> ReviewedArtifacts,
    string EndpointName,
    DateTime AnalyzedAt
);

public record PluginHealthReport(
    HealthStatus Overall,
    HealthStatus ObjectiveOverall,
    List<HealthCheckItem> Checks,
    PluginAnalysisState AnalysisState,
    string? AnalysisMessage,
    PluginDiagnosticAnalysis? Analysis,
    DateTime? LogLastModified,
    bool GameNeverRun,
    bool FreshRunVerified,
    DateTime CheckedAt
);
