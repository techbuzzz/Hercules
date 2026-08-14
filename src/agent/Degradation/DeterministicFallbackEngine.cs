using Hercules.LLM;
using Microsoft.Extensions.Logging;

namespace Hercules.Degradation;

/// <summary>
///     Deterministic fallback engine for rule-based decisions when cloud services are unavailable.
///     Task 061: Local-first degradation.
/// </summary>
public sealed class DeterministicFallbackEngine
{
    private readonly DegradationConfig _config;
    private readonly ILLMClient? _llmClient;
    private readonly ILogger<DeterministicFallbackEngine> _log;

    public DeterministicFallbackEngine(
        DegradationConfig config,
        ILLMClient? llmClient,
        ILogger<DeterministicFallbackEngine> log)
    {
        _config = config;
        _llmClient = llmClient;
        _log = log;
    }

    /// <summary>
    ///     Determines the appropriate fallback strategy based on current degradation state.
    /// </summary>
    public FallbackStrategy GetFallbackStrategy(DegradationState state)
    {
        if (!_config.Fallback.EnableDeterministicFallback)
        {
            return FallbackStrategy.None;
        }

        return state.Mode switch
        {
            DegradationMode.Full => FallbackStrategy.None,
            DegradationMode.Degraded => GetDegradedStrategy(state),
            DegradationMode.Offline => GetOfflineStrategy(state),
            _ => FallbackStrategy.None
        };
    }

    private FallbackStrategy GetDegradedStrategy(DegradationState state)
    {
        var llmUnhealthy = state.ServiceStatuses
            .Any(s => s.ServiceName == "llm" && s.Health == ServiceHealth.Unhealthy);

        var networkUnhealthy = state.ServiceStatuses
            .Any(s => s.ServiceName == "network" && s.Health == ServiceHealth.Unhealthy);

        if (llmUnhealthy && _config.Fallback.UseReducedCapabilityModels)
        {
            _log.LogInformation("Fallback strategy: ReducedCapabilityModel (LLM unhealthy)");
            return FallbackStrategy.ReducedCapabilityModel;
        }

        if (networkUnhealthy && _config.Fallback.QueueWorkWhenOffline)
        {
            _log.LogInformation("Fallback strategy: QueueWork (network unstable)");
            return FallbackStrategy.QueueWork;
        }

        if (_config.Fallback.UseLocalSkills)
        {
            _log.LogInformation("Fallback strategy: LocalSkillsOnly");
            return FallbackStrategy.LocalSkillsOnly;
        }

        return FallbackStrategy.None;
    }

    private FallbackStrategy GetOfflineStrategy(DegradationState state)
    {
        if (_config.Fallback.QueueWorkWhenOffline)
        {
            _log.LogInformation("Fallback strategy: QueueWork (offline mode)");
            return FallbackStrategy.QueueWork;
        }

        if (_config.Fallback.UseLocalSkills)
        {
            _log.LogInformation("Fallback strategy: LocalSkillsOnly (offline)");
            return FallbackStrategy.LocalSkillsOnly;
        }

        return FallbackStrategy.None;
    }

    /// <summary>
    ///     Gets the best available LLM provider for the given strategy.
    /// </summary>
    public Task<string?> GetBestAvailableLlmProviderAsync(
        FallbackStrategy strategy,
        CancellationToken ct = default)
    {
        if (strategy == FallbackStrategy.None)
        {
            return Task.FromResult<string?>(null);
        }

        if (strategy == FallbackStrategy.ReducedCapabilityModel)
        {
            var reducedModels = _config.Fallback.ReducedCapabilityModels;
            if (reducedModels.Count > 0)
            {
                _log.LogInformation("Selected reduced capability model: {Model}", reducedModels[0]);
                return Task.FromResult<string?>(reducedModels[0]);
            }
        }

        // For offline mode, prefer local models
        if (strategy == FallbackStrategy.QueueWork || strategy == FallbackStrategy.LocalSkillsOnly)
        {
            var reducedModels = _config.Fallback.ReducedCapabilityModels;
            var localModel = reducedModels.LastOrDefault(m => m.Contains("llama", StringComparison.OrdinalIgnoreCase));
            if (localModel != null)
            {
                return Task.FromResult<string?>(localModel);
            }
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    ///     Determines if new delegations should be accepted based on current state.
    /// </summary>
    public bool ShouldAcceptDelegations(DegradationState state)
    {
        if (state.Mode == DegradationMode.Full)
        {
            return true;
        }

        if (state.Mode == DegradationMode.Degraded)
        {
            return _config.Fallback.AllowDelegationsInDegradedMode;
        }

        // Offline mode
        return !_config.Fallback.RefuseDelegationsWhenOffline;
    }

    /// <summary>
    ///     Gets the reason why delegations might be refused.
    /// </summary>
    public string? GetDelegationRefusalReason(DegradationState state)
    {
        if (ShouldAcceptDelegations(state))
        {
            return null;
        }

        return state.Mode switch
        {
            DegradationMode.Degraded => "Agent is in degraded mode and policy prevents accepting new delegations",
            DegradationMode.Offline => "Agent is offline and not accepting new delegations",
            _ => "Agent is not accepting new delegations"
        };
    }
}

/// <summary>
///     Fallback strategy types.
/// </summary>
public enum FallbackStrategy
{
    /// <summary>No fallback needed - full capability.</summary>
    None,

    /// <summary>Use local skills only.</summary>
    LocalSkillsOnly,

    /// <summary>Use reduced-capability LLM models.</summary>
    ReducedCapabilityModel,

    /// <summary>Queue work for later processing.</summary>
    QueueWork
}
