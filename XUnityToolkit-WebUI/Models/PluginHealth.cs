namespace XUnityToolkit_WebUI.Models;

public enum HealthStatus { Healthy, Warning, Error, Unknown }

public record HealthCheckItem(
    string Id,
    string Label,
    HealthStatus Status,
    string? Detail
);

public record PluginHealthReport(
    HealthStatus Overall,
    List<HealthCheckItem> Checks,
    DateTime? LogLastModified,
    bool GameNeverRun,
    DateTime CheckedAt
);
