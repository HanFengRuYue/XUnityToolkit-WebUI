namespace XUnityToolkit_WebUI.Models;

public enum HealthStatus { Healthy, Warning, Error, Unknown }

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

public record PluginHealthReport(
    HealthStatus Overall,
    List<HealthCheckItem> Checks,
    DateTime? LogLastModified,
    bool GameNeverRun,
    DateTime CheckedAt
);
