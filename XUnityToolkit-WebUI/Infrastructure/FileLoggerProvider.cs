using System.Text;

namespace XUnityToolkit_WebUI.Infrastructure;

/// <summary>
/// Minimal file logger provider — appends timestamped log lines to a file.
/// Rotates when file exceeds 5 MB (rename to .old, start fresh).
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    internal readonly LogLevel _minLevel;
    private StreamWriter _writer;
    private readonly Lock _lock = new();
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

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

        lock (_lock)
        {
            try
            {
                _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{levelTag}] [{shortCategory}] {message}");
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
