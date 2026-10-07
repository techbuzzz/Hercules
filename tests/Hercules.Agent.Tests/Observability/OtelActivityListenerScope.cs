using System.Diagnostics;
using Hercules.Observability;
using Xunit;

namespace Hercules.Agent.Tests.Observability;

/// <summary>
///     R3b: <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns
///     <c>null</c> unless at least one <see cref="ActivityListener"/> is registered.
///     <c>OtelSetup.Source</c> is a process-wide static, so every test that exercises
///     the *enabled* path needs a listener or it silently gets <c>null</c> — which is
///     exactly what made five OtelServiceTests red on a clean checkout.
/// </summary>
/// <remarks>
///     The listener is scoped to a single test and restricted to the Hercules source so
///     unrelated tests that depend on other <see cref="ActivitySource"/> instances (and
///     therefore on them returning null) keep their current behaviour. Pair it with
///     <see cref="OpenTelemetryCollection"/> so no two Otel tests register or dispose a
///     listener concurrently.
/// </remarks>
public sealed class OtelActivityListenerScope : IDisposable
{
    private readonly ActivityListener _listener;

    public OtelActivityListenerScope()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OtelSetup.ServiceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();
}

/// <summary>
///     Serialises the OpenTelemetry tests against each other. The listener registry is
///     process-global, so concurrent registration/disposal is the flakiness source that
///     made the failing set differ between otherwise identical runs.
/// </summary>
[CollectionDefinition(nameof(OpenTelemetryCollection), DisableParallelization = true)]
public sealed class OpenTelemetryCollection
{
}