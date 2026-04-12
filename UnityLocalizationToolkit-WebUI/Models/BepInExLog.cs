namespace UnityLocalizationToolkit_WebUI.Models;

public record BepInExLogResponse(string Content, long FileSize, DateTime LastModified);
public record BepInExLogAnalysis(string Report, string EndpointName, DateTime AnalyzedAt);
