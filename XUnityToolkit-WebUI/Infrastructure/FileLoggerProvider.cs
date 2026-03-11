using System.Collections.Concurrent;
using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// File logger provider — creates a timestamped log file per application startup.
/// Retains the most recent 10 log files and cleans up older ones.
/// Also maintains an in-memory ring buffer and optional broadcast callback for real-time log streaming.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logsDirectory;
    private readonly string _filePath;
    private readonly string _sessionTimestamp;
    internal readonly LogLevel _minLevel;
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();
    private const int MaxLogFiles = 10;

    private readonly ConcurrentQueue<LogEntry> _recentEntries = new();
    private const int RingBufferSize = 500;

    /// <summary>
    /// Optional callback invoked (outside lock) for each new log entry.
    /// Set after DI build to broadcast via SignalR.
    /// </summary>
    internal Action<LogEntry>? LogBroadcast { get; set; }

    public FileLoggerProvider(string logsDirectory, LogLevel minLevel = LogLevel.Information)
    {
        _logsDirectory = logsDirectory;
        _minLevel = minLevel;
        _sessionTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        Directory.CreateDirectory(logsDirectory);
        CleanupOldLogFiles();
        _filePath = Path.Combine(logsDirectory, $"XUnityToolkit_{_sessionTimestamp}.log");
        _writer = new StreamWriter(_filePath, append: false, Encoding.UTF8) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    public void Dispose()
    {
        lock (_lock) { _writer.Dispose(); }
    }

    internal void WriteEntry(string category, LogLevel level, string message, Exception? exception)
    {
        if (level < _minLevel) return;

        var shortCategory = category.Contains('.')
            ? category[(category.LastIndexOf('.') + 1)..]
            : category;
        var levelTag = level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRI",
            _ => "???"
        };

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var fullMessage = exception is not null ? $"{message}\n  Exception: {exception}" : message;

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"{timestamp} [{levelTag}] [{shortCategory}] {message}");
                if (exception is not null)
                    _writer.WriteLine($"  Exception: {exception}");
            }
            catch
            {
                // Logging should never crash the app
            }
        }

        // Outside lock: enqueue to ring buffer and broadcast
        var entry = new LogEntry(timestamp, levelTag, shortCategory, fullMessage);
        _recentEntries.Enqueue(entry);
        while (_recentEntries.Count > RingBufferSize)
            _recentEntries.TryDequeue(out _);

        try { LogBroadcast?.Invoke(entry); } catch { /* never crash */ }
    }

    internal string FilePath => _filePath;

    /// <summary>
    /// The session timestamp used for this log file, for consistent export naming.
    /// </summary>
    internal string SessionTimestamp => _sessionTimestamp;

    internal LogEntry[] GetRecentEntries(int count)
    {
        var all = _recentEntries.ToArray();
        return count >= all.Length ? all : all[^count..];
    }

    /// <summary>
    /// Export all in-memory ring buffer entries as formatted log text.
    /// </summary>
    internal string ExportSessionLog()
    {
        var entries = _recentEntries.ToArray();
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            sb.AppendLine($"{entry.Timestamp} [{entry.Level}] [{entry.Category}] {entry.Message}");
        }
        return sb.ToString();
    }

    private void CleanupOldLogFiles()
    {
        try
        {
            var logFiles = Directory.GetFiles(_logsDirectory, "XUnityToolkit_*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();

            // Keep only MaxLogFiles - 1 (the new one will be created after this)
            for (int i = MaxLogFiles - 1; i < logFiles.Count; i++)
            {
                try { File.Delete(logFiles[i]); } catch { /* best effort */ }
            }

            // Also clean up legacy log files
            var legacyLog = Path.Combine(_logsDirectory, "app.log");
            if (File.Exists(legacyLog))
                try { File.Delete(legacyLog); } catch { /* best effort */ }
            var legacyOld = Path.Combine(_logsDirectory, "app.log.old");
            if (File.Exists(legacyOld))
                try { File.Delete(legacyOld); } catch { /* best effort */ }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}

internal sealed class FileLogger(string category, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        provider.WriteEntry(category, logLevel, formatter(state, exception), exception);
    }
}
