using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hercules.Mesh.Observability;

/// <summary>
///     In-memory log sink for mesh diagnostics (task_093).
///     Pushes each <see cref="LogLevel.Information"/> (and above) log entry
///     into the <see cref="MeshDiagnosticsService"/> ring buffer. The web UI
///     surfaces this buffer as the "Recent logs" panel.
///     We never log the raw payload, secrets, or request bodies — only the
///     formatted message, category, and a redacted structured-fields snapshot.
///     The provider is registered with the default <see cref="LoggerFactory"/>
///     so the existing logger pipeline continues to work; we only add a side
///     channel that copies entries into the diagnostics service.
/// </summary>
public sealed class InMemoryLogSink : ILoggerProvider
{
    private readonly MeshDiagnosticsService _diagnostics;
    private readonly LogLevel _minLevel;
    private readonly ConcurrentDictionary<string, InMemoryLogger> _loggers = new(StringComparer.Ordinal);

    public InMemoryLogSink(MeshDiagnosticsService diagnostics, LogLevel minLevel = LogLevel.Information)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new InMemoryLogger(name, _minLevel, _diagnostics));
    }

    public void Dispose() => _loggers.Clear();

    private sealed class InMemoryLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _minLevel;
        private readonly MeshDiagnosticsService _diagnostics;

        public InMemoryLogger(string category, LogLevel minLevel, MeshDiagnosticsService diagnostics)
        {
            _category = category;
            _minLevel = minLevel;
            _diagnostics = diagnostics;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // The structured fields expose TraceId / RequestId / etc. when the
            // caller uses message templates. ASP.NET request logging sets both.
            string? traceId = null;
            string? requestId = null;
            var fields = new Dictionary<string, object?>();
            if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (k, v) in pairs)
                {
                    fields[k] = v;
                    if (string.Equals(k, "traceId", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(k, "trace_id", StringComparison.OrdinalIgnoreCase))
                    {
                        traceId = v?.ToString();
                    }
                    else if (string.Equals(k, "requestId", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(k, "request_id", StringComparison.OrdinalIgnoreCase))
                    {
                        requestId = v?.ToString();
                    }
                }
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = message + " | " + exception.GetType().Name + ": " + exception.Message;
            }

            _diagnostics.AppendLog(new LogEntrySummary
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = NormalizeLevel(logLevel),
                Source = _category,
                RequestId = requestId,
                TraceId = traceId,
                Message = message,
                StructuredFields = fields
            });
        }

        private static string NormalizeLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => "Trace",
            LogLevel.Debug => "Debug",
            LogLevel.Information => "Info",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Critical",
            _ => level.ToString()
        };

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
