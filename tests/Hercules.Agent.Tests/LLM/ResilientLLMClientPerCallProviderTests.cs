using System.Collections.Concurrent;
using System.Diagnostics;
using Hercules.Config;
using Hercules.LLM;
using Hercules.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Hercules.Agent.Tests.LLM;

/// <summary>
/// Tests for task_076: ResilientLLMClient must NOT mutate shared instance
/// <c>ProviderName</c>/<c>ModelName</c> per-call (race under concurrent calls).
/// Per-call provider/model info must come from <see cref="LlmResponse"/>.
/// </summary>
public class ResilientLLMClientPerCallProviderTests
{
    private static IReadOnlyList<ChatTurn> SampleMessages() =>
        [new ChatTurn(ChatRole.User, "ping")];

    private static LlmConfig MakeConfig(params string[] chain)
    {
        var cfg = new LlmConfig
        {
            Provider = chain[0],
            Fallback = chain.Skip(1).ToList()
        };
        return cfg;
    }

    private static (ResilientLLMClient client, FakeFactory factory) Build(
        LlmConfig cfg,
        IDictionary<string, ILLMClient> clientsByProvider,
        IOtelService? otel = null)
    {
        var factory = new FakeFactory(clientsByProvider);
        var appConfig = new AppConfig
        {
            Llm = cfg
        };
        var router = new RoleRouter(appConfig, factory);
        var resilient = new ResilientLLMClient(cfg, factory, router, NullLogger<ResilientLLMClient>.Instance, otel);
        return (resilient, factory);
    }

    // ─── Test 1: 10 concurrent CompleteAsync, each response carries its own provider/model ───

    [Fact]
    public async Task Concurrent_CompleteAsync_returns_per_call_provider_model_in_response()
    {
        // Build a fallback chain: p1 → p2 → p3 → p4 → p5
        // Each provider has a unique provider/model in its LlmResponse.
        const int parallelism = 10;
        var cfg = MakeConfig("p1", "p2", "p3", "p4", "p5");

        // Each provider returns its own response; primary succeeds immediately.
        var providerResponses = new Dictionary<string, (string provider, string model)>
        {
            ["p1"] = ("p1", "model-p1"),
            ["p2"] = ("p2", "model-p2"),
            ["p3"] = ("p3", "model-p3"),
            ["p4"] = ("p4", "model-p4"),
            ["p5"] = ("p5", "model-p5")
        };

        // Slow each p1 a bit so concurrent calls genuinely interleave on the writer of ProviderName.
        // Without the fix, with the old code, last-writer-wins would cause response.Provider/Model
        // to come from the wrong concurrent call... BUT actually the old code wrote to
        // this.ProviderName, NOT to resp.Provider. So the response always had the right values.
        // The real fix is: do NOT mutate this.ProviderName at all. To verify that, we add a
        // separate assertion: after concurrent calls, this.ProviderName still reflects the
        // configured primary (p1), not whatever happened to be the "last" successful call.
        var clients = providerResponses.ToDictionary(
            kv => kv.Key,
            kv => (ILLMClient)new StaticLlmClient(kv.Value.provider, kv.Value.model, asyncDelayMs: kv.Key == "p1" ? 50 : 0));

        var (resilient, _) = Build(cfg, clients);

        // Snapshot configured primary name BEFORE concurrent calls — should remain stable.
        var configuredPrimary = resilient.ProviderName;
        Assert.Equal("p1", configuredPrimary);

        var tasks = Enumerable.Range(0, parallelism)
            .Select(_ => resilient.CompleteAsync(Roles.Main, SampleMessages()))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Each response carries the provider/model of the client that actually answered.
        Assert.All(responses, r =>
        {
            var (expectedProvider, expectedModel) = providerResponses["p1"];
            Assert.Equal(expectedProvider, r.Provider);
            Assert.Equal(expectedModel, r.Model);
        });

        // [task_076] ResilientLLMClient.ProviderName still reflects the configured primary,
        // NOT whatever the last call happened to be. This is the property that the
        // race-prone setter was breaking.
        Assert.Equal(configuredPrimary, resilient.ProviderName);
    }

    [Fact]
    public async Task Concurrent_CompleteAsync_with_fallback_each_response_carries_its_own_provider()
    {
        // Force a fallback by making p1 fail; each task should land on a different fallback provider
        // by varying per-call request, but the wrapper uses one chain. So simulate: p1 always fails,
        // p2 succeeds. We then run concurrent calls — the response should consistently carry p2/p2
        // (no per-call write to this.ProviderName leaking from a different call).
        var cfg = MakeConfig("p1", "p2");

        var clients = new Dictionary<string, ILLMClient>
        {
            ["p1"] = new ThrowingLlmClient("p1", "model-p1", new HttpRequestException("503")),
            ["p2"] = new StaticLlmClient("p2", "model-p2", asyncDelayMs: 0)
        };

        var (resilient, _) = Build(cfg, clients);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => resilient.CompleteAsync(Roles.Main, SampleMessages()))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // All responses must come from p2 (since p1 fails after retries).
        Assert.All(responses, r =>
        {
            Assert.Equal("p2", r.Provider);
            Assert.Equal("model-p2", r.Model);
        });

        // Configured primary is p1; the wrapper's ProviderName must still say p1 (no per-call write).
        Assert.Equal("p1", resilient.ProviderName);
    }

    // ─── Test 2: OTel Activity tags per-call contain correct provider/model ───

    [Fact]
    public async Task CompleteAsync_sets_OTel_Activity_tags_with_response_provider_model_per_call()
    {
        var cfg = MakeConfig("p1", "p2");
        using var otel = new RecordingOtelService();
        var clients = new Dictionary<string, ILLMClient>
        {
            ["p1"] = new StaticLlmClient("p1", "model-p1", asyncDelayMs: 0),
            ["p2"] = new StaticLlmClient("p2", "model-p2", asyncDelayMs: 0)
        };

        var (resilient, _) = Build(cfg, clients, otel);

        var resp = await resilient.CompleteAsync(Roles.Main, SampleMessages());

        Assert.Equal("p1", resp.Provider);
        Assert.Equal("model-p1", resp.Model);

        // Find the per-call LLM.p1 activity; its tags should carry p1/model-p1.
        var activity = Assert.Single(otel.Activities, a => a.OperationName == "LLM.p1");
        Assert.Equal("p1", activity.GetTagItem("llm.provider"));
        Assert.Equal("model-p1", activity.GetTagItem("llm.model"));
    }

    // ─── Helpers ───

    private sealed class FakeFactory : ILLMClientFactory
    {
        private readonly IDictionary<string, ILLMClient> _clients;
        public FakeFactory(IDictionary<string, ILLMClient> clients) => _clients = clients;
        public ILLMClient Create(string provider)
        {
            if (_clients.TryGetValue(provider, out var c)) return c;
            throw new InvalidOperationException($"unknown provider: {provider}");
        }
    }

    private sealed class StaticLlmClient : ILLMClient
    {
        private readonly string _provider;
        private readonly string _model;
        private readonly int _delayMs;

        public StaticLlmClient(string provider, string model, int asyncDelayMs = 0)
        {
            _provider = provider;
            _model = model;
            _delayMs = asyncDelayMs;
        }

        public string ProviderName => _provider;
        public string ModelName => _model;

        public async Task<LlmResponse> CompleteAsync(string role, IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
            => await CompleteAsync(messages, ct);

        public async Task<LlmResponse> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct = default)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs, ct);
            return new LlmResponse($"ok-{_provider}", _provider, _model, InputTokens: 1, OutputTokens: 1);
        }

        public async IAsyncEnumerable<string> StreamAsync(string role, IReadOnlyList<ChatTurn> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var s in StreamAsync(messages, ct)) yield return s;
        }

        public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatTurn> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return $"ok-{_provider}";
        }
    }

    private sealed class ThrowingLlmClient : ILLMClient
    {
        private readonly string _provider;
        private readonly string _model;
        private readonly Exception _ex;
        public ThrowingLlmClient(string provider, string model, Exception ex)
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
    /// Recording IOtelService: creates real Activities via a private ActivitySource and
    /// an internal ActivityListener so we can inspect per-call tags.
    /// </summary>
    private sealed class RecordingOtelService : IOtelService, IDisposable
    {
        private readonly ActivitySource _source;
        private readonly ConcurrentQueue<Activity> _activities = new();
        private readonly ActivityListener _listener;

        public RecordingOtelService()
        {
            // Per-instance source with a unique name (based on GUID) to avoid global state
            // collisions when tests run in parallel.
            var name = $"Hercules.Tests.RecordingOtelService.{Guid.NewGuid():N}";
            _source = new ActivitySource(name);
            _listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == _source.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = a => _activities.Enqueue(a)
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentQueue<Activity> Activities => _activities;
        public bool IsEnabled => true;

        public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
            => _source.StartActivity(name, kind);

        public Activity? StartActivity(string name, ActivityContext parent, ActivityKind kind = ActivityKind.Internal)
            => _source.StartActivity(name, kind, parent);

        public void SetTag(Activity? activity, string key, string value) => activity?.SetTag(key, value);
        public void SetTags(Activity? activity, params KeyValuePair<string, object?>[] tags)
        {
            if (activity is null) return;
            foreach (var t in tags) activity.SetTag(t.Key, t.Value);
        }
        public void AddEvent(Activity? activity, string name, params KeyValuePair<string, object?>[] tags) { }
        public void SetErrorStatus(Activity? activity, string description) { }
        public void StopActivity(Activity? activity, ActivityStatusCode status = ActivityStatusCode.Ok) => activity?.Stop();

        public void Dispose() => _listener.Dispose();
    }
}
