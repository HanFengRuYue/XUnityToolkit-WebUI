using System.Collections.Concurrent;
using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Minimal file logger provider — appends timestamped log lines to a file.
/// Rotates when file exceeds 5 MB (rename to .old, start fresh).
/// Also maintains an in-memory ring buffer and optional broadcast callback for real-time log streaming.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    internal readonly LogLevel _minLevel;
    private StreamWriter _writer;
    private readonly Lock _lock = new();
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    private readonly ConcurrentQueue<LogEntry> _recentEntries = new();
    private const int RingBufferSize = 500;

    /// <summary>
    /// Optional callback invoked (outside lock) for each new log entry.
    /// Set after DI build to broadcast via SignalR.
    /// </summary>
    internal Action<LogEntry>? LogBroadcast { get; set; }

    public FileLoggerProvider(string filePath, LogLevel minLevel = LogLevel.Information)
    {
        _filePath = filePath;
        _minLevel = minLevel;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        RotateIfNeeded();
        _writer = new StreamWriter(filePath, append: true, Encoding.UTF8) { AutoFlush = true };
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

                // Check rotation
                if (_writer.BaseStream.Length > MaxFileSize)
                {
                    _writer.Dispose();
                    RotateIfNeeded();
                    _writer = new StreamWriter(_filePath, append: true, Encoding.UTF8) { AutoFlush = true };
                }
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

    internal LogEntry[] GetRecentEntries(int count)
    {
        var all = _recentEntries.ToArray();
        return count >= all.Length ? all : all[^count..];
    }

    private void RotateIfNeeded()
    {
        try
        {
            if (File.Exists(_filePath) && new FileInfo(_filePath).Length > MaxFileSize)
            {
                var oldPath = _filePath + ".old";
                File.Move(_filePath, oldPath, overwrite: true);
            }
        }
        catch
        {
            // Best effort
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
