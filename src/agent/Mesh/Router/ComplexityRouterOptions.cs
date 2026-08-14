namespace Hercules.Mesh.Router;

/// <summary>
///     Configuration for the complexity-based router (task_044).
///     Controls complexity thresholds, cost budgets, and fan-out limits.
/// </summary>
public sealed class ComplexityRouterOptions
{
    /// <summary>
    ///     Enable the complexity router. When disabled, always returns <see cref="ExecutionPath.LocalDirect"/>.
    ///     Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Maximum payload size in bytes for <see cref="ComplexityLevel.Simple"/> classification.
    ///     Default: 512 bytes.
    /// </summary>
    public int SimpleMaxPayloadBytes { get; set; } = 512;

    /// <summary>
    ///     Maximum estimated cost in USD for <see cref="ComplexityLevel.Simple"/> classification.
    ///     Default: $0.001 (0.1 cents).
    /// </summary>
    public decimal SimpleMaxCostUsd { get; set; } = 0.001m;

    /// <summary>
    ///     Maximum payload size in bytes for <see cref="ComplexityLevel.Moderate"/> classification.
    ///     Default: 4 KB.
    /// </summary>
    public int ModerateMaxPayloadBytes { get; set; } = 4_096;

    /// <summary>
    ///     Maximum estimated cost in USD for <see cref="ComplexityLevel.Moderate"/> classification.
    ///     Default: $0.10.
    /// </summary>
    public decimal ModerateMaxCostUsd { get; set; } = 0.10m;

    /// <summary>
    ///     Maximum number of peers to fan out to in a single fan-out request.
    ///     Default: 5.
    /// </summary>
    public int MaxPeersForFanOut { get; set; } = 5;

    /// <summary>
    ///     Enable fan-out routing. When false, fan-out requests are downgraded to <see cref="ExecutionPath.SinglePeer"/>.
    ///     Default: true.
    /// </summary>
    public bool EnableFanOut { get; set; } = true;

    /// <summary>
    ///     Cost threshold above which <see cref="ExecutionPath.LocalSmallModel"/> is used instead of direct skill.
    ///     Default: $0.0005.
    /// </summary>
    public decimal SmallModelCostThresholdUsd { get; set; } = 0.0005m;

    /// <summary>
    ///     Cost threshold above which <see cref="ExecutionPath.LocalLargeModel"/> is preferred over small model.
    ///     Default: $0.01.
    /// </summary>
    public decimal LargeModelCostThresholdUsd { get; set; } = 0.01m;

    /// <summary>
    ///     Maximum per-request budget in USD. Requests exceeding this budget are blocked or downgraded.
    ///     Default: $1.00.
    /// </summary>
    public decimal MaxBudgetPerRequestUsd { get; set; } = 1.00m;

    /// <summary>
    ///     Default suggested retry count for peer calls.
    ///     Default: 2.
    /// </summary>
    public int DefaultPeerRetryCount { get; set; } = 2;

    /// <summary>
    ///     Maximum retry count for simple local tasks.
    ///     Default: 0.
    /// </summary>
    public int MaxRetriesForSimple { get; set; } = 0;

    /// <summary>
    ///     Maximum retry count for moderate tasks.
    ///     Default: 1.
    /// </summary>
    public int MaxRetriesForModerate { get; set; } = 1;

    /// <summary>
    ///     Maximum retry count for complex tasks.
    ///     Default: 2.
    /// </summary>
    public int MaxRetriesForComplex { get; set; } = 2;
}
