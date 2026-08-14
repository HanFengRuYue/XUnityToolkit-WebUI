using System.Text;
using XUnityToolkit_WebUI.Infrastructure;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Endpoints;

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
            var fileName = $"XUnityToolkit_{provider.SessionTimestamp}.log";
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
        var rawLines = ReadTailRawLines(filePath, maxLines);
        if (rawLines.Count == 0)
            return [];

        // Parse lines into LogEntry, folding exception continuation lines
        var entries = new List<LogEntry>();
        foreach (var raw in rawLines)
        {
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

    private static List<string> ReadTailRawLines(string filePath, int maxEntries)
    {
        const int blockSize = 16 * 1024;
        const int maxTailBytes = 4 * 1024 * 1024;
        var targetRawLines = Math.Max(maxEntries * 4, maxEntries + 50);
        var buffer = new byte[blockSize];
        var chunks = new List<byte[]>();
        var newlineCount = 0;
        var bytesReadTotal = 0;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var position = fs.Length;
            while (position > 0 && newlineCount < targetRawLines && bytesReadTotal < maxTailBytes)
            {
                var toRead = (int)Math.Min(blockSize, position);
                position -= toRead;
                fs.Seek(position, SeekOrigin.Begin);
                var read = fs.Read(buffer, 0, toRead);
                if (read <= 0)
                    break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                chunks.Add(chunk);
                bytesReadTotal += read;
                newlineCount += chunk.Count(static b => b == (byte)'\n');
            }
        }
        catch
        {
            return [];
        }

        if (chunks.Count == 0)
            return [];

        chunks.Reverse();
        var tail = new byte[chunks.Sum(static chunk => chunk.Length)];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, tail, offset, chunk.Length);
            offset += chunk.Length;
        }

        return Encoding.UTF8.GetString(tail)
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Where(static line => line.Length > 0)
            .ToList();
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
