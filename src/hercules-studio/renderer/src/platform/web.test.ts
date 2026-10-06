import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  createWebCapabilities,
  DEFAULT_SETTINGS,
  AgentError,
  __resetSessionRegistry,
} from "./web";
import type { SessionResponse } from "./web";

const AGENT = "http://localhost:8421";

function sessionResponse(overrides: Partial<SessionResponse> = {}): SessionResponse {
  return {
    token: "sess-token-abc",
    role: "contribute",
    agentId: "agent-1",
    displayName: "Agent One",
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    capabilities: ["chat", "skills:read"],
    ...overrides,
  };
}

/** Minimal fetch stub routing the handful of paths the platform touches. */
function stubFetch(routes: Record<string, { status?: number; body?: unknown }>) {
  return vi.fn(async (input: RequestInfo | URL, _init?: RequestInit) => {
    const url = typeof input === "string" ? input : String(input);
    const path = url.replace(AGENT, "");
    const hit = routes[path];
    if (!hit) {
      return new Response(JSON.stringify({ error: "not found" }), { status: 404 });
    }
    return new Response(hit.body === undefined ? null : JSON.stringify(hit.body), {
      status: hit.status ?? 200,
      headers: { "Content-Type": "application/json" },
    });
  });
}

beforeEach(() => {
  __resetSessionRegistry();
});

describe("credential handling", () => {
  it("never persists the API key to localStorage", async () => {
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1", displayName: "Agent One" } },
      "/api/studio/session": { body: sessionResponse() },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    const conn = await caps.connections.add({
      name: "agent",
      baseUrl: AGENT,
      apiKey: "hc_contrib_SUPERSECRET",
    });

    expect(conn.hasSession).toBe(true);

    // The key must not appear anywhere in persisted state.
    const dump = JSON.stringify({
      ...localStorage,
      keys: Object.keys(localStorage),
    });
    expect(dump).not.toContain("hc_contrib_SUPERSECRET");

    for (let i = 0; i < localStorage.length; i += 1) {
      const key = localStorage.key(i);
      if (key === null) continue;
      expect(localStorage.getItem(key)).not.toContain("hc_contrib_SUPERSECRET");
    }

    vi.unstubAllGlobals();
  });

  it("keeps the session token retrievable in memory but not in storage", async () => {
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1" } },
      "/api/studio/session": { body: sessionResponse({ token: "tok-marker-zzz" }) },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    const conn = await caps.connections.add({ name: "a", baseUrl: AGENT, apiKey: "k" });

    expect(caps.session.token(conn.id)).toBe("tok-marker-zzz");
    expect(caps.session.info(conn.id)?.role).toBe("contribute");

    // token itself must not be persisted
    let persisted = "";
    for (let i = 0; i < localStorage.length; i += 1) {
      const key = localStorage.key(i);
      if (key === null) continue;
      persisted += `${key}${localStorage.getItem(key)}`;
    }
    expect(persisted).not.toContain("tok-marker-zzz");

    vi.unstubAllGlobals();
  });

  it("presents the raw API key as X-Api-Key, never as a session token", async () => {
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1" } },
      "/api/studio/session": { body: sessionResponse() },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    await caps.connections.add({ name: "a", baseUrl: AGENT, apiKey: "hc_contrib_REAL" });

    const exchangeCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith("/api/studio/session"),
    );
    expect(exchangeCall).toBeDefined();

    const init = exchangeCall?.[1] as RequestInit;
    const headers = init.headers as Record<string, string>;

    expect(headers["X-Api-Key"]).toBe("hc_contrib_REAL");
    // Regression guard: sending the key as a session token makes the agent
    // reject the exchange with 401, which surfaced only in browser E2E.
    expect(headers["X-Session-Token"]).toBeUndefined();

    vi.unstubAllGlobals();
  });

  it("surfaces an invalid key rather than storing an offline connection silently", async () => {
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1" } },
      "/api/studio/session": { status: 401, body: { error: "Invalid API key" } },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    await expect(
      caps.connections.add({ name: "a", baseUrl: AGENT, apiKey: "bad" }),
    ).rejects.toThrow(AgentError);

    vi.unstubAllGlobals();
  });

  it("notifies listeners when a session expires and forgets the token", async () => {
    vi.useFakeTimers();
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1" } },
      "/api/studio/session": {
        body: sessionResponse({ expiresAt: new Date(Date.now() + 5_000).toISOString() }),
      },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    const conn = await caps.connections.add({ name: "a", baseUrl: AGENT, apiKey: "k" });

    const seen: string[] = [];
    caps.session.onExpired((id, reason) => seen.push(`${id}:${reason}`));

    vi.advanceTimersByTime(6_000);

    expect(seen).toEqual([`${conn.id}:expired`]);
    expect(caps.session.token(conn.id)).toBeNull();

    vi.useRealTimers();
    vi.unstubAllGlobals();
  });
});

describe("connections CRUD", () => {
  it("persists connections without credential fields", async () => {
    const fetchMock = stubFetch({
      "/agent.manifest.json": { body: { agentId: "agent-1" } },
      "/api/studio/session": { body: sessionResponse() },
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    await caps.connections.add({ name: "first", baseUrl: AGENT, apiKey: "k" });

    const stored = localStorage.getItem("hercules-studio.connections.v1") ?? "";
    expect(JSON.parse(stored)).toHaveLength(1);
    expect(stored).not.toContain("apiKey");
    expect(stored).not.toContain("token");

    const list = await caps.connections.list();
    expect(list).toHaveLength(1);
    expect(list[0].name).toBe("first");

    vi.unstubAllGlobals();
  });

  it("saves an offline agent instead of failing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("Failed to fetch");
      }),
    );

    const caps = createWebCapabilities();
    const conn = await caps.connections.add({ name: "down", baseUrl: AGENT, apiKey: "k" });

    expect(conn.status).toBe("offline");
    expect(conn.hasSession).toBe(false);
    expect(await caps.connections.list()).toHaveLength(1);

    vi.unstubAllGlobals();
  });

  it("refuses to rewrite identity fields through update()", async () => {
    // Stub: without this the call hits whatever agent happens to run on 8421.
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 404 })));
    const caps = createWebCapabilities();
    await caps.connections.add({ name: "orig", baseUrl: AGENT, apiKey: "k" });
    const [conn] = await caps.connections.list();

    const patched = await caps.connections.update(conn.id, {
      name: "renamed",
      baseUrl: "http://evil.example",
    } as never);

    expect(patched.name).toBe("renamed");
    expect(patched.baseUrl).toBe(AGENT);
    vi.unstubAllGlobals();
  });

  it("rejects updates to an unknown connection", async () => {
    const caps = createWebCapabilities();
    await expect(caps.connections.update("nope", { name: "x" })).rejects.toThrow(AgentError);
  });

  it("reports health through the public endpoint and caches the result", async () => {
    const fetchMock = stubFetch({ "/api/health": { body: { status: "ok" } } });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    await caps.connections.add({ name: "a", baseUrl: AGENT, apiKey: "k" });
    const [conn] = await caps.connections.list();

    const health = await caps.connections.healthCheck(conn.id);
    expect(health.online).toBe(true);

    const list = await caps.connections.list();
    expect(list[0].status).toBe("online");

    vi.unstubAllGlobals();
  });
});

describe("discovery", () => {
  it("probes /agent-card.json and flags agents that require auth", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === "http://localhost:8421/agent.manifest.json") {
        return new Response(JSON.stringify({ agentId: "a1", displayName: "A1", version: "1.0" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (url === "http://127.0.0.1:8421/agent.manifest.json") {
        return new Response(null, { status: 401 });
      }
      return new Response(null, { status: 404 });
    });
    vi.stubGlobal("fetch", fetchMock);

    const caps = createWebCapabilities();
    const found = await caps.scanner.discover();

    const byUrl = Object.fromEntries(found.map((f) => [f.baseUrl, f]));
    expect(byUrl[AGENT]).toMatchObject({
      agentId: "a1",
      displayName: "A1",
      authRequired: false,
      foundVia: "wellknown",
    });
    expect(byUrl["http://127.0.0.1:8421"]).toMatchObject({ authRequired: true });
    expect(found.some((f) => f.baseUrl === "http://localhost:5000")).toBe(false);

    vi.unstubAllGlobals();
  });

  it("emits progress and returns an unsubscribe function", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 404 })));

    const caps = createWebCapabilities();
    const seen: number[] = [];
    const off = caps.scanner.onProgress((p) => seen.push(p.scanned));

    await caps.scanner.discover();
    expect(seen.length).toBeGreaterThan(0);

    const countAfter = seen.length;
    off();
    await caps.scanner.discover();
    expect(seen.length).toBe(countAfter);

    vi.unstubAllGlobals();
  });
});

describe("files", () => {
  it("reports File System Access API as unsupported when absent", () => {
    const caps = createWebCapabilities();
    expect(caps.files.isSupported()).toBe(false);
  });

  it("falls back to a download when showSaveFilePicker is missing", async () => {
    const caps = createWebCapabilities();
    const created: string[] = [];
    const originalCreate = URL.createObjectURL;
    URL.createObjectURL = vi.fn(() => {
      created.push("blob");
      return "blob:mock";
    }) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = vi.fn() as unknown as typeof URL.revokeObjectURL;

    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    const result = await caps.files.save("notes.md", "# hello");

    expect(result).toBe(true);
    expect(created).toEqual(["blob"]);
    expect(clickSpy).toHaveBeenCalledOnce();

    URL.createObjectURL = originalCreate;
    vi.unstubAllGlobals();
  });

  it("rejects pickAndRead with a clear error when unsupported", async () => {
    const caps = createWebCapabilities();
    await expect(caps.files.pickAndRead()).rejects.toThrow(/File System Access/);
  });
});

describe("settings", () => {
  it("returns defaults when nothing is stored", async () => {
    const caps = createWebCapabilities();
    expect(await caps.settings.get()).toEqual(DEFAULT_SETTINGS);
  });

  it("merges partial updates without dropping unrelated fields", async () => {
    const caps = createWebCapabilities();
    const after = await caps.settings.update({ theme: "light" });
    expect(after.theme).toBe("light");
    expect(after.locale).toBe(DEFAULT_SETTINGS.locale);
    expect(after.scan.endpoints).toEqual(DEFAULT_SETTINGS.scan.endpoints);
  });

  it("normalises a legacy port-range scan config into endpoints", async () => {
    localStorage.setItem(
      "hercules-studio.state.v1",
      JSON.stringify({
        theme: "dark",
        scan: { portStart: 8421, portEnd: 8521, legacyPort: 5000, timeoutMs: 300 },
      }),
    );

    const caps = createWebCapabilities();
    const settings = await caps.settings.get();

    // Unknown legacy keys must not leak through, and endpoints must be present.
    expect(settings.scan.endpoints).toEqual(DEFAULT_SETTINGS.scan.endpoints);
    expect(settings.scan).not.toHaveProperty("portStart");
    expect(settings.scan).not.toHaveProperty("enableProcessScan");
  });
});

describe("storage resilience", () => {
  it("falls back to defaults on corrupt JSON instead of throwing", async () => {
    localStorage.setItem("hercules-studio.state.v1", "{not json");
    const caps = createWebCapabilities();
    await expect(caps.settings.get()).resolves.toEqual(DEFAULT_SETTINGS);
  });

  it("ignores a corrupt connections blob", async () => {
    localStorage.setItem("hercules-studio.connections.v1", "<<<");
    const caps = createWebCapabilities();
    await expect(caps.connections.list()).resolves.toEqual([]);
  });

  it("migrates the legacy Electron-era blob and strips key fields", async () => {
    localStorage.setItem(
      "hercules-studio-mock",
      JSON.stringify({
        connections: [
          {
            id: "old-1",
            name: "legacy",
            baseUrl: "http://localhost:8421",
            agentId: "a",
            displayName: "Legacy",
            status: "online",
            apiKey: "hc_contrib_LEGACYSECRET",
          },
        ],
        consent: { consentVersion: 1, acceptedAt: "2026-01-01T00:00:00Z", type: "nonprofit", licenseKey: null },
        settings: { theme: "light" },
      }),
    );

    const caps = createWebCapabilities();

    const list = await caps.connections.list();
    expect(list).toHaveLength(1);
    expect(list[0].id).toBe("old-1");
    expect(list[0].hasSession).toBe(false);

    // Legacy secret must be dropped, not carried over.
    expect(localStorage.getItem("hercules-studio.connections.v1")).not.toContain("LEGACYSECRET");
    // Old blob is consumed.
    expect(localStorage.getItem("hercules-studio-mock")).toBeNull();

    expect(await caps.license.getConsent()).toMatchObject({ type: "nonprofit" });
    expect((await caps.settings.get()).theme).toBe("light");
  });

  it("does not clobber existing v1 state during migration", async () => {
    localStorage.setItem("hercules-studio.state.v1", JSON.stringify({ theme: "dark", locale: "ru" }));
    localStorage.setItem(
      "hercules-studio-mock",
      JSON.stringify({ settings: { theme: "light" }, connections: [] }),
    );

    const caps = createWebCapabilities();
    const settings = await caps.settings.get();
    expect(settings.locale).toBe("ru");
    expect(localStorage.getItem("hercules-studio-mock")).toBeNull();
  });
});

describe("license + app", () => {
  it("round-trips license consent", async () => {
    const caps = createWebCapabilities();
    expect(await caps.license.getConsent()).toBeNull();
    await caps.license.acceptConsent("commercial", "LIC-123");
    expect(await caps.license.getConsent()).toMatchObject({ type: "commercial", licenseKey: "LIC-123" });
  });

  it("returns a stable non-secret studio id", () => {
    const caps = createWebCapabilities();
    const id = caps.app.studioId();
    expect(id).toMatch(/^studio-/);
    expect(caps.app.studioId()).toBe(id);
    expect(JSON.stringify(localStorage)).toContain(id);
  });
});