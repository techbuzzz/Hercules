// Клиент Hercules Web API.
// База и ключ берутся из переменных окружения Astro (PUBLIC_*),
// со значениями по умолчанию для локального запуска.

export const API_BASE =
  (import.meta.env.PUBLIC_API_BASE as string) || "http://localhost:8421";
export const API_KEY =
  (import.meta.env.PUBLIC_API_KEY as string) || "dev-local-key";

function headers(json = true): Record<string, string> {
  const h: Record<string, string> = { "X-Api-Key": API_KEY };
  if (json) h["Content-Type"] = "application/json";
  return h;
}

async function handle<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try {
      const body = await res.json();
      if (body?.error) msg = body.error;
    } catch { /* игнор */ }
    throw new Error(msg);
  }
  return res.json() as Promise<T>;
}

// ---- Типы ----
export interface SkillDto {
  id: string;
  name: string;
  description: string;
  triggers: string[];
  version: number;
  successRate: number;
  totalUses: number;
  createdAt: string;
}

export interface SkillDetailDto {
  meta: SkillDto;
  descriptionMarkdown: string;
  prompt: string;
}

export interface ChatResponseDto {
  answer: string;
  mode: string;
  confidence: string;
  provider: string;
  skill: SkillDto | null;
  proposeSkillForInput: string | null;
  proposeImproveSkillId: string | null;
  proposeImproveSkillName: string | null;
}

export interface SkillEvaluationResultDto {
  skillId: string;
  score: number;
  passed: boolean;
  perTestResults: { testName: string; passed: boolean; details: string }[];
  evaluatedAt: string;
}

export interface DeprecatedSkillsDto {
  count: number;
  skills: SkillDto[];
}

export interface AuditEntryDto {
  id: number;
  actor: string;
  action: string;
  target: string;
  details: string;
  sessionId: string;
  createdAt: string;
}

export interface AuditLogDto {
  count: number;
  entries: AuditEntryDto[];
}

export interface BudgetDailyDto {
  date: string;
  calls: number;
  costUsd: number;
}

export interface BudgetSummaryDto {
  totalCalls: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCostUsd: number;
}

export interface BudgetDto {
  periodDays: number;
  summary: BudgetSummaryDto;
  daily: BudgetDailyDto[];
}

export interface BudgetMonthlyDto {
  month: string;
  totalCalls: number;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCostUsd: number;
  limitUsd: number | null;
  isOverBudget: boolean;
}

export interface StatsDto {
  totalInteractions: number;
  skillBased: number;
  direct: number;
  successRate: number;
  totalSkills: number;
  byDay: { date: string; total: number; skill: number; direct: number }[];
}

// ---- Методы ----
export interface ConfigDto {
  config: Record<string, unknown>;
  source: string;
}

export const api = {
  async chat(message: string): Promise<ChatResponseDto> {
    const res = await fetch(`${API_BASE}/api/chat`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ message }),
    });
    return handle<ChatResponseDto>(res);
  },

  async listSkills(): Promise<SkillDto[]> {
    return handle<SkillDto[]>(await fetch(`${API_BASE}/api/skills`, { headers: headers(false) }));
  },

  async getSkill(id: string): Promise<SkillDetailDto> {
    return handle<SkillDetailDto>(await fetch(`${API_BASE}/api/skills/${id}`, { headers: headers(false) }));
  },

  async createSkill(data: { name: string; trigger: string; prompt: string; description?: string }): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(data),
    });
    return handle<SkillDto>(res);
  },

  async updateSkill(id: string, data: { triggers?: string[]; prompt?: string; description?: string }): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills/${id}`, {
      method: "PUT",
      headers: headers(),
      body: JSON.stringify(data),
    });
    return handle<SkillDto>(res);
  },

  async getProfile(): Promise<{ content: string }> {
    return handle<{ content: string }>(await fetch(`${API_BASE}/api/memory/profile`, { headers: headers(false) }));
  },

  async updateProfile(content: string): Promise<{ status: string; content: string }> {
    const res = await fetch(`${API_BASE}/api/memory/profile`, {
      method: "PUT",
      headers: headers(),
      body: JSON.stringify({ content }),
    });
    return handle<{ status: string; content: string }>(res);
  },

  async resetMemory(): Promise<{ status: string }> {
    return handle<{ status: string }>(await fetch(`${API_BASE}/api/memory/reset`, { method: "POST", headers: headers() }));
  },

  async stats(): Promise<StatsDto> {
    return handle<StatsDto>(await fetch(`${API_BASE}/api/stats`, { headers: headers(false) }));
  },

  async reflect(): Promise<{ markdown: string; file: string }> {
    return handle<{ markdown: string; file: string }>(await fetch(`${API_BASE}/api/reflect`, { headers: headers(false) }));
  },

  async getConfig(): Promise<ConfigDto> {
    return handle<ConfigDto>(await fetch(`${API_BASE}/api/config`, { headers: headers(false) }));
  },

  async updateConfig(body: unknown): Promise<ConfigDto> {
    const res = await fetch(`${API_BASE}/api/config`, {
      method: "PUT",
      headers: headers(),
      body: JSON.stringify(body),
    });
    return handle<ConfigDto>(res);
  },

  async patchConfig(patch: Record<string, unknown>): Promise<ConfigDto> {
    const res = await fetch(`${API_BASE}/api/config`, {
      method: "PATCH",
      headers: headers(),
      body: JSON.stringify(patch),
    });
    return handle<ConfigDto>(res);
  },

  // ---- Skill lifecycle ----

  async evaluateSkill(id: string): Promise<SkillEvaluationResultDto> {
    const res = await fetch(`${API_BASE}/api/skills/${encodeURIComponent(id)}/evaluate`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<SkillEvaluationResultDto>(res);
  },

  async deprecateSkill(id: string, reason: string): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills/${encodeURIComponent(id)}/deprecate`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ reason }),
    });
    return handle<SkillDto>(res);
  },

  async rollbackSkill(id: string): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills/${encodeURIComponent(id)}/rollback`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<SkillDto>(res);
  },

  async undeprecateSkill(id: string): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills/${encodeURIComponent(id)}/undeprecate`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<SkillDto>(res);
  },

  async improveSkill(id: string): Promise<SkillDto> {
    const res = await fetch(`${API_BASE}/api/skills/${encodeURIComponent(id)}/improve`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<SkillDto>(res);
  },

  async getDeprecatedSkills(): Promise<DeprecatedSkillsDto> {
    const res = await fetch(`${API_BASE}/api/skills/deprecated`, { headers: headers(false) });
    return handle<DeprecatedSkillsDto>(res);
  },

  // ---- Budget ----

  async getBudget(days = 30): Promise<BudgetDto> {
    const res = await fetch(`${API_BASE}/api/budget?days=${days}`, { headers: headers(false) });
    return handle<BudgetDto>(res);
  },

  async getBudgetMonthly(limitUsd?: number): Promise<BudgetMonthlyDto> {
    const url = limitUsd != null ? `${API_BASE}/api/budget/monthly?limit=${limitUsd}` : `${API_BASE}/api/budget/monthly`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<BudgetMonthlyDto>(res);
  },

  // ---- Audit ----

  async getAudit(limit = 100): Promise<AuditLogDto> {
    const res = await fetch(`${API_BASE}/api/audit?limit=${limit}`, { headers: headers(false) });
    return handle<AuditLogDto>(res);
  },

  async getAuditByTarget(target: string, limit = 50): Promise<AuditLogDto> {
    const res = await fetch(`${API_BASE}/api/audit/${encodeURIComponent(target)}?limit=${limit}`, { headers: headers(false) });
    return handle<AuditLogDto>(res);
  },

  // ---- Mesh Router (Phase 4 task_043) ----

  async getMeshRouterRoutes(capability: string, maxCostUsd?: number): Promise<MeshRouterRoutesDto> {
    const url = maxCostUsd != null
      ? `${API_BASE}/api/mesh/router/routes?capability=${encodeURIComponent(capability)}&maxCostUsd=${maxCostUsd}`
      : `${API_BASE}/api/mesh/router/routes?capability=${encodeURIComponent(capability)}`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<MeshRouterRoutesDto>(res);
  },

  async getMeshRouterHealth(): Promise<MeshRouterHealthDto> {
    const res = await fetch(`${API_BASE}/api/mesh/router/health`, { headers: headers(false) });
    return handle<MeshRouterHealthDto>(res);
  },

  async getMeshCircuits(): Promise<Record<string, string>> {
    const res = await fetch(`${API_BASE}/api/mesh/circuits`, { headers: headers(false) });
    return handle<Record<string, string>>(res);
  },

  // ---- Mesh Dashboard (Phase 5 task_053) ----

  async getMeshDashboard(): Promise<MeshDashboardDto> {
    const res = await fetch(`${API_BASE}/api/mesh/dashboard`, { headers: headers(false) });
    return handle<MeshDashboardDto>(res);
  },

  async getMeshTopology(): Promise<MeshTopologyDto> {
    const res = await fetch(`${API_BASE}/api/mesh/topology`, { headers: headers(false) });
    return handle<MeshTopologyDto>(res);
  },

  async getMeshHealth(): Promise<MeshHealthDto> {
    const res = await fetch(`${API_BASE}/api/mesh/health`, { headers: headers(false) });
    return handle<MeshHealthDto>(res);
  },

  async getMeshDenials(limit = 50): Promise<MeshPolicyDenialsDto> {
    const res = await fetch(`${API_BASE}/api/mesh/denials?limit=${limit}`, { headers: headers(false) });
    return handle<MeshPolicyDenialsDto>(res);
  },

  async getMeshSkillHeatmap(): Promise<MeshSkillHeatmapDto> {
    const res = await fetch(`${API_BASE}/api/mesh/skills/heatmap`, { headers: headers(false) });
    return handle<MeshSkillHeatmapDto>(res);
  },

  async getMeshEvalSummary(): Promise<MeshEvalSummaryDto> {
    const res = await fetch(`${API_BASE}/api/mesh/eval/summary`, { headers: headers(false) });
    return handle<MeshEvalSummaryDto>(res);
  },

  // ---- Mesh profiles & backend health (Phase 5 task_070 / Phase 7 task_092) ----

  /**
   * List registered mesh profile names and the active profile.
   * Wire format: `{count, profiles: string[], activeProfile: string}`.
   */
  async listMeshProfiles(): Promise<MeshProfileListDto> {
    const res = await fetch(`${API_BASE}/api/mesh/profiles`, { headers: headers(false) });
    return handle<MeshProfileListDto>(res);
  },

  /**
   * Fetch a single mesh profile definition (name, description, profile kind,
   * backends{role → config}, constraints, degradationPolicy).
   */
  async getMeshProfile(name: string): Promise<MeshProfileDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/profiles/${encodeURIComponent(name)}`,
      { headers: headers(false) },
    );
    return handle<MeshProfileDto>(res);
  },

  /**
   * Effective backend configurations for a profile (bus/queue/stateStore) —
   * honours `Enabled` and falls back to in-process defaults. Wire format:
   * `{profile, backends: {role: {role, kind, enabled, ...}}}`.
   */
  async getMeshProfileBackends(name: string): Promise<MeshProfileBackendsDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/profiles/${encodeURIComponent(name)}/backends`,
      { headers: headers(false) },
    );
    return handle<MeshProfileBackendsDto>(res);
  },

  /**
   * Live health status for all mesh backends plus the overall rollup.
   * Wire format: `{overall: "Healthy"|"Degraded"|"Unavailable"|"Unknown",
   * backends: BackendStatusDto[]}`.
   */
  async getAllBackendsStatus(): Promise<MeshBackendStatusListDto> {
    const res = await fetch(`${API_BASE}/api/mesh/backend-status`, { headers: headers(false) });
    return handle<MeshBackendStatusListDto>(res);
  },

  /**
   * Live health status for a specific backend role ("bus" | "queue" | "stateStore").
   * Backend returns 404 when the role is not present in the active profile.
   */
  async getBackendStatus(role: string): Promise<BackendStatusDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/backend-status/${encodeURIComponent(role)}`,
      { headers: headers(false) },
    );
    return handle<BackendStatusDto>(res);
  },

  // ---- Mesh observability counters / traces / logs (Phase 7 task_093) ----

  /**
   * Mesh observability status (config + counters since process start).
   * Wire format: `{enabled, config{...}, counters:{from, to, routingDecision,
   * retryAttempt, circuitBreakerStateChange, delegation, meshBackendHealth,
   * byCapability Record, byPeer Record}}`.
   */
  async getMeshObservabilityStatus(): Promise<MeshObservabilityStatusDto> {
    const res = await fetch(`${API_BASE}/api/mesh/observability/status`, { headers: headers(false) });
    return handle<MeshObservabilityStatusDto>(res);
  },

  /**
   * Counters snapshot only (no config payload). Same shape as
   * `MeshObservabilityStatusDto.counters` plus from/to.
   */
  async getMeshObservabilityCounters(): Promise<MeshObservabilityCountersDto> {
    const res = await fetch(`${API_BASE}/api/mesh/observability/counters`, { headers: headers(false) });
    return handle<MeshObservabilityCountersDto>(res);
  },

  /**
   * Recent completed traces (newest first). Default `limit` is the backend's
   * default cap (100). TraceSummaryDto carries the trace id, root operation
   * name, start timestamp, duration, status and span count.
   */
  async getRecentTraces(limit = 50): Promise<MeshTracesResponseDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/observability/traces?limit=${encodeURIComponent(limit)}`,
      { headers: headers(false) },
    );
    return handle<MeshTracesResponseDto>(res);
  },

  /**
   * Recent log entries (newest first). `level` is a min-level filter
   * ("trace" | "debug" | "info" | "warning" | "error" | "critical").
   * Unknown levels are returned as-is.
   */
  async getRecentLogs(limit = 100, level?: string): Promise<MeshLogsResponseDto> {
    const url = level
      ? `${API_BASE}/api/mesh/observability/logs?limit=${encodeURIComponent(limit)}&level=${encodeURIComponent(level)}`
      : `${API_BASE}/api/mesh/observability/logs?limit=${encodeURIComponent(limit)}`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<MeshLogsResponseDto>(res);
  },

  // ---- A2A Agent Card (Phase 7 task_088) ----

  async getAgentCard(): Promise<AgentCardDto> {
    const res = await fetch(`${API_BASE}/api/a2a/agent-card`, { headers: headers(false) });
    return handle<AgentCardDto>(res);
  },

  async getRemoteAgentCard(url: string): Promise<AgentCardDto> {
    const res = await fetch(
      `${API_BASE}/api/a2a/agent-card/from?url=${encodeURIComponent(url)}`,
      { headers: headers(false) },
    );
    return handle<AgentCardDto>(res);
  },

  async discoverAgentCards(urls: string[]): Promise<DiscoverResultDto> {
    const res = await fetch(`${API_BASE}/api/a2a/discover`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(urls),
    });
    return handle<DiscoverResultDto>(res);
  },

  async publishAgentCard(): Promise<{ status: string; path?: string; message?: string }> {
    const res = await fetch(`${API_BASE}/api/a2a/agent-card/publish`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<{ status: string; path?: string; message?: string }>(res);
  },

  // ---- Capability Registry (Phase 3 task_034 / Phase 7 task_089) ----

  async listMeshAgents(): Promise<MeshAgentEntryDto[]> {
    const res = await fetch(`${API_BASE}/api/mesh/agents`, { headers: headers(false) });
    // Backend wraps payload in { count, agents: RegistryAgentFullEntry[] }.
    const wrapped = await handle<{ count: number; agents: MeshAgentEntryDto[] }>(res);
    return wrapped.agents ?? [];
  },

  async getMeshAgent(id: string): Promise<MeshAgentEntryDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/agents/${encodeURIComponent(id)}`,
      { headers: headers(false) },
    );
    return handle<MeshAgentEntryDto>(res);
  },

  async getMeshAgentHealth(id: string): Promise<MeshAgentHealthDto> {
    const res = await fetch(
      `${API_BASE}/api/mesh/agents/${encodeURIComponent(id)}/health`,
      { headers: headers(false) },
    );
    return handle<MeshAgentHealthDto>(res);
  },

  /**
   * List capabilities of a specific agent. If `agentId` is omitted the
   * backend returns the lightweight agent list — use that as a "registry
   * snapshot" and stay on the full list otherwise.
   */
  async listMeshCapabilities(agentId: string): Promise<MeshCapabilityDto[]> {
    const res = await fetch(
      `${API_BASE}/api/mesh/capabilities?agentId=${encodeURIComponent(agentId)}`,
      { headers: headers(false) },
    );
    return handle<MeshCapabilityDto[]>(res);
  },

  /** Server-side semantic search across capability phrase_receivers. */
  async searchMeshByCapability(phrase: string): Promise<MeshAgentLightDto[]> {
    const res = await fetch(
      `${API_BASE}/api/mesh/capabilities/search?phrase=${encodeURIComponent(phrase)}`,
      { headers: headers(false) },
    );
    return handle<MeshAgentLightDto[]>(res);
  },

  async touchMeshAgent(id: string): Promise<void> {
    const res = await fetch(
      `${API_BASE}/api/mesh/agents/${encodeURIComponent(id)}/touch`,
      { method: "POST", headers: headers(false) },
    );
    await handle<{ status: string; agentId: string }>(res);
  },

  async removeMeshAgent(id: string): Promise<void> {
    const res = await fetch(
      `${API_BASE}/api/mesh/agents/${encodeURIComponent(id)}`,
      { method: "DELETE", headers: headers(false) },
    );
    await handle<{ status: string; agentId: string }>(res);
  },

  // ---- Trust admission policy (Phase 3 task_040 / Phase 7 task_091) ----

  /** Get the current trust admission policy configuration and effective mode. */
  async getPolicyStatus(): Promise<TrustAdmissionPolicyStatusDto> {
    const res = await fetch(`${API_BASE}/api/mesh/policy/status`, { headers: headers(false) });
    return handle<TrustAdmissionPolicyStatusDto>(res);
  },

  /**
   * Evaluate a request against the current trust admission policy without
   * actually sending the intent. The backend returns whether the request
   * would be allowed, the reason and code for a denial, and the effective
   * mode (Enforce/DryRun/Disabled) at the time of evaluation.
   */
  async dryRunPolicy(req: TrustAdmissionDryRunRequestDto): Promise<TrustAdmissionDryRunResultDto> {
    const res = await fetch(`${API_BASE}/api/mesh/policy/dry-run`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(req),
    });
    return handle<TrustAdmissionDryRunResultDto>(res);
  },

  // ---- Discovery mechanisms (Phase 3 task_038 / Phase 7 task_090) ----

  /**
   * List all registered discovery sources (static / registry / mdns) with
   * their enabled state. Backend does not expose per-source `lastRunAt` or
   * `lastError`; the `DiscoveryService` records them server-side but does
   * not surface them through the controller.
   */
  async listDiscoverySources(): Promise<DiscoverySourceDto[]> {
    const res = await fetch(`${API_BASE}/api/mesh/discovery/sources`, { headers: headers(false) });
    const wrapped = await handle<{ count: number; sources: DiscoverySourceDto[] }>(res);
    return wrapped.sources ?? [];
  },

  /**
   * List agents discovered by all enabled sources. When `source` is provided
   * the result is filtered to that single source kind (static/registry/mdns).
   * Cache is served if fresh, otherwise the backend transparently refreshes.
   */
  async listDiscoveredAgents(source?: DiscoverySourceKind): Promise<DiscoveredAgentDto[]> {
    const url = source
      ? `${API_BASE}/api/mesh/discovery/agents?source=${encodeURIComponent(source)}`
      : `${API_BASE}/api/mesh/discovery/agents`;
    const res = await fetch(url, { headers: headers(false) });
    const wrapped = await handle<{ count: number; cacheFresh: boolean; agents: DiscoveredAgentDto[] }>(res);
    return wrapped.agents ?? [];
  },

  /**
   * Force-refresh all enabled sources. The backend returns the refreshed
   * list but does NOT return added/removed counters, so the caller diffs the
   * result against a previously captured snapshot.
   */
  async refreshDiscovery(): Promise<{ count: number; agents: DiscoveredAgentDto[] }> {
    const res = await fetch(`${API_BASE}/api/mesh/discovery/refresh`, {
      method: "POST",
      headers: headers(false),
    });
    return handle<{ status: string; count: number; agents: DiscoveredAgentDto[] }>(res);
  },

  // ---- Backup (task_063 / Phase 7 task_094) ----

  /**
   * List all available backup archives. Wire format: `BackupSummary[]`.
   * Each entry carries id, filename, created_at (ISO 8601), size_bytes,
   * encrypted, key_fingerprint, components{config, skills, memory, sqlite_db,
   * identity}, files_count.
   */
  async listBackups(): Promise<BackupSummaryDto[]> {
    const res = await fetch(`${API_BASE}/api/backups`, { headers: headers(false) });
    return handle<BackupSummaryDto[]>(res);
  },

  /**
   * Create a new backup archive. The optional `passphrase` is forwarded to
   * the backend (which falls back to the configured `BackupConfig.Passphrase`
   * when omitted). Wire format: `BackupResult` with BackupId, ArchivePath,
   * SizeBytes, UncompressedBytes, FilesCount, Encrypted, KeyFingerprint,
   * Duration, Warnings.
   */
  async createBackup(passphrase?: string): Promise<BackupResultDto> {
    const res = await fetch(`${API_BASE}/api/backups`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(passphrase ? { passphrase } : {}),
    });
    return handle<BackupResultDto>(res);
  },

  /**
   * Restore a backup archive. `passphrase` is required only for encrypted
   * archives; `targetDir` is optional and falls back to the configured data
   * dir. Wire format: `RestoreResult` with BackupId, Success, FilesRestored,
   * FilesSkipped, Errors[], Warnings[], Duration.
   */
  async restoreBackup(id: string, passphrase?: string, targetDir?: string): Promise<RestoreResultDto> {
    const body: { passphrase?: string; targetDir?: string } = {};
    if (passphrase) body.passphrase = passphrase;
    if (targetDir) body.targetDir = targetDir;
    const res = await fetch(`${API_BASE}/api/backups/${encodeURIComponent(id)}/restore`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(body),
    });
    return handle<RestoreResultDto>(res);
  },

  /**
   * Verify integrity and hash validity of a backup archive.
   * Wire format: `VerifyResult` with BackupId, Valid, Encrypted,
   * KeyFingerprint, FileHashesValid[], FileHashesInvalid[], Warnings[],
   * Duration.
   */
  async verifyBackup(id: string, passphrase?: string): Promise<VerifyResultDto> {
    const url = passphrase
      ? `${API_BASE}/api/backups/${encodeURIComponent(id)}/verify?passphrase=${encodeURIComponent(passphrase)}`
      : `${API_BASE}/api/backups/${encodeURIComponent(id)}/verify`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<VerifyResultDto>(res);
  },

  /**
   * Delete a backup archive. Returns 204 No Content.
   */
  async deleteBackup(id: string): Promise<void> {
    const res = await fetch(`${API_BASE}/api/backups/${encodeURIComponent(id)}`, {
      method: "DELETE",
      headers: headers(false),
    });
    if (!res.ok) {
      let msg = `HTTP ${res.status}`;
      try {
        const body = await res.json();
        if (body?.error) msg = body.error;
      } catch { /* ignore */ }
      throw new Error(msg);
    }
  },

  // ---- SLO (task_064 / Phase 7 task_094) ----

  /**
   * SLO summary across all configured verticals. Wire format: `SloSummary`
   * with Timestamp, Verticals[] (each `SloStatus`), TotalVerticals, OkCount,
   * WarningCount, CriticalCount.
   */
  async getSloSummary(): Promise<SloSummaryDto> {
    const res = await fetch(`${API_BASE}/api/slos`, { headers: headers(false) });
    return handle<SloSummaryDto>(res);
  },

  /**
   * SLO definition for a specific vertical (e.g. "greenhouse", "cold-chain",
   * "server-room", "vending"). Wire format: `SloDefinition` with Vertical,
   * Description, Version, AvailabilityTarget, ResponseTimeTargetMs,
   * DataLossTargetPerDay, RecoveryTimeTargetMinutes, CostTargetUsd,
   * AlertThresholds, Runbooks.
   */
  async getSloDefinition(vertical: string): Promise<SloDefinitionDto> {
    const res = await fetch(`${API_BASE}/api/slos/${encodeURIComponent(vertical)}/definition`, {
      headers: headers(false),
    });
    return handle<SloDefinitionDto>(res);
  },

  /**
   * Current SLO status for a vertical. Wire format: `SloStatus` with
   * Vertical, OverallSeverity ("Ok" | "Warning" | "Critical"), Timestamp,
   * Objectives[], RecentViolations[], IsAcknowledged, AcknowledgedBy,
   * AcknowledgedAt.
   */
  async getSloStatus(vertical: string): Promise<SloStatusDto> {
    const res = await fetch(`${API_BASE}/api/slos/${encodeURIComponent(vertical)}`, {
      headers: headers(false),
    });
    return handle<SloStatusDto>(res);
  },

  /**
   * Full SLO report for a vertical: definition + current status +
   * historical compliance summary. Wire format: `SloReport` with Vertical,
   * GeneratedAt, CurrentStatus, Definition, Compliance (windowDays,
   * availabilityAchievementPct, responseTimeAchievementPct,
   * dataLossEventsTotal, maxRecoveryTimeMinutes, totalCostUsd,
   * overallCompliancePct).
   */
  async getSloReport(vertical: string): Promise<SloReportDto> {
    const res = await fetch(`${API_BASE}/api/slos/${encodeURIComponent(vertical)}/report`, {
      headers: headers(false),
    });
    return handle<SloReportDto>(res);
  },

  /**
   * Acknowledge a specific SLO violation (suppresses repeated alerts).
   * `acknowledgedBy` defaults to "operator" on the backend.
   */
  async ackSloViolation(vertical: string, violationId: string, acknowledgedBy?: string): Promise<{ acknowledged: boolean; violationId: string; by: string }> {
    const url = acknowledgedBy
      ? `${API_BASE}/api/slos/${encodeURIComponent(vertical)}/ack/${encodeURIComponent(violationId)}?acknowledgedBy=${encodeURIComponent(acknowledgedBy)}`
      : `${API_BASE}/api/slos/${encodeURIComponent(vertical)}/ack/${encodeURIComponent(violationId)}`;
    const res = await fetch(url, { method: "POST", headers: headers(false) });
    return handle<{ acknowledged: boolean; violationId: string; by: string }>(res);
  },

  /**
   * Acknowledge all active violations for a vertical.
   */
  async ackAllSloViolations(vertical: string, acknowledgedBy?: string): Promise<{ acknowledgedAll: boolean; vertical: string; by: string }> {
    const url = acknowledgedBy
      ? `${API_BASE}/api/slos/${encodeURIComponent(vertical)}/ack?acknowledgedBy=${encodeURIComponent(acknowledgedBy)}`
      : `${API_BASE}/api/slos/${encodeURIComponent(vertical)}/ack`;
    const res = await fetch(url, { method: "POST", headers: headers(false) });
    return handle<{ acknowledgedAll: boolean; vertical: string; by: string }>(res);
  },

  // ---- Quotas (task_056 / Phase 7 task_095) ----

  /**
   * Quota status + counters for a given scope. Mirrors the anonymous
   * payload of `GET /api/quotas` and `GET /api/quotas/{scope}/{scopeId}`.
   * `scope` is one of: "Agent" | "Skill" | "User" | "Tenant". When
   * `scopeId` is omitted, backend defaults it to "default".
   */
  async getQuotas(scope: string, scopeId?: string): Promise<QuotaStatusListDto> {
    const url = scopeId
      ? `${API_BASE}/api/quotas/${encodeURIComponent(scope)}/${encodeURIComponent(scopeId)}`
      : `${API_BASE}/api/quotas?scope=${encodeURIComponent(scope)}${scopeId ? `&scopeId=${encodeURIComponent(scopeId)}` : ""}`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<QuotaStatusListDto>(res);
  },

  /**
   * Single quota status for `(scope, scopeId, type)` — mirrors
   * `GET /api/quotas/{scope}/{scopeId}/{type}`.
   */
  async getQuotaForType(scope: string, scopeId: string, type: string): Promise<QuotaStatusDto> {
    const res = await fetch(
      `${API_BASE}/api/quotas/${encodeURIComponent(scope)}/${encodeURIComponent(scopeId)}/${encodeURIComponent(type)}`,
      { headers: headers(false) },
    );
    return handle<QuotaStatusDto>(res);
  },

  /**
   * Rate limit info for HTTP response headers — mirrors
   * `GET /api/quotas/rate-limit?scope=&scopeId=&type=`.
   */
  async getRateLimit(scope: string, scopeId?: string, type?: string): Promise<RateLimitInfoDto> {
    const params = new URLSearchParams({ scope });
    if (scopeId) params.set("scopeId", scopeId);
    if (type) params.set("type", type);
    const res = await fetch(`${API_BASE}/api/quotas/rate-limit?${params.toString()}`, { headers: headers(false) });
    return handle<RateLimitInfoDto>(res);
  },

  // ---- Rollouts (task_058 / Phase 7 task_095) ----

  /**
   * Current rollout state — mirrors `GET /api/rollout/state`. Returns
   * the full `RolloutState` with current / pending / LKG / history.
   */
  async getRolloutState(): Promise<{ state: RolloutStateDto }> {
    const res = await fetch(`${API_BASE}/api/rollout/state`, { headers: headers(false) });
    return handle<{ state: RolloutStateDto }>(res);
  },

  /**
   * Fetch a single bundle by ID — mirrors `GET /api/rollout/bundle/{id}`.
   */
  async getRolloutBundle(id: string): Promise<{ bundle: ConfigBundleDto }> {
    const res = await fetch(
      `${API_BASE}/api/rollout/bundle/${encodeURIComponent(id)}`,
      { headers: headers(false) },
    );
    return handle<{ bundle: ConfigBundleDto }>(res);
  },

  /**
   * Apply a new config/policy bundle. `bundle` is a partial `ConfigBundleDto`
   * payload — backend uses defaults for the rest.
   */
  async applyRollout(bundle: Partial<ConfigBundleDto>): Promise<{ status: string; bundleId: string; stage: string }> {
    const res = await fetch(`${API_BASE}/api/rollout/apply`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(bundle),
    });
    return handle<{ status: string; bundleId: string; stage: string }>(res);
  },

  /**
   * Promote a bundle to the next stage (Staging → Production).
   */
  async promoteRollout(bundleId: string): Promise<{ status: string; bundleId: string; stage: string }> {
    const res = await fetch(`${API_BASE}/api/rollout/promote`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify({ bundleId }),
    });
    return handle<{ status: string; bundleId: string; stage: string }>(res);
  },

  /**
   * Roll back to the last-known-good bundle.
   */
  async rollbackRollout(reason?: string): Promise<{ status: string; bundleId: string; stage: string }> {
    const res = await fetch(`${API_BASE}/api/rollout/rollback`, {
      method: "POST",
      headers: headers(),
      body: JSON.stringify(reason ? { reason } : {}),
    });
    return handle<{ status: string; bundleId: string; stage: string }>(res);
  },

  // ---- Security ops (task_055 / Phase 7 task_095) ----

  /**
   * List vulnerabilities with optional filters — mirrors
   * `GET /api/security/vulnerabilities`. `minSeverity` is one of
   * "Low" | "Medium" | "High" | "Critical" (lowercase also accepted).
   * `status` is one of the `VulnerabilityStatus` enum values.
   */
  async getVulnerabilities(params: {
    minSeverity?: string;
    status?: string;
    component?: string;
    from?: string;
    to?: string;
    limit?: number;
  } = {}): Promise<{ count: number; vulnerabilities: VulnerabilityDto[] }> {
    const qs = new URLSearchParams();
    if (params.minSeverity) qs.set("minSeverity", params.minSeverity);
    if (params.status) qs.set("status", params.status);
    if (params.component) qs.set("component", params.component);
    if (params.from) qs.set("from", params.from);
    if (params.to) qs.set("to", params.to);
    if (params.limit != null) qs.set("limit", String(params.limit));
    const url = qs.toString() ? `${API_BASE}/api/security/vulnerabilities?${qs.toString()}` : `${API_BASE}/api/security/vulnerabilities`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<{ count: number; vulnerabilities: VulnerabilityDto[] }>(res);
  },

  /** Aggregated vulnerability summary (Open / Critical / High / Medium / Low). */
  async getVulnerabilitySummary(): Promise<VulnerabilitySummaryDto> {
    const res = await fetch(`${API_BASE}/api/security/vulnerabilities/summary`, { headers: headers(false) });
    return handle<VulnerabilitySummaryDto>(res);
  },

  /** Single vulnerability by ID. */
  async getVulnerability(id: string): Promise<VulnerabilityDto> {
    const res = await fetch(
      `${API_BASE}/api/security/vulnerabilities/${encodeURIComponent(id)}`,
      { headers: headers(false) },
    );
    return handle<VulnerabilityDto>(res);
  },

  /**
   * Update vulnerability status. `status` is the new value; `notes` is
   * optional. Returns the updated record.
   */
  async updateVulnerabilityStatus(
    id: string,
    status: string,
    notes?: string,
  ): Promise<VulnerabilityDto> {
    const res = await fetch(
      `${API_BASE}/api/security/vulnerabilities/${encodeURIComponent(id)}/status`,
      { method: "PATCH", headers: headers(), body: JSON.stringify(notes ? { status, notes } : { status }) },
    );
    return handle<VulnerabilityDto>(res);
  },

  /**
   * Security-relevant audit events from the audit log. Mirrors
   * `GET /api/security/events?from=&to=&limit=`. Returns the most recent
   * `limit` events ordered by `Timestamp` descending.
   */
  async getSecurityEvents(params: { from?: string; to?: string; limit?: number } = {}): Promise<SecurityEventsResponseDto> {
    const qs = new URLSearchParams();
    if (params.from) qs.set("from", params.from);
    if (params.to) qs.set("to", params.to);
    if (params.limit != null) qs.set("limit", String(params.limit));
    const url = qs.toString() ? `${API_BASE}/api/security/events?${qs.toString()}` : `${API_BASE}/api/security/events`;
    const res = await fetch(url, { headers: headers(false) });
    return handle<SecurityEventsResponseDto>(res);
  },

  /**
   * Compliance report for a standard ("SOC2" | "ISO27001" | "GDPR" | "HIPAA").
   * Mirrors `GET /api/security/compliance/{standard}`.
   */
  async getComplianceReport(standard: string): Promise<ComplianceReportDto> {
    const res = await fetch(
      `${API_BASE}/api/security/compliance/${encodeURIComponent(standard)}`,
      { headers: headers(false) },
    );
    return handle<ComplianceReportDto>(res);
  },
};

// ---- Mesh DTOs ----

export interface MeshRouterRoutesDto {
  capability: string;
  count: number;
  candidates: MeshPeerCandidate[];
}

export interface MeshPeerCandidate {
  agentId: string;
  displayName: string;
  endpoint: string;
  healthScore: number;
  latencyMs: number;
  qualityScore: number;
  trustLevel: string;
  compositeScore: number;
  costHintUsd: number;
  circuitState: string;
  lastSeen: string;
  trustPassed: boolean;
}

export interface MeshRouterHealthDto {
  count: number;
  health: Record<string, { healthScore: number; avgLatencyMs: number }>;
}

// ---- Mesh Dashboard (Phase 5 task_053) ----

export interface MeshDashboardDto {
  topology: MeshTopologyDto;
  traffic: MeshTrafficDto;
  health: MeshHealthDto;
  policyDenials: MeshPolicyDenialsDto;
  skillHeatmap: MeshSkillHeatmapDto;
  evalSummary: MeshEvalSummaryDto;
  generatedAt: string;
}

export interface MeshTopologyDto {
  agentCount: number;
  agents: MeshAgentDto[];
  generatedAt: string;
}

export interface MeshAgentDto {
  agentId: string;
  displayName: string;
  endpoint: string;
  healthScore: number;
  latencyMs: number;
  qualityScore: number;
  trustLevel: string;
  lastSeen: string | null;
  capabilities: string[];
}

export interface MeshTrafficDto {
  totalRequests: number;
  fanOutRequests: number;
  meshDelegations: number;
  avgLatencyMs: number;
  meshRouterHits: number;
  circuitBreakerRejections: number;
  from: string;
  to: string;
}

export interface MeshHealthDto {
  agents: MeshHealthEntryDto[];
  healthyCount: number;
  degradedCount: number;
  unhealthyCount: number;
}

export interface MeshHealthEntryDto {
  agentId: string;
  displayName: string;
  healthScore: number;
  healthStatus: string;
  circuitState: string;
  consecutiveFailures: number;
  lastSeen: string | null;
  avgLatencyMs: number;
}

export interface MeshPolicyDenialsDto {
  count: number;
  denials: MeshDenialEntryDto[];
}

export interface MeshDenialEntryDto {
  id: number;
  actor: string;
  action: string;
  details: string | null;
  policyDecision: string | null;
  createdAt: string;
}

export interface MeshSkillHeatmapDto {
  totalSkills: number;
  skills: MeshSkillHeatmapEntryDto[];
}

export interface MeshSkillHeatmapEntryDto {
  skillId: string;
  skillName: string;
  totalUses: number;
  successRate: number;
  version: number;
  createdAt: string;
}

export interface MeshEvalSummaryDto {
  totalRuns: number;
  passedRuns: number;
  failedRuns: number;
  recentRuns: MeshEvalRunDto[];
}

export interface MeshEvalRunDto {
  runId: string;
  scenarioType: string;
  passed: boolean;
  score: number;
  successRate: number;
  assertionsPassed: number;
  assertionsFailed: number;
  startTimeUtc: string;
  durationMs: number;
}

// ---- A2A Agent Card (Phase 3 task_033 / Phase 7 task_088) ----

export interface AgentCardSkillDto {
  id: string;
  name: string;
  description: string;
  tags?: string[] | null;
  inputModes?: string[] | null;
  outputModes?: string[] | null;
  version?: string | null;
}

export interface AgentCardCapabilitiesDto {
  streaming: boolean;
  pushNotifications: boolean;
  stateTransitionReports: boolean;
  multipartResponses: boolean;
}

export interface A2AProviderDto {
  organization: string;
  url?: string | null;
}

export interface A2AAuthenticationDto {
  schemes: string[];
  credentials?: string | null;
}

export interface AgentCardDto {
  name: string;
  description: string;
  url: string;
  version: string;
  provider?: A2AProviderDto | null;
  capabilities: AgentCardCapabilitiesDto;
  authentication?: A2AAuthenticationDto | null;
  skills: AgentCardSkillDto[];
  defaultInputModes: string[];
  defaultOutputModes: string[];
  tags?: string[] | null;
  documentationUrl?: string | null;
  generatedAt: string;
}

export interface DiscoverCardEntryDto {
  url: string;
  name: string;
  version: string;
  url2: string;
  skills: { id: string; name: string }[];
}

export interface DiscoverResultDto {
  total: number;
  discovered: number;
  failed: number;
  cards: DiscoverCardEntryDto[];
}

// ---- Capability Registry (Phase 3 task_034 / Phase 7 task_089) ----

/**
 * Mirrors `RegistryAgentFullEntry` (Mesh/CapabilityRegistry.cs).
 * Backend GET /api/mesh/agents → `{count, agents: MeshAgentEntryDto[]}`.
 * Note: `supportedProtocolVersionsJson` is serialized as a JSON-encoded string,
 * not an array — we keep the same name on the wire and parse on the client.
 */
export interface MeshAgentEntryDto {
  agentId: string;
  displayName: string;
  description: string;
  endpoint: string;
  lastSeen: string;
  healthStatus: string;
  lastHealthCheck: string;
  consecutiveFailures: number;
  trustLevel: string;
  costHintUsd: number;
  latencyHintMs: number;
  expirySeconds: number;
  supportedProtocolVersionsJson: string;
}

/** Lightweight agent — used by capability search and `/api/mesh/capabilities` without agentId. */
export interface MeshAgentLightDto {
  agentId: string;
  displayName: string;
  description: string;
  endpoint: string;
  lastSeen: string;
}

/**
 * Mirrors the anonymous payload of GET /api/mesh/agents/{id}/health.
 */
export interface MeshAgentHealthDto {
  consecutiveFailures: number;
  trustLevel: string;
  costHintUsd: number;
  latencyHintMs: number;
  expirySeconds: number;
}

/**
 * Mirrors `RegistryCapabilityEntry` (Mesh/CapabilityRegistry.cs).
 * Returned by GET /api/mesh/capabilities?agentId=…
 */
export interface MeshCapabilityDto {
  name: string;
  description: string;
  phraseReceivers: string[];
}

// ---- Trust admission policy (Phase 3 task_040 / Phase 7 task_091) ----

/**
 * Mirrors the anonymous payload of GET /api/mesh/policy/status.
 * `mode` is lowercase ("enforce" | "dryrun" | "disabled"); `enabled`
 * indicates whether the policy subsystem is switched on at all. The
 * per-list fields default to "allow everything" when the array is empty.
 */
export interface TrustAdmissionPolicyStatusDto {
  enabled: boolean;
  mode: string;
  allowedTrustLevels: string[];
  allowedIntents: string[];
  allowedClassifications: string[];
  allowSchemaMismatch: boolean;
  allowBudgetExceeded: boolean;
  allowedRiskLevels: string[];
}

/**
 * Minimal `IntentEnvelope` shape required by the dry-run endpoint.
 * Only the fields that the policy engine inspects are typed here;
 * extra fields from a real envelope are allowed but ignored.
 */
export interface TrustAdmissionIntentEnvelopeDto {
  request_id: string;
  sender: string;
  intent: string;
  payload: string;
  version: string;
}

/**
 * Request body for POST /api/mesh/policy/dry-run.
 * The required `envelope` carries intent + payload; the rest are the
 * caller-side attributes that the policy engine checks.
 */
export interface TrustAdmissionDryRunRequestDto {
  envelope: TrustAdmissionIntentEnvelopeDto;
  targetAgentId?: string | null;
  callerTrustLevel?: string | null;
  dataClassification?: string | null;
  callerSchemaVersion?: string | null;
  requestedRiskLevel?: string | null;
}

/**
 * Mirrors the anonymous payload of POST /api/mesh/policy/dry-run.
 * `denialCode` is one of: "TrustLevelTooLow", "IntentNotAllowed",
 * "ClassificationTooHigh", "SchemaVersionMismatch", "BudgetLimitExceeded",
 * "RiskLevelMismatch" — or null when allowed. `dryRun` reflects whether
 * the evaluation ran in dry-run mode at the time; `effectiveMode` is the
 * mode the policy was in (Enforce/DryRun/Disabled).
 */
export interface TrustAdmissionDryRunResultDto {
  allowed: boolean;
  denialReason: string | null;
  denialCode: string | null;
  dryRun: boolean;
  effectiveMode: string;
}

// ---- Discovery mechanisms (Phase 3 task_038 / Phase 7 task_090) ----

/** Source kind matching `Hercules.Mesh.Discovery.DiscoverySourceKind` (static / registry / mdns). */
export type DiscoverySourceKind = "static" | "registry" | "mdns";

/**
 * Mirrors the anonymous payload of GET /api/mesh/discovery/sources.
 * Backend does not expose `lastRunAt` / `lastError` per source; those are
 * recorded server-side by `DiscoveryService` but not surfaced via the
 * controller — we keep them out of the DTO to match the wire shape.
 */
export interface DiscoverySourceDto {
  /** Lowercase source kind: "static" | "registry" | "mdns". */
  kind: DiscoverySourceKind;
  /** Lowercase kind as returned by the backend; alias of `kind` for convenience. */
  source: DiscoverySourceKind;
  /** Human-readable name (e.g. "Static peers"). */
  name: string;
  /** Whether this source participates in `GetAgentsAsync` / `RefreshAsync`. */
  enabled: boolean;
}

/**
 * Mirrors the anonymous payload of GET /api/mesh/discovery/agents.
 * `discoveredAt` is an ISO 8601 UTC timestamp.
 */
export interface DiscoveredAgentDto {
  agentId: string;
  displayName: string;
  endpoint: string;
  /** Source kind that reported this agent. */
  source: DiscoverySourceKind;
  /** ISO 8601 UTC timestamp. */
  discoveredAt: string;
  capabilities: string[];
  manifestLoaded: boolean;
  error: string | null;
}

/**
 * Result of POST /api/mesh/discovery/refresh as returned by the backend.
 * The backend does not compute added/removed counters — `refreshDiscovery()`
 * returns the refreshed list and the caller (DiscoveryPanel) diffs it
 * against the previous snapshot to compute these values itself.
 */
export interface RefreshDiscoveryRawDto {
  status: string;
  count: number;
  agents: DiscoveredAgentDto[];
}

// ---- Mesh profiles & backend health (Phase 5 task_070 / Phase 7 task_092) ----

/**
 * Wire payload of GET /api/mesh/profiles — names only plus active.
 * `activeProfile` is the profile currently in use (falls back to "local").
 */
export interface MeshProfileListDto {
  count: number;
  profiles: string[];
  activeProfile: string;
}

/**
 * Backend config for a single role ("bus" | "queue" | "stateStore") inside
 * a profile definition. Mirrors `MeshBackendConfig` in Mesh/Profiles/MeshProfile.cs.
 */
export interface MeshProfileBackendDto {
  /** "in-process" | "redis" | "nats" | "postgres". */
  kind: string;
  /** Connection string or host list. `null` when in-process. */
  connectionString: string | null;
  /** Comma-separated host:port pairs (alternative to connectionString). */
  hosts: string[];
  /** Whether this backend is enabled in the profile. */
  enabled: boolean;
  /** Interval in seconds between health checks. Default 30. */
  healthCheckIntervalSec: number;
  /** Connection/operation timeout in seconds. Default 5. */
  timeoutSec: number;
  /** Max consecutive retries before marking Unavailable. Default 3. */
  maxRetries: number;
}

/** Mirrors `MeshProfileConstraints` (Mesh/Profiles/MeshProfile.cs). */
export interface MeshProfileConstraintsDto {
  /** 0 = no limit. */
  maxAgents: number;
  /** Target region / availability zone. */
  region: string | null;
  /** Required external services (e.g. ["redis:6379"]). */
  requiredServices: string[];
}

/**
 * Mirrors `DegradationPolicy` (Mesh/Profiles/MeshProfile.cs).
 * `mode` is one of: "FailSilent" | "DegradeToLocal" | "RefuseDelegations".
 */
export interface DegradationPolicyDto {
  mode: string;
  /** Optional webhook URL called when a backend transitions. */
  alertWebhook: string | null;
  /** 0 = no forced stop. */
  maxDegradedSeconds: number;
}

/**
 * Mirrors `MeshProfileDefinition` (Mesh/Profiles/MeshProfile.cs) as returned
 * by GET /api/mesh/profiles/{name}. `profile` is one of: "Local" | "Redis"
 * | "Nats" | "Postgres" | "Hybrid". `backends` is keyed by role
 * ("bus" | "queue" | "stateStore").
 */
export interface MeshProfileDto {
  name: string;
  description: string;
  profile: string;
  backends: Record<string, MeshProfileBackendDto>;
  constraints: MeshProfileConstraintsDto;
  degradationPolicy: DegradationPolicyDto;
}

/**
 * Effective backend config for a profile (after Enabled/fallback resolution).
 * Returned by GET /api/mesh/profiles/{name}/backends. The wrapper is
 * `{profile, backends: {role: Effective}}` — `Effective` carries the role
 * name back for convenience.
 */
export interface MeshProfileBackendEffectiveDto {
  role: string;
  kind: string;
  enabled: boolean;
  connectionString: string | null;
  hosts: string[];
  healthCheckIntervalSec: number;
  timeoutSec: number;
  maxRetries: number;
}

export interface MeshProfileBackendsDto {
  profile: string;
  backends: Record<string, MeshProfileBackendEffectiveDto>;
}

/**
 * Mirrors the controller-private `BackendHealthDto` from
 * `MeshProfileController`. The backend does NOT return `latencyMs` per
 * backend (that field is on agent-level mesh health, not on backend health)
 * nor a separate `enabled` flag — that information lives on the profile's
 * backend config. `state` is one of:
 *   "Unknown" | "Healthy" | "Degraded" | "Unavailable".
 */
export interface BackendStatusDto {
  role: string;
  kind: string;
  state: string;
  lastCheckedAt: string;
  consecutiveFailures: number;
  lastError: string | null;
}

/**
 * Wire payload of GET /api/mesh/backend-status. `overall` mirrors
 * the rollup returned by the controller: "Healthy" if all backends are
 * Healthy, "Unavailable" if any is Unavailable, "Degraded" if any is
 * Degraded (but none Unavailable), "Unknown" when no backends are
 * registered.
 */
export interface MeshBackendStatusListDto {
  overall: string;
  backends: BackendStatusDto[];
}

// ---- Mesh observability (Phase 7 task_093) ----

/**
 * Mesh observability counters snapshot.
 * `from`/`to` are the process-start time and current time (ISO 8601 UTC).
 * `byCapability` aggregates `routing_decision` events per intent.
 * `byPeer` aggregates `delegation` events per peer agent id.
 */
export interface MeshObservabilityCountersDto {
  from: string;
  to: string;
  routingDecision: number;
  retryAttempt: number;
  circuitBreakerStateChange: number;
  delegation: number;
  meshBackendHealth: number;
  byCapability: Record<string, number>;
  byPeer: Record<string, number>;
}

/**
 * Full mesh observability status — counters + observability config + enabled flag.
 * Mirrors the GET /api/mesh/observability/status response.
 */
export interface MeshObservabilityStatusDto {
  enabled: boolean;
  config: {
    enabled: boolean;
    propagationFormat: string;
    enableSpanEnrichment: boolean;
    enableMetrics: boolean;
    enableStructuredLogs: boolean;
    enableTraceContextPropagation: boolean;
    enableTraceContextExtraction: boolean;
    maxTagValueLength: number;
    redactedAttributes: string[];
    otlpEndpoints: string[];
  };
  counters: MeshObservabilityCountersDto;
}

/**
 * Mirrors `TraceSummary` (Mesh/Observability/MeshDiagnosticsService.cs).
 * `startedAt` is ISO 8601 UTC. `status` is one of: "Ok" | "Error" | "Unset".
 */
export interface TraceSummaryDto {
  traceId: string;
  rootName: string;
  startedAt: string;
  durationMs: number;
  status: string;
  spanCount: number;
}

/**
 * Wire payload of GET /api/mesh/observability/traces.
 * `count` is the number of traces actually returned; `limit` echoes the
 * request limit (or the backend default if no `?limit=` was provided).
 */
export interface MeshTracesResponseDto {
  count: number;
  limit: number;
  traces: TraceSummaryDto[];
}

/**
 * Mirrors `LogEntrySummary`. `level` is the normalized level name
 * (Trace | Debug | Info | Warning | Error | Critical).
 * `structuredFields` is the original log scope / template parameters,
 * captured verbatim for the UI to render as a key/value table.
 */
export interface LogEntryDto {
  timestamp: string;
  level: string;
  source: string;
  requestId: string | null;
  traceId: string | null;
  message: string;
  structuredFields: Record<string, unknown>;
}

/**
 * Wire payload of GET /api/mesh/observability/logs.
 * `level` is the filter that was applied (or "all" when no filter).
 */
export interface MeshLogsResponseDto {
  count: number;
  limit: number;
  level: string;
  logs: LogEntryDto[];
}

// ---- Backup DTOs (task_063 / Phase 7 task_094) ----

/**
 * Mirrors `BackupComponents` (Backup/Models.cs). All flags indicate which
 * subsystems were included in the archive.
 */
export interface BackupComponentsDto {
  config: boolean;
  skills: boolean;
  memory: boolean;
  sqlite_db: boolean;
  identity: boolean;
}

/**
 * Mirrors `BackupSummary` (Backup/Models.cs) — the per-archive row returned
 * by `GET /api/backups`. Field names use snake_case because the backend
 * serialises them via `[JsonPropertyName]` attributes.
 */
export interface BackupSummaryDto {
  id: string;
  filename: string;
  created_at: string;
  size_bytes: number;
  encrypted: boolean;
  key_fingerprint: string | null;
  components: BackupComponentsDto;
  files_count: number;
}

/**
 * Mirrors `BackupResult` (Backup/Models.cs) — returned by `POST /api/backups`.
 * `Duration` is serialised as a TimeSpan and arrives as an ISO 8601 duration
 * string (e.g. "PT1.234S").
 */
export interface BackupResultDto {
  BackupId: string;
  ArchivePath: string;
  SizeBytes: number;
  UncompressedBytes: number;
  FilesCount: number;
  Encrypted: boolean;
  KeyFingerprint: string | null;
  Duration: string;
  Warnings: string[];
}

/**
 * Mirrors `RestoreResult` (Backup/Models.cs) — returned by
 * `POST /api/backups/{id}/restore`. `Success=false` with non-empty `Errors`
 * yields HTTP 422.
 */
export interface RestoreResultDto {
  BackupId: string;
  Success: boolean;
  FilesRestored: number;
  FilesSkipped: number;
  Errors: string[];
  Warnings: string[];
  Duration: string;
}

/**
 * Mirrors `VerifyResult` (Backup/Models.cs) — returned by
 * `GET /api/backups/{id}/verify`. `Valid=false` yields HTTP 422.
 */
export interface VerifyResultDto {
  BackupId: string;
  Valid: boolean;
  Encrypted: boolean;
  KeyFingerprint: string | null;
  FileHashesValid: string[];
  FileHashesInvalid: string[];
  Warnings: string[];
  Duration: string;
}

// ---- SLO DTOs (task_064 / Phase 7 task_094) ----

/**
 * Mirrors `SloAlertThresholds` (Slo/SloTypes.cs). All fields are nullable
 * — when null, the config default applies.
 */
export interface SloAlertThresholdsDto {
  availability_warning_pct?: number | null;
  availability_critical_pct?: number | null;
  response_time_warning_ms?: number | null;
  response_time_critical_ms?: number | null;
  data_loss_warning_per_day?: number | null;
  data_loss_critical_per_day?: number | null;
  recovery_time_warning_minutes?: number | null;
  recovery_time_critical_minutes?: number | null;
  cost_warning_pct?: number | null;
  cost_critical_pct?: number | null;
}

/**
 * Mirrors `SloRunbook` (Slo/SloTypes.cs) — one entry per breach type.
 */
export interface SloRunbookDto {
  title: string;
  symptoms: string[];
  diagnosis_steps: string[];
  mitigation_steps: string[];
  escalation_trigger: string;
}

/**
 * Mirrors `SloRunbooks` (Slo/SloTypes.cs).
 */
export interface SloRunbooksDto {
  availability: SloRunbookDto;
  response_time: SloRunbookDto;
  data_loss: SloRunbookDto;
  recovery_time: SloRunbookDto;
  cost: SloRunbookDto;
}

/**
 * Mirrors `SloDefinition` (Slo/SloTypes.cs) — returned by
 * `GET /api/slos/{vertical}/definition`.
 *
 * Targets are stored as raw values:
 *  - `availabilityTarget` is a decimal fraction (0.999 = 99.9%)
 *  - `responseTimeTargetMs` is P95 latency in milliseconds
 *  - `dataLossTargetPerDay` is the maximum allowed lost events per day
 *  - `recoveryTimeTargetMinutes` is the maximum recovery time in minutes
 *  - `costTargetUsd` is the daily cost budget in USD
 */
export interface SloDefinitionDto {
  vertical: string;
  description: string;
  version: string;
  availabilityTarget: number;
  responseTimeTargetMs: number;
  dataLossTargetPerDay: number;
  recoveryTimeTargetMinutes: number;
  costTargetUsd: number;
  alertThresholds: SloAlertThresholdsDto | null;
  runbooks: SloRunbooksDto;
}

/**
 * Mirrors `SloObjectiveStatus` (Slo/SloTypes.cs) — per-objective entry in
 * `SloStatus.Objectives`. `severity` is "Ok" | "Warning" | "Critical".
 * `targetAchievementPct` is computed server-side and capped at 200.
 */
export interface SloObjectiveStatusDto {
  objective: string;
  severity: string;
  currentValue: number;
  targetValue: number;
  unit: string;
  targetAchievementPct: number;
  breachDescription: string;
}

/**
 * Mirrors `SloViolationRecord` (Slo/SloTypes.cs). `severity` is
 * "Ok" | "Warning" | "Critical".
 */
export interface SloViolationRecordDto {
  violationId: string;
  vertical: string;
  objective: string;
  severity: string;
  detectedAt: string;
  resolvedAt: string | null;
  actualValue: number;
  targetValue: number;
  breachDescription: string;
  isAcknowledged: boolean;
  acknowledgedBy: string | null;
  acknowledgedAt: string | null;
}

/**
 * Mirrors `SloStatus` (Slo/SloTypes.cs) — returned by
 * `GET /api/slos/{vertical}` and embedded in `SloSummary.Verticals`.
 */
export interface SloStatusDto {
  vertical: string;
  overallSeverity: string;
  timestamp: string;
  objectives: SloObjectiveStatusDto[];
  recentViolations: SloViolationRecordDto[];
  isAcknowledged: boolean;
  acknowledgedBy: string | null;
  acknowledgedAt: string | null;
}

/**
 * Mirrors `SloComplianceSummary` (Slo/SloTypes.cs) — historical compliance
 * over the configured reporting window (default 7 days).
 */
export interface SloComplianceSummaryDto {
  windowDays: number;
  availabilityAchievementPct: number;
  responseTimeAchievementPct: number;
  dataLossEventsTotal: number;
  maxRecoveryTimeMinutes: number;
  totalCostUsd: number;
  overallCompliancePct: number;
}

/**
 * Mirrors `SloReport` (Slo/SloTypes.cs) — returned by
 * `GET /api/slos/{vertical}/report`.
 */
export interface SloReportDto {
  vertical: string;
  generatedAt: string;
  currentStatus: SloStatusDto;
  definition: SloDefinitionDto;
  compliance: SloComplianceSummaryDto;
}

/**
 * Mirrors `SloSummary` (Slo/SloTypes.cs) — returned by `GET /api/slos`.
 * `totalVerticals`, `okCount`, `warningCount`, `criticalCount` are computed
 * server-side from `verticals[]`.
 */
export interface SloSummaryDto {
  timestamp: string;
  verticals: SloStatusDto[];
  totalVerticals: number;
  okCount: number;
  warningCount: number;
  criticalCount: number;
}

// ---- Quota DTOs (task_056 / Phase 7 task_095) ----

/**
 * Mirrors the anonymous `counters` payload of `GET /api/quotas` and
 * `GET /api/quotas/{scope}/{scopeId}`. Field names use snake_case because
 * the backend uses `[JsonPropertyName]` attributes on the C# side.
 */
export interface QuotaCountersDto {
  tokensUsedToday: number;
  messagesUsedToday: number;
  requestsUsedToday: number;
  storageUsedMb: number;
  costUsedTodayCents: number;
  activeConcurrentRequests: number;
  activeSkillExecutions: number;
  lastResetDate: string;
}

/**
 * Mirrors the anonymous per-limit payload of `GET /api/quotas`.
 * `type` is one of the `QuotaLimitType` enum values (e.g. "CallsPerMinutePerAgent").
 * `usagePercent` is the precomputed [0..100] percentage.
 */
export interface QuotaStatusDto {
  type: string;
  limit: number;
  current: number;
  remaining: number;
  isExceeded: boolean;
  isHardCap: boolean;
  usagePercent: number;
  resetAt: string | null;
}

/**
 * Wire payload of `GET /api/quotas` and `GET /api/quotas/{scope}/{scopeId}`.
 * `limits` is the list of individual limit statuses for this scope.
 */
export interface QuotaStatusListDto {
  scope: string;
  scopeId: string;
  counters: QuotaCountersDto;
  limits: QuotaStatusDto[];
}

/**
 * Mirrors `RateLimitInfo` (Quotas/Models.cs) — returned by
 * `GET /api/quotas/rate-limit`. `resetAt` is ISO 8601 UTC.
 */
export interface RateLimitInfoDto {
  limit: string;
  remaining: number;
  resetAt: string;
  retryAfterSeconds: number;
}

// ---- Rollout DTOs (task_058 / Phase 7 task_095) ----

/**
 * Mirrors `RolloutHistoryEntry` (Config/Rollout/Models.cs).
 * `action` is one of: "applied" | "promoted" | "rolled_back" | "expired".
 */
export interface RolloutHistoryEntryDto {
  bundleId: string;
  version: string;
  action: string;
  timestamp: string;
  reason: string | null;
}

/**
 * Mirrors `RolloutState` (Config/Rollout/Models.cs) — returned by
 * `GET /api/rollout/state`. `currentStage` is one of the `BundleStage`
 * enum values: "Pending" | "Staging" | "Production" | "Retired".
 * `lastPromotedAt` and `lastRollbackAt` are nullable timestamps.
 */
export interface RolloutStateDto {
  currentBundleId: string | null;
  currentVersion: string | null;
  currentStage: string;
  lastKnownGoodBundleId: string | null;
  lastKnownGoodVersion: string | null;
  pendingBundleId: string | null;
  pendingVersion: string | null;
  lastPromotedAt: string | null;
  lastRollbackAt: string | null;
  rolloutHistory: RolloutHistoryEntryDto[];
}

/**
 * Mirrors `ConfigBundle` (Config/Rollout/Models.cs) — returned by
 * `GET /api/rollout/bundle/{id}` and accepted by `POST /api/rollout/apply`.
 * `type` is "config" | "policy". `stage` follows the same enum as
 * `RolloutState.currentStage`. Fields use snake_case because the backend
 * uses `[JsonPropertyName]` attributes.
 */
export interface ConfigBundleDto {
  id: string;
  version: string;
  type: string;
  name: string;
  description: string;
  signerId: string | null;
  signedAt: string | null;
  algorithm: string;
  signature: string;
  publicKeyFingerprint: string | null;
  stagingGroup: string | null;
  expiresAt: string | null;
  minHerculesVersion: string | null;
  maxHerculesVersion: string | null;
  stage: string;
  createdAt: string;
  appliedAt: string | null;
  stagingDurationMinutes: number;
  payload: string;
}

// ---- Security DTOs (task_055 / Phase 7 task_095) ----

/**
 * Mirrors `VulnerabilityReport` (Security/IVulnerabilityReporter.cs).
 * `severity` is one of: "Low" | "Medium" | "High" | "Critical".
 * `status` is one of: "Reported" | "Confirmed" | "InProgress" | "Mitigated"
 * | "Resolved" | "FalsePositive" | "Accepted".
 */
export interface VulnerabilityDto {
  vulnerabilityId: string;
  title: string;
  description: string;
  severity: string;
  status: string;
  affectedComponent: string;
  affectedVersion: string | null;
  reporter: string;
  reportedAt: string;
  resolvedAt: string | null;
  resolution: string | null;
  references: string[];
}

/**
 * Mirrors `VulnerabilitySummary` (Security/IVulnerabilityReporter.cs).
 * Returned by `GET /api/security/vulnerabilities/summary`.
 */
export interface VulnerabilitySummaryDto {
  total: number;
  open: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
  lastReportedAt: string;
  lastResolvedAt: string | null;
}

/**
 * Mirrors `SecurityEvent` (Security/ISecurityAuditExporter.cs) — one
 * entry in the security-events feed. `success` is the heuristic that
 * inspects the audit entry result.
 */
export interface SecurityEventDto {
  eventId: string;
  eventType: string;
  actor: string;
  target: string | null;
  timestamp: string;
  details: string | null;
  ipAddress: string | null;
  success: boolean;
}

/**
 * Mirrors `SecurityMetrics` (Security/ISecurityAuditExporter.cs).
 */
export interface SecurityMetricsDto {
  totalEvents: number;
  authenticationEvents: number;
  authorizationEvents: number;
  configurationChanges: number;
  toolExecutions: number;
  skillOperations: number;
  securityAlerts: number;
  averageResponseTimeMs: number;
}

/**
 * Wire payload of `GET /api/security/events`. `from`/`to` are the
 * requested audit window; `total` is the full count of events the
 * exporter saw; `returned` is the count actually serialised (capped
 * to `limit`).
 */
export interface SecurityEventsResponseDto {
  from: string;
  to: string;
  total: number;
  returned: number;
  events: SecurityEventDto[];
  metrics: SecurityMetricsDto;
}

/**
 * Mirrors `ComplianceCheck` (Security/ISecurityAuditExporter.cs).
 */
export interface ComplianceCheckDto {
  controlId: string;
  description: string;
  passed: boolean;
  evidence: string | null;
  remediation: string | null;
}

/**
 * Mirrors `ComplianceReport` (Security/ISecurityAuditExporter.cs).
 * `standard` is one of: "SOC2" | "ISO27001" | "GDPR" | "HIPAA".
 */
export interface ComplianceReportDto {
  reportId: string;
  standard: string;
  generatedAt: string;
  periodStart: string;
  periodEnd: string;
  isCompliant: boolean;
  checks: ComplianceCheckDto[];
  findings: string[];
  recommendations: string[];
}
