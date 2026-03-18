using System.Text.Json;
using System.Text.Json.Serialization;

namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Shared file I/O utilities: atomic writes and standard JSON serialization options.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Standard JSON options for data file serialization.
    /// WriteIndented + camelCase + enum-as-string.
    /// IMPORTANT: Do NOT add a global naming policy to JsonStringEnumConverter
    /// (per CLAUDE.md enum JSON casing rules).
    /// </summary>
    public static readonly JsonSerializerOptions DataJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Atomic write: serialize to .tmp file, then move to target.
    /// Creates parent directory if it doesn't exist.
    /// </summary>
    public static async Task WriteJsonAtomicAsync<T>(string filePath, T data,
        JsonSerializerOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.Serialize(data, options ?? DataJsonOptions);
        var tmpPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, filePath, overwrite: true);
    }
}
