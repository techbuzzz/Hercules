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
