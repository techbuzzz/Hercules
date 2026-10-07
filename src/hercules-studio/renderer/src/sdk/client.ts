import type {
  AgentManifest,
  ApprovalDto,
  ApprovalsResponse,
  ChatResponseDto,
  ConfigDto,
  EscalationDto,
  EscalationsResponse,
  MeshDashboardDto,
  MeshHealthDto,
  MeshRoutesResponseDto,
  SharedFact,
  McpServerDetailDto,
  McpReloadResponseDto,
  McpServersListResponseDto,
  ApiKeySummaryDto,
  ApiKeysListResponseDto,
  CreateApiKeyRequestDto,
  CreatedApiKeyResponseDto,
  UpdateApiKeyRequestDto,
  ApiKeyMutationResponseDto,
  ApiKeyDeleteResponseDto,
  ContextBudgetDto,
  ContextDistillResultDto,
  CodeScanResponseDto,
  ConsensusSessionDto,
  ConsensusSessionListDto,
  ContextSummaryDto,
  LlmConfigDto,
  QuotaStatusResponseDto,
  SkillDto,
  SkillPromptHistoryResponseDto,
  SkillDetailDto,
  StatsDto,
  ToolDto,
  ToolsResponse,
} from "./types";

/**
 * HTTP client for a Hercules agent.
 *
 * Authentication is a short-lived session token obtained via
 * `POST /api/studio/session` and held only in memory by the platform layer.
 * The token is read through a provider on every request so a re-exchange is
 * picked up without rebuilding the client.
 *
 * System-role operations (config PUT, restart, MCP mutation) are authorised by
 * the agent based on the token's role; a contribute-role token gets 403 and the
 * caller must request a system session.
 */
/** Circuit breaker state for one peer, normalised from the agent's map shape. */
export interface MeshCircuitState {
  agentId: string;
  state: string;
}

export class HerculesClient {
  private readonly baseUrl: string;
  private readonly tokenProvider: () => string | null;

  constructor(baseUrl: string, tokenProvider: () => string | null) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.tokenProvider = tokenProvider;
  }

  hasSession(): boolean {
    return this.tokenProvider() !== null;
  }

  private authHeaders(json = true): Record<string, string> {
    const token = this.tokenProvider();
    if (!token) throw new Error("No active session — re-enter the API key");
    const h: Record<string, string> = { "X-Session-Token": token };
    if (json) h["Content-Type"] = "application/json";
    return h;
  }

  private async handle<T>(res: Response): Promise<T> {
    if (!res.ok) {
      let msg = `HTTP ${res.status}`;
      try {
        const body = await res.json();
        if (body?.error) msg = body.error;
      } catch {
        /* ignore */
      }
      throw new Error(msg);
    }
    if (res.status === 204) return undefined as T;
    return res.json() as Promise<T>;
  }

  private async request<T>(
    path: string,
    init: { method?: string; body?: unknown; auth?: boolean; timeoutMs?: number } = {},
  ): Promise<T> {
    const { method = "GET", body, auth = true, timeoutMs = 30_000 } = init;
    const headers = auth ? this.authHeaders(body !== undefined) : {};
    return this.handle<T>(
      await fetch(`${this.baseUrl}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
        signal: AbortSignal.timeout(timeoutMs),
      }),
    );
  }

  // ---- System ----

  /** Public health endpoint; requires no session. */
  async getHealth(): Promise<{ status: string }> {
    return this.handle<{ status: string }>(
      await fetch(`${this.baseUrl}/api/health`, { signal: AbortSignal.timeout(3000) }),
    );
  }

  async getManifest(): Promise<AgentManifest> {
    return this.request<AgentManifest>("/agent.manifest.json", { auth: false });
  }

  // ---- Chat ----

  async chat(message: string, sessionId?: string): Promise<ChatResponseDto> {
    const headers: Record<string, string> = { ...this.authHeaders(), "X-Session-Id": sessionId ?? "default" };
    return this.handle<ChatResponseDto>(
      await fetch(`${this.baseUrl}/api/chat`, {
        method: "POST",
        headers,
        body: JSON.stringify({ message }),
        signal: AbortSignal.timeout(120_000),
      }),
    );
  }

  // ---- Skills ----

  async listSkills(): Promise<SkillDto[]> {
    return this.request<SkillDto[]>("/api/skills");
  }

  async getSkill(id: string): Promise<SkillDetailDto> {
    return this.request<SkillDetailDto>(`/api/skills/${encodeURIComponent(id)}`);
  }

  async createSkill(data: {
    name: string;
    trigger: string;
    prompt: string;
    description?: string;
    /** Modern multi-trigger field; the agent prefers it over the legacy single `trigger`. */
    phraseReceivers?: string[];
  }): Promise<SkillDto> {
    return this.request<SkillDto>("/api/skills", { method: "POST", body: data });
  }

  async updateSkill(
    id: string,
    data: { triggers?: string[]; prompt?: string; description?: string },
  ): Promise<SkillDto> {
    return this.request<SkillDto>(`/api/skills/${encodeURIComponent(id)}`, { method: "PUT", body: data });
  }

  /**
 * Imports a `.skillpkg` archive (Stage 3). Sent as multipart because the endpoint reads
 * `ReadFormAsync`; `conflict` is the agent's own replace|skip|rename policy.
 */
async importSkill(file: File, conflict: "replace" | "skip" | "rename" = "rename"): Promise<unknown> {
  const form = new FormData();
  form.append("file", file);
  form.append("conflict", conflict);

  return this.handle<unknown>(
    await fetch(`${this.baseUrl}/api/skills/import?conflict=${conflict}`, {
      method: "POST",
      headers: this.authHeaders(),
      body: form,
      signal: AbortSignal.timeout(120_000),
    }),
  );
}

/** Previous prompt revisions, oldest-first. Powers the diff view (Stage 2). */
  async getSkillPromptHistory(id: string): Promise<SkillPromptHistoryResponseDto> {
    return this.request<SkillPromptHistoryResponseDto>(
      `/api/skills/${encodeURIComponent(id)}/prompt-history`,
    );
  }

  async deleteSkill(id: string): Promise<void> {
    await this.request<void>(`/api/skills/${encodeURIComponent(id)}`, { method: "DELETE" });
  }

  async improveSkill(id: string): Promise<SkillDto> {
    return this.request<SkillDto>(`/api/skills/${encodeURIComponent(id)}/improve`, { method: "POST" });
  }

  async evaluateSkill(id: string): Promise<{ skillId: string; score: number; passed: boolean }> {
    return this.request<{ skillId: string; score: number; passed: boolean }>(
      `/api/skills/${encodeURIComponent(id)}/evaluate`,
      { method: "POST" },
    );
  }

  // ---- Stats ----

  async stats(): Promise<StatsDto> {
    return this.request<StatsDto>("/api/stats");
  }

  // ---- Config ----

  async getConfig(): Promise<ConfigDto> {
    return this.request<ConfigDto>("/api/config");
  }

  async patchConfig(patch: Record<string, unknown>): Promise<ConfigDto> {
    return this.request<ConfigDto>("/api/config", { method: "PATCH", body: patch });
  }

  /** Requires a system-role session. */
  async putConfig(body: unknown): Promise<ConfigDto> {
    return this.request<ConfigDto>("/api/config", { method: "PUT", body });
  }

  // ---- Mesh ----

  async getMeshDashboard(): Promise<MeshDashboardDto> {
    return this.request<MeshDashboardDto>("/api/mesh/dashboard");
  }

  /**
 * The agent answers `{ count, agents }`, not a bare array — declaring this as
 * `unknown[]` is what let MeshView's `Array.isArray` guard silently swallow
 * every real response. Typed as the envelope; callers unwrap `.agents`.
 * The endpoint returns an anonymous object, so there is no generated schema.
 */
async getMeshAgents(): Promise<{ count?: number; agents?: unknown[] }> {
    return this.request<{ count?: number; agents?: unknown[] }>("/api/mesh/agents");
  }

  async getMeshHealth(): Promise<MeshHealthDto> {
    return this.request<MeshHealthDto>("/api/mesh/health");
  }

  /** Recent policy denials, newest first. */
  async getMeshDenials(limit = 20): Promise<unknown[]> {
    return this.request<unknown[]>(`/api/mesh/denials?limit=${limit}`);
  }

  // ---- Mesh circuit breakers + shared memory (Stage 4.6 / 4.7) ----
  // Both endpoints return untyped payloads, so the shapes are narrowed at the SDK
  // boundary rather than asserted blind at the call site.

  /**
   * `GET /api/mesh/circuits` answers a **map** (`{ agentId: state }`, from
   * `CircuitBreaker.GetAllStates()`), not an array. Normalising here keeps the view from
   * repeating the `Array.isArray(...) ? ... : []` mistake that silently emptied the agent
   * list — a bare array check against a map payload always fails.
   */
  async getMeshCircuits(): Promise<MeshCircuitState[]> {
    const raw = await this.request<unknown>("/api/mesh/circuits");
    if (Array.isArray(raw)) return raw as MeshCircuitState[];

    if (raw && typeof raw === "object") {
      return Object.entries(raw as Record<string, unknown>).map(([agentId, state]) => ({
        agentId,
        state: String(state),
      }));
    }

    return [];
  }

async resetMeshCircuit(agentId: string): Promise<unknown> {
  return this.request<unknown>(`/api/mesh/circuits/${encodeURIComponent(agentId)}/reset`, {
    method: "POST",
  });
}

// ---- Mesh agent registry actions (Stage 4.4 context menu) ----

/** Heartbeat: refreshes the peer's last-seen so its TTL does not lapse. */
async touchMeshAgent(agentId: string): Promise<unknown> {
  return this.request<unknown>(`/api/mesh/agents/${encodeURIComponent(agentId)}/touch`, {
    method: "POST",
  });
}

/** Drops a peer from the capability registry. Destructive — callers must confirm. */
async removeMeshAgent(agentId: string): Promise<unknown> {
  return this.request<unknown>(`/api/mesh/agents/${encodeURIComponent(agentId)}`, {
    method: "DELETE",
  });
}

async getSharedFacts(): Promise<SharedFact[]> {
  const raw = await this.request<unknown>("/api/mesh/shared-memory");
  return Array.isArray(raw) ? (raw as SharedFact[]) : [];
}

async publishSharedFact(body: {
  category: string;
  content: string;
  allowedAgents?: string[];
}): Promise<SharedFact> {
  return this.request<SharedFact>("/api/mesh/shared-memory", { method: "POST", body });
}

async syncSharedMemory(): Promise<{ status?: string; receivedCount?: number }> {
  return this.request<{ status?: string; receivedCount?: number }>("/api/mesh/shared-memory/sync", {
    method: "POST",
  });
}

async deleteSharedFact(factId: string): Promise<unknown> {
  return this.request<unknown>(`/api/mesh/shared-memory/${encodeURIComponent(factId)}`, {
    method: "DELETE",
  });
}

/** Stage 4.5 — semantic lookup over the capability registry. */
async searchMeshCapabilities(phrase: string): Promise<unknown[]> {
    const raw = await this.request<unknown>(
      `/api/mesh/capabilities/search?phrase=${encodeURIComponent(phrase)}`,
    );
    return Array.isArray(raw) ? raw : [];
  }

async getMeshRoutes(capability: string, maxCostUsd?: number): Promise<MeshRoutesResponseDto> {
    const q = new URLSearchParams({ capability });
    if (maxCostUsd !== undefined) q.set("maxCostUsd", String(maxCostUsd));
    return this.request<MeshRoutesResponseDto>(`/api/mesh/router/routes?${q.toString()}`);
  }

  // ---- Human-in-the-loop: approvals + escalations ----

  async getPendingApprovals(): Promise<ApprovalsResponse> {
    return this.request<ApprovalsResponse>("/api/approvals/pending");
  }

  async approveApproval(id: string): Promise<unknown> {
    return this.request(`/api/approvals/${encodeURIComponent(id)}/approve`, { method: "POST" });
  }

  async denyApproval(id: string): Promise<unknown> {
    return this.request(`/api/approvals/${encodeURIComponent(id)}/deny`, { method: "POST" });
  }

  async getPendingEscalations(minSeverity?: string): Promise<EscalationsResponse> {
    const q = minSeverity ? `?minSeverity=${encodeURIComponent(minSeverity)}` : "";
    return this.request<EscalationsResponse>(`/api/escalations/pending${q}`);
  }

  async approveEscalation(id: string): Promise<unknown> {
    return this.request(`/api/escalations/${encodeURIComponent(id)}/approve`, { method: "POST" });
  }

  async denyEscalation(id: string): Promise<unknown> {
    return this.request(`/api/escalations/${encodeURIComponent(id)}/deny`, { method: "POST" });
  }

  async batchApproveEscalations(ids: string[]): Promise<unknown> {
    return this.request("/api/escalations/batch-approve", { method: "POST", body: { ids } });
  }

  // ---- Context / distillation (Stage 6.6) ----

  async getContextBudget(): Promise<ContextBudgetDto> {
    return this.request<ContextBudgetDto>("/api/context/budget");
  }

  async getContextSummary(sessionId: string): Promise<ContextSummaryDto> {
    return this.request<ContextSummaryDto>(
      `/api/context/summary?sessionId=${encodeURIComponent(sessionId)}`,
    );
  }

  async distillContext(sessionId: string, mode?: "auto" | "manual"): Promise<ContextDistillResultDto> {
    return this.request<ContextDistillResultDto>("/api/context/distill", {
      method: "POST",
      body: { sessionId, mode: mode ?? null },
    });
  }

  // ---- LLM providers (Stage 6.2) ----

  /** Secret-free view of the LLM config. API keys are never exposed here. */
  async getLlmConfig(): Promise<LlmConfigDto> {
    return this.request<LlmConfigDto>("/api/llm/config");
  }

  /** Per-provider health probe results; shape is provider-specific, kept loose. */
  async getLlmHealth(): Promise<Record<string, unknown>> {
    return this.request<Record<string, unknown>>("/api/llm/health");
  }

  // ---- Quotas (Stage 6.5) ----

  async getQuotas(scope: string, scopeId?: string): Promise<QuotaStatusResponseDto> {
    const q = new URLSearchParams({ scope });
    if (scopeId) q.set("scopeId", scopeId);
    return this.request<QuotaStatusResponseDto>(`/api/quotas?${q.toString()}`);
  }

  // ---- Tools / MCP ----

  async listTools(): Promise<ToolsResponse> {
    return this.request<ToolsResponse>("/api/tools");
  }

  async enableTool(name: string): Promise<ToolDto> {
    return this.request<ToolDto>(`/api/tools/${encodeURIComponent(name)}/enable`, { method: "POST" });
  }

  async disableTool(name: string): Promise<ToolDto> {
    return this.request<ToolDto>(`/api/tools/${encodeURIComponent(name)}/disable`, { method: "POST" });
  }

  async listMcpServers(): Promise<McpServersListResponseDto> {
    return this.request<McpServersListResponseDto>("/api/mcp/servers");
  }

  async getMcpServer(name: string): Promise<McpServerDetailDto> {
    return this.request<McpServerDetailDto>(`/api/mcp/servers/${encodeURIComponent(name)}`);
  }

  async reloadMcpServers(): Promise<McpReloadResponseDto> {
    return this.request<McpReloadResponseDto>("/api/mcp/servers/reload", { method: "POST" });
  }

  // ---- API keys and roles (Stage 6.3) ----
  // All of these require a system-role session. Keys are addressed by a
  // non-reversible fingerprint: the raw value is never returned, so a browser
  // that has never seen a key can still manage it.

  async listApiKeys(): Promise<ApiKeysListResponseDto> {
    return this.request<ApiKeysListResponseDto>("/api/auth/keys");
  }

  async createApiKey(body: CreateApiKeyRequestDto): Promise<CreatedApiKeyResponseDto> {
    return this.request<CreatedApiKeyResponseDto>("/api/auth/keys", { method: "POST", body });
  }

  async updateApiKey(fingerprint: string, body: UpdateApiKeyRequestDto): Promise<ApiKeyMutationResponseDto> {
    return this.request<ApiKeyMutationResponseDto>(
      `/api/auth/keys/${encodeURIComponent(fingerprint)}`,
      { method: "PATCH", body },
    );
  }

  async deleteApiKey(fingerprint: string): Promise<ApiKeyDeleteResponseDto> {
    return this.request<ApiKeyDeleteResponseDto>(
      `/api/auth/keys/${encodeURIComponent(fingerprint)}`,
      { method: "DELETE" },
    );
  }

  // ---- Restart (supervisor-driven) ----

  /**
   * Requests an agent restart. The agent only sets a flag; `Hercules.Supervisor`
   * polls `/api/system/restart-pending` and performs the actual kill/start.
   * Requires a system-role session.
   */
  async requestRestart(reason: string): Promise<unknown> {
    return this.request<unknown>("/api/system/restart", { method: "POST", body: { reason } });
  }

  async getRestartPending(): Promise<unknown> {
    return this.request<unknown>("/api/system/restart-pending", { auth: false });
  }

  // ---- Sandbox code runs (ADR-0009) ----

  /** Starts a sandbox run and returns its id. Execution continues in the background. */
  // ---- Consensus history (Stage 7.8) ----

async listConsensusSessions(): Promise<ConsensusSessionListDto> {
    return this.request<ConsensusSessionListDto>("/api/consensus/sessions");
  }

async saveConsensusSession(body: {
    prompt: string;
    selectedAgents: string[];
    responses: Array<{ agentName: string; connectionId: string; answer: string }>;
    aggregationMode: string;
    result?: string;
    judgeRationale?: string;
  }): Promise<ConsensusSessionDto> {
    return this.request<ConsensusSessionDto>("/api/consensus/sessions", {
      method: "POST",
      body,
    });
  }

/** Stage 4.4 — drops registry entries whose TTL has lapsed. */
async cleanupStaleAgents(): Promise<{ status?: string; removedCount?: number }> {
    return this.request<{ status?: string; removedCount?: number }>("/api/mesh/agents/cleanup", {
      method: "POST",
    });
  }

/**
 * Stage 4.4 — registers a peer from its manifest URL.
 *
 * The endpoint takes an `AgentManifest` body rather than a URL, so the manifest is
 * fetched first. It is a well-known discovery document, fetched cross-origin, so a
 * failure here is expected and reported rather than swallowed.
 */
async registerPeerFromUrl(url: string): Promise<unknown> {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`Manifest ${url} returned ${res.status}`);
    const manifest = await res.json();
    return this.request<unknown>("/api/mesh/agents/register", { method: "POST", body: manifest });
  }

/**
 * Scan-only sandbox check (Stage 3.5). Uses the same DangerousCodeScanner and
 * SandboxOptions the executors use, so the verdict matches what execution would enforce.
 */
async scanCode(code: string): Promise<CodeScanResponseDto> {
    return this.request<CodeScanResponseDto>("/api/code/scan", {
      method: "POST",
      body: { code },
      timeoutMs: 15_000,
    });
  }

  async startCodeRun(req: {
    code: string;
    language?: string;
    args?: string[];
    timeoutMs?: number;
    skillId?: string;
  }): Promise<{ runId: string }> {
    return this.request<{ runId: string }>("/api/code/run", { method: "POST", body: req, timeoutMs: 15_000 });
  }

  async getCodeRun(runId: string): Promise<CodeRunStatus> {
    return this.request<CodeRunStatus>(`/api/code/run/${encodeURIComponent(runId)}`);
  }

  /**
   * Subscribes to a run's SSE output.
   *
   * Uses fetch + a manual parser rather than EventSource because the stream is
   * authenticated with `X-Session-Token`, which EventSource cannot send.
   *
   * Returns an unsubscribe function.
   */
  streamCodeRun(
    runId: string,
    onEvent: (type: string, data: unknown) => void,
    options: { after?: number; signal?: AbortSignal } = {},
  ): () => void {
    const token = this.tokenProvider();
    if (!token) throw new Error("No active session");

    const controller = new AbortController();
    if (options.signal) {
      options.signal.addEventListener("abort", () => controller.abort(), { once: true });
    }

    const cursor = options.after ?? 0;
    void (async () => {
      try {
        const res = await fetch(
          `${this.baseUrl}/api/code/run/${encodeURIComponent(runId)}/stream?after=${cursor}`,
          { headers: { Accept: "text/event-stream", "X-Session-Token": token }, signal: controller.signal },
        );
        if (!res.ok || !res.body) {
          onEvent("error", { message: `stream failed: HTTP ${res.status}` });
          return;
        }

        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true });

          // SSE frames are separated by a blank line.
          for (let split = buffer.indexOf("\n\n"); split !== -1; split = buffer.indexOf("\n\n")) {
            const frame = buffer.slice(0, split);
            buffer = buffer.slice(split + 2);

            let type = "message";
            const dataLines: string[] = [];
            for (const line of frame.split("\n")) {
              if (line.startsWith("event:")) type = line.slice(6).trim();
              else if (line.startsWith("data:")) dataLines.push(line.slice(5).trim());
            }
            if (dataLines.length === 0) continue;
            try {
              onEvent(type, JSON.parse(dataLines.join("\n")));
            } catch {
              onEvent(type, dataLines.join("\n"));
            }
          }
        }
      } catch (e) {
        if (!controller.signal.aborted) {
          onEvent("error", { message: e instanceof Error ? e.message : String(e) });
        }
      }
    })();

    return () => controller.abort();
  }
}

export interface CodeRunStatus {
  runId: string;
  skillId: string | null;
  language: string;
  status: "queued" | "running" | "completed" | "failed";
  startedAt: string;
  completedAt: string | null;
  error: string | null;
  result: {
    exitCode: number;
    stdout: string;
    stderr: string;
    durationMs: number;
    status: string;
    blockedPatterns: string[];
    sessionDir: string | null;
    success: boolean;
  } | null;
}