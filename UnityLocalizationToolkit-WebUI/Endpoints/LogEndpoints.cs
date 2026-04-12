using System.Text;
using UnityLocalizationToolkit_WebUI.Infrastructure;
using UnityLocalizationToolkit_WebUI.Models;

namespace UnityLocalizationToolkit_WebUI.Endpoints;

public static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/logs");

        group.MapGet("/", (FileLoggerProvider provider, int? count) =>
        {
            var n = Math.Clamp(count ?? 200, 1, 500);
            var entries = provider.GetRecentEntries(n);
            return Results.Ok(ApiResult<LogEntry[]>.Ok(entries));
        });

        // Download the complete current session log from the on-disk session file.
        group.MapGet("/download", (FileLoggerProvider provider) =>
        {
            var stream = provider.ExportSessionLog();
            var fileName = $"UnityLocalizationToolkit_{provider.SessionTimestamp}.log";
            return Results.File(stream, "text/plain", fileName);
        });

        group.MapGet("/history", (FileLoggerProvider provider, int? lines) =>
        {
            var n = Math.Clamp(lines ?? 500, 1, 2000);
            var filePath = provider.FilePath;

            if (!File.Exists(filePath))
                return Results.Ok(ApiResult<LogEntry[]>.Ok([]));

            var entries = ReadLastLines(filePath, n);
            return Results.Ok(ApiResult<LogEntry[]>.Ok(entries));
        });
    }

    private static LogEntry[] ReadLastLines(string filePath, int maxLines)
    {
        var rawLines = new List<string>();
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
                rawLines.Add(line);
        }
        catch
        {
            return [];
        }

        // Parse lines into LogEntry, folding exception continuation lines
        var entries = new List<LogEntry>();
        for (var i = 0; i < rawLines.Count; i++)
        {
            var raw = rawLines[i];
            var entry = ParseLogLine(raw);
            if (entry is not null)
            {
                entries.Add(entry);
            }
            else if (entries.Count > 0)
            {
                // Continuation line (e.g. "  Exception: ...") — append to previous entry
                var prev = entries[^1];
                entries[^1] = prev with { Message = prev.Message + "\n" + raw };
            }
        }

        // Return the last N entries
        return entries.Count <= maxLines
            ? entries.ToArray()
            : entries.GetRange(entries.Count - maxLines, maxLines).ToArray();
    }

    /// <summary>
    /// Parses a log line with format: "yyyy-MM-dd HH:mm:ss [LVL] [Category] message"
    /// </summary>
    private static LogEntry? ParseLogLine(string line)
    {
        // Minimum: "2026-03-11 14:22:05 [INF] [X] m"  → at least 29 chars
        if (line.Length < 29 || line[19] != ' ' || line[20] != '[')
            return null;

        var timestamp = line[..19]; // "yyyy-MM-dd HH:mm:ss"
        var closeBracket1 = line.IndexOf(']', 21);
        if (closeBracket1 < 0) return null;
        var level = line[21..closeBracket1]; // "INF", "WRN", etc.

        // Expect " [Category] " after level
        var openBracket2 = line.IndexOf('[', closeBracket1 + 1);
        if (openBracket2 < 0) return null;
        var closeBracket2 = line.IndexOf(']', openBracket2 + 1);
        if (closeBracket2 < 0) return null;
        var category = line[(openBracket2 + 1)..closeBracket2];

        var message = closeBracket2 + 2 < line.Length ? line[(closeBracket2 + 2)..] : "";

        return new LogEntry(timestamp, level, category, message);
    }
}
