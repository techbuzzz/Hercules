// Клиент Hercules Web API.
// База и ключ берутся из переменных окружения Astro (PUBLIC_*),
// со значениями по умолчанию для локального запуска.

export const API_BASE =
  (import.meta.env.PUBLIC_API_BASE as string) || "http://localhost:5000";
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
