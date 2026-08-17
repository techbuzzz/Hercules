using System.Text;

namespace Hercules.WebApi.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDir;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _maxFiles;

    public FileLoggerProvider(string logDir, int maxFiles = 14)
    {
        _logDir = logDir;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(logDir);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal void Write(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        lock (_lock)
        {
            if (_writer is null || _currentDate != today)
            {
                _writer?.Flush();
                _writer?.Dispose();
                var path = Path.Combine(_logDir, $"hercules-{today:yyyy-MM-dd}.log");
                var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
                {
                    AutoFlush = true
                };
                _currentDate = today;
                PurgeOldFiles();
            }

            var level = logLevel switch
            {
                LogLevel.Trace => "TRCE",
                LogLevel.Debug => "DBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERRO",
                LogLevel.Critical => "CRIT",
                _ => "????"
            };

            _writer.WriteLine($"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {categoryName}: {message}");
            if (exception is not null)
            {
                _writer.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
                _writer.WriteLine($"  {exception.StackTrace}");
            }
        }
    }

    private void PurgeOldFiles()
    {
        try
        {
            var files = Directory.GetFiles(_logDir, "hercules-*.log")
                .OrderByDescending(f => f)
                .Skip(_maxFiles);
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }
}

file sealed class FileLogger(FileLoggerProvider _provider, string _categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        _provider.Write(_categoryName, logLevel, formatter(state, exception), exception);
    }
}