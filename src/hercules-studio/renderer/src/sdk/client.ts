import type {
  AgentManifest,
  ChatResponseDto,
  ConfigDto,
  MeshDashboardDto,
  SkillDto,
  SkillDetailDto,
  StatsDto,
} from "./types";

export class HerculesClient {
  private baseUrl: string;
  private apiKey: string;
  private systemKey: string | null = null;

  constructor(baseUrl: string, apiKey: string) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.apiKey = apiKey;
  }

  setSystemKey(key: string | null) {
    this.systemKey = key;
  }

  hasSystemKey(): boolean {
    return this.systemKey !== null;
  }

  private headers(json = true): Record<string, string> {
    const h: Record<string, string> = { "X-Api-Key": this.apiKey };
    if (json) h["Content-Type"] = "application/json";
    return h;
  }

  private systemHeaders(json = true): Record<string, string> {
    if (!this.systemKey) throw new Error("System key not set");
    const h: Record<string, string> = { "X-Api-Key": this.systemKey };
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
    return res.json() as Promise<T>;
  }

  // ---- System ----

  async getHealth(): Promise<{ status: string }> {
    const res = await fetch(`${this.baseUrl}/api/health`);
    return this.handle(res);
  }

  async getManifest(): Promise<AgentManifest> {
    const res = await fetch(`${this.baseUrl}/agent.manifest.json`, {
      headers: { "X-Api-Key": this.apiKey },
    });
    return this.handle(res);
  }

  // ---- Chat ----

  async chat(message: string): Promise<ChatResponseDto> {
    const res = await fetch(`${this.baseUrl}/api/chat`, {
      method: "POST",
      headers: this.headers(),
      body: JSON.stringify({ message }),
    });
    return this.handle(res);
  }

  // ---- Skills ----

  async listSkills(): Promise<SkillDto[]> {
    return this.handle<SkillDto[]>(
      await fetch(`${this.baseUrl}/api/skills`, { headers: this.headers(false) }),
    );
  }

  async getSkill(id: string): Promise<SkillDetailDto> {
    return this.handle<SkillDetailDto>(
      await fetch(`${this.baseUrl}/api/skills/${id}`, { headers: this.headers(false) }),
    );
  }

  async createSkill(data: {
    name: string;
    trigger: string;
    prompt: string;
    description?: string;
  }): Promise<SkillDto> {
    const res = await fetch(`${this.baseUrl}/api/skills`, {
      method: "POST",
      headers: this.headers(),
      body: JSON.stringify(data),
    });
    return this.handle(res);
  }

  async updateSkill(
    id: string,
    data: { triggers?: string[]; prompt?: string; description?: string },
  ): Promise<SkillDto> {
    const res = await fetch(`${this.baseUrl}/api/skills/${id}`, {
      method: "PUT",
      headers: this.headers(),
      body: JSON.stringify(data),
    });
    return this.handle(res);
  }

  async improveSkill(id: string): Promise<SkillDto> {
    const res = await fetch(`${this.baseUrl}/api/skills/${id}/improve`, {
      method: "POST",
      headers: this.headers(false),
    });
    return this.handle(res);
  }

  async evaluateSkill(id: string): Promise<{ skillId: string; score: number; passed: boolean }> {
    const res = await fetch(`${this.baseUrl}/api/skills/${id}/evaluate`, {
      method: "POST",
      headers: this.headers(false),
    });
    return this.handle(res);
  }

  // ---- Stats ----

  async stats(): Promise<StatsDto> {
    return this.handle<StatsDto>(
      await fetch(`${this.baseUrl}/api/stats`, { headers: this.headers(false) }),
    );
  }

  // ---- Config ----

  async getConfig(): Promise<ConfigDto> {
    return this.handle<ConfigDto>(
      await fetch(`${this.baseUrl}/api/config`, { headers: this.headers(false) }),
    );
  }

  async patchConfig(patch: Record<string, unknown>): Promise<ConfigDto> {
    const res = await fetch(`${this.baseUrl}/api/config`, {
      method: "PATCH",
      headers: this.headers(),
      body: JSON.stringify(patch),
    });
    return this.handle(res);
  }

  async putConfig(body: unknown): Promise<ConfigDto> {
    const res = await fetch(`${this.baseUrl}/api/config`, {
      method: "PUT",
      headers: this.systemHeaders(),
      body: JSON.stringify(body),
    });
    return this.handle(res);
  }

  // ---- Mesh ----

  async getMeshDashboard(): Promise<MeshDashboardDto> {
    return this.handle<MeshDashboardDto>(
      await fetch(`${this.baseUrl}/api/mesh/dashboard`, { headers: this.headers(false) }),
    );
  }

  async getMeshAgents(): Promise<unknown[]> {
    return this.handle<unknown[]>(
      await fetch(`${this.baseUrl}/api/mesh/agents`, { headers: this.headers(false) }),
    );
  }
}