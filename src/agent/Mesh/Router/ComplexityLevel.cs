namespace Hercules.Mesh.Router;

/// <summary>
///     Complexity level of an incoming intent, used to select the appropriate execution path.
///     Determined by <see cref="IComplexityClassifier"/> using rule-based heuristics.
///     Specification: docs/ROADMAP-RU.md Phase 4 task_044.
/// </summary>
public enum ComplexityLevel
{
    /// <summary>
    ///     Simple request: low payload, few/no tools, known pattern.
    ///     Route via direct local skill or small model.
    /// </summary>
    Simple = 0,

    /// <summary>
    ///     Moderate request: larger payload or multiple tools, reasonable cost.
    ///     Route via small or large model; consider single peer if local skills insufficient.
    /// </summary>
    Moderate = 1,

    /// <summary>
    ///     Complex request: large payload, safety-sensitive tools, high cost, multi-step reasoning.
    ///     Route via large model or fan-out to multiple trusted peers.
    /// </summary>
    Complex = 2
}
