using Hercules.Mesh.Escalation;
using Hercules.Tasks;

namespace Hercules.Storage;

/// <summary>
///     Storage-agnostic session store contract (task_103).
///     Extracts the persistence surface previously hard-coded in
///     <see cref="SqliteSessionStore"/> so the same code can target a shared
///     PostgreSQL backend or remain on a local SQLite file. The default
///     implementation stays <see cref="SqliteSessionStore"/> for backward
///     compatibility; <see cref="PostgresSessionStore"/> provides the
///     optional Npgsql-backed variant.
/// </summary>
/// <remarks>
///     Sync overloads (e.g. <c>LogInteraction</c>) are convenience wrappers
///     over their async siblings and intentionally not part of this interface
///     — callers that need to plug in a different backend should switch to
///     the async methods. The shared <c>SqliteConnection</c> exposed by
///     <c>SqliteSessionStore.Connection</c> is a SQLite-specific escape hatch
///     used by <c>SkillQualityStore</c> (task_029) and is not part of the
///     contract on purpose.
/// </remarks>
public interface ISessionStore
{
    // ---- Lifecycle ----

    /// <summary>True when the underlying connection is reachable and the schema is initialised.</summary>
    bool IsHealthy();

    // ---- Sessions ----

    Task StartSessionAsync(string sessionId, CancellationToken ct = default);
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);

    // ---- Interactions ----

    Task LogInteractionAsync(InteractionLog log, CancellationToken ct = default);
    Task<int> IncrementRequestCountAsync(string normalizedInput, CancellationToken ct = default);
    Task ResetRequestCountAsync(string normalizedInput, CancellationToken ct = default);
    Task<List<InteractionLog>> GetLowConfidenceAsync(string sessionId, CancellationToken ct = default);
    Task<List<InteractionLog>> GetSessionInteractionsAsync(string sessionId, int limit = 500, CancellationToken ct = default);
    Task<(int Skill, int Direct)> GetModeStatsAsync(string sessionId, CancellationToken ct = default);
    Task<(int Skill, int Direct)> GetGlobalModeStatsAsync(CancellationToken ct = default);
    Task<double> GetGlobalSuccessRateAsync(CancellationToken ct = default);
    Task<List<(string Date, int Total, int Skill, int Direct)>> GetDailyStatsAsync(int days = 14, CancellationToken ct = default);
    Task<int> GetTotalInteractionsAsync(CancellationToken ct = default);

    // ---- Sandbox ----

    Task LogSandboxExecutionAsync(
        string sessionId,
        string codeHash,
        string language,
        int? exitCode,
        string status,
        long durationMs,
        IReadOnlyList<string> blockedPatterns,
        CancellationToken ct = default);

    Task<List<SandboxExecutionLog>> GetRecentSandboxExecutionsAsync(int limit = 20, CancellationToken ct = default);
    Task<double> GetRecentSandboxFailureRateAsync(int window = 5, CancellationToken ct = default);

    // ---- Budget ----

    Task LogBudgetEntryAsync(
        string sessionId,
        string provider,
        string model,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        CancellationToken ct = default);

    Task<BudgetSummary> GetBudgetSummaryAsync(DateTime? since = null, CancellationToken ct = default);
    Task<List<(string Date, int Calls, decimal CostUsd)>> GetDailyBudgetAsync(int days = 30, CancellationToken ct = default);

    // ---- Audit ----

    Task LogAuditAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        CancellationToken ct = default);

    Task LogAuditExAsync(
        string actor,
        string action,
        string? target,
        string? details,
        string? sessionId,
        string? requestId,
        string? toolName,
        string? policyDecision,
        string? permissionUsed,
        string? result,
        string? payloadHash,
        CancellationToken ct = default);

    Task<List<AuditLogEntry>> GetAuditLogAsync(int limit = 100, CancellationToken ct = default);
    Task<AuditLogStats> GetAuditLogStatsAsync(
        string action,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);

    Task<List<AuditLogEntry>> GetAuditLogByTargetAsync(string target, int limit = 50, CancellationToken ct = default);
    Task<List<AuditLogEntry>> GetAuditLogQueryAsync(AuditLogQuery query, CancellationToken ct = default);

    // ---- Skill evaluation history ----

    Task SaveEvaluationResultAsync(
        string skillId,
        double score,
        bool passed,
        string? testResults,
        string evaluatorProvider,
        CancellationToken ct = default);

    Task<List<SkillEvaluationRecord>> GetSkillEvaluationHistoryAsync(string skillId, int limit = 20, CancellationToken ct = default);

    // ---- Durable task state ----

    Task SaveTaskStateAsync(
        string taskId,
        string status,
        string? result,
        string? error,
        string? metadata,
        CancellationToken ct = default);

    Task<TaskState?> LoadTaskStateAsync(string taskId, CancellationToken ct = default);
    Task<List<TaskState>> ListTaskStatesAsync(string? statusFilter = null, int limit = 100, CancellationToken ct = default);

    // ---- Approval gates ----

    Task SaveApprovalRequestAsync(ApprovalRequest req, CancellationToken ct = default);
    Task<List<ApprovalRequest>> GetPendingApprovalsAsync(string? sessionId = null, CancellationToken ct = default);
    Task<List<ApprovalRequest>> GetApprovalRequestsAsync(string? sessionId = null, int limit = 100, CancellationToken ct = default);
    Task UpdateApprovalStatusAsync(string id, string status, DateTime? approvedAt = null, DateTime? deniedAt = null, CancellationToken ct = default);
    Task ExpireOldApprovalsAsync(int ttlMinutes, CancellationToken ct = default);

    // ---- Durable tasks (task_018) ----

    Task SaveDurableTaskAsync(DurableTask task, CancellationToken ct = default);
    Task<DurableTask?> LoadDurableTaskAsync(string taskId, CancellationToken ct = default);
    Task<List<DurableTask>> ListDurableTasksAsync(DurableTaskStatus? statusFilter = null, int limit = 100, CancellationToken ct = default);
    Task DeleteDurableTaskAsync(string taskId, CancellationToken ct = default);

    // ---- Checkpoints ----

    Task InitCheckpointSchemaAsync(CancellationToken ct = default);
    Task SaveCheckpointAsync(TaskCheckpoint ckpt, CancellationToken ct = default);
    Task<List<TaskCheckpoint>> ListCheckpointsAsync(string taskId, CancellationToken ct = default);
    Task CleanupOldCheckpointsAsync(int retentionDays, CancellationToken ct = default);

    // ---- Escalations ----

    Task SaveEscalationAsync(EscalationResult e, CancellationToken ct = default);
    Task UpdateEscalationStatusAsync(string escalationId, string status, string? resolvedBy = null, CancellationToken ct = default);
    Task ExpireOldEscalationsAsync(int ttlMinutes, CancellationToken ct = default);
}
