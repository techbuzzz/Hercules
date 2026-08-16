using System.Collections.Concurrent;
using System.Net;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Observability;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

/// <summary>
/// Tests for task_085: ResilientLLMClient retry/fallback warning logs are
/// sampled 1-in-N while the underlying LlmRetryCounter metric increments on
/// every retryable failure (no sampling on metrics).
/// </summary>
public class ResilientLLMClientSampledLogTests
{
    // Install a MeterListener at module load so it can capture instruments
    // published by OtelMetrics' static initializer (otherwise the listener
    // would only see instruments created after its Start() call).
    private static readonly System.Diagnostics.Metrics.MeterListener _listener = BuildListener();

    private static System.Diagnostics.Metrics.MeterListener BuildListener()
    {
        var l = new System.Diagnostics.Metrics.MeterListener();
        l.InstrumentPublished = (instr, listener) =>
        {
            if (instr.Meter.Name == OtelSetup.ServiceName &&
                instr.Name == "hercules.llm.retry.count")
            {
                listener.EnableMeasurementEvents(instr);
            }
        };
        l.SetMeasurementEventCallback<long>(OnRetryLong);
        l.Start();
        return l;
    }

    private static long _retryTotal;
    private static readonly object _gate = new();

    private static void OnRetryLong<T>(System.Diagnostics.Metrics.Instrument instrument, T value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        if (instrument.Meter.Name == OtelSetup.ServiceName &&
            instrument.Name == "hercules.llm.retry.count")
        {
            lock (_gate) { _retryTotal += Convert.ToInt64(value); }
        }
    }

    private static long GetRetryTotal()
    {
        // Force a measurement publish by touching the counter. The delta
        // is included in the total because the listener captures every Add().
        // We subtract the 0 we just added so callers see the pre-touch value.
        lock (_gate)
        {
            var total = _retryTotal;
            return total;
        }
    }

    [Fact]
    public async Task Retryable_failures_emit_sampled_warnings_and_increment_counter()
    {
        // Build a single-provider chain whose client throws a retryable error
        // (HTTP 503) on every call. With 3 attempts, that yields
        //   - 3 LogWarning calls on the retry path
        //   - 1 LogWarning call on the "exhausted retries" path
        // for each CompleteAsync invocation.
        const int sampleRate = 10;
        const int invocations = 100;
        const int retryableExceptionsPerInvocation = 3;
        const int fallbackLogPerInvocation = 1;
        // Total expected log emissions per invocation = 3 retry-warn + 1 exhausted-warn.
        const int expectedLogsPerInvocation = retryableExceptionsPerInvocation + fallbackLogPerInvocation;
        const int totalCalls = invocations * expectedLogsPerInvocation;

        // Snapshot the retry-counter baseline before the work — the module-level
        // listener is shared across tests so we measure *deltas* not absolutes.
        var baseline = GetRetryTotal();

        var client = new RecordingLlmClient(
            "p1", "model-p1",
            new HttpRequestException("503", inner: null, HttpStatusCode.ServiceUnavailable));
        var cfg = new LlmConfig { Provider = "p1", Fallback = [] };
        var factory = new SingleProviderFactory(client);
        var router = new RoleRouter(new AppConfig { Llm = cfg }, factory);

        var sink = new ListLogger<ResilientLLMClient>();
        var resilient = new ResilientLLMClient(cfg, factory, router, sink, otel: null);
        resilient.SetLogSampleRate(sampleRate);

        // Drive `invocations` independent CompleteAsync calls. Each call
        // exhausts the 3-retry budget on the only provider and falls off the
        // end of the chain (no fallback configured).
        for (var i = 0; i < invocations; i++)
        {
            try
            {
                await resilient.CompleteAsync(Roles.Main, [new ChatTurn(ChatRole.User, "ping")]);
            }
            catch (InvalidOperationException)
            {
                // Expected: "all providers unavailable" once we run out of fallbacks.
            }
        }

        // The structured warning is sampled 1-in-10: with `totalCalls` events,
        // we expect exactly totalCalls / 10 emissions.
        var expectedLogs = totalCalls / sampleRate;
        Assert.Equal(expectedLogs, sink.Entries.Count(e => e.Level == LogLevel.Warning));

        // The LlmRetryCounter increments on every retryable failure, never sampled.
        // 100 invocations * 3 retry attempts = 300 increments on the retry path.
        var retryDelta = GetRetryTotal() - baseline;
        Assert.Equal(invocations * retryableExceptionsPerInvocation, retryDelta);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private sealed class SingleProviderFactory : ILLMClientFactory
    {
        private readonly ILLMClient _client;
        public SingleProviderFactory(ILLMClient client) => _client = client;
        public ILLMClient Create(string provider) => _client;
    }

    private sealed class RecordingLlmClient : ILLMClient
    {
        private readonly string _provider;
        private readonly string _model;
        private readonly Exception _ex;
        public RecordingLlmClient(string provider, string model, Exception ex)
        {
            _provider = provider;
            _model = model;
            _ex = ex;
        }
        public string ProviderName => _provider;
        public string ModelName => _model;
        public Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => Task.FromException<LlmResponse>(_ex);
        public Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => Task.FromException<LlmResponse>(_ex);
        public IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => throw _ex;
        public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => throw _ex;
    }

    /// <summary>
    /// Minimal in-memory ILogger sink that records every (level, message) pair.
    /// </summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public ConcurrentBag<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Captures the value of the LlmRetryCounter by listening to the Meter
    /// that publishes it. Call EnableRetryCounter() before triggering any
    /// retry events so the listener is wired up in time.
    /// </summary>
    // The module-level _listener (created when the class is first loaded) is
    // the real implementation; it captures every measurement event from
    // hercules.llm.retry.count via the InstrumentPublished callback. GetRetryTotal()
    // returns the running total — callers should subtract a baseline taken
    // before the work under test to obtain a delta.
}
