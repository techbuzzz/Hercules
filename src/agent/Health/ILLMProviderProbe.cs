using Hercules.LLM;

namespace Hercules.Health;

/// <summary>
/// task_079: minimal abstraction over <see cref="ProviderHealthChecker"/>
/// so <see cref="LlmHealthCheck"/> can be unit-tested without mocking
/// the sealed concrete class. Real production code resolves to
/// <see cref="ProviderHealthChecker"/> via the DI registration below.
/// </summary>
public interface ILLMProviderProbe
{
    Task<ProviderHealthResult> CheckAsync(string provider, CancellationToken ct = default);
}

/// <summary>
/// task_079: default adapter that delegates to <see cref="ProviderHealthChecker"/>.
/// </summary>
public sealed class ProviderHealthCheckerAdapter : ILLMProviderProbe
{
    private readonly ProviderHealthChecker _checker;

    public ProviderHealthCheckerAdapter(ProviderHealthChecker checker)
    {
        _checker = checker;
    }

    public Task<ProviderHealthResult> CheckAsync(string provider, CancellationToken ct = default)
        => _checker.CheckAsync(provider, ct);
}
