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
  SkillDto,
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
  }): Promise<SkillDto> {
    return this.request<SkillDto>("/api/skills", { method: "POST", body: data });
  }

  async updateSkill(
    id: string,
    data: { triggers?: string[]; prompt?: string; description?: string },
  ): Promise<SkillDto> {
    return this.request<SkillDto>(`/api/skills/${encodeURIComponent(id)}`, { method: "PUT", body: data });
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

  async getMeshAgents(): Promise<unknown[]> {
    return this.request<unknown[]>("/api/mesh/agents");
  }

  async getMeshHealth(): Promise<MeshHealthDto> {
    return this.request<MeshHealthDto>("/api/mesh/health");
  }

  /** Recent policy denials, newest first. */
  async getMeshDenials(limit = 20): Promise<unknown[]> {
    return this.request<unknown[]>(`/api/mesh/denials?limit=${limit}`);
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

  async listMcpServers(): Promise<unknown> {
    return this.request<unknown>("/api/mcp/servers");
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
          let split: number;
          while ((split = buffer.indexOf("\n\n")) !== -1) {
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