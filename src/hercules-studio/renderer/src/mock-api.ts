// Mock IPC API for browser preview (when running without Electron).
// In production (Electron), this is replaced by preload contextBridge.

import type { IpcApi, Connection, NewConnection, DiscoveredAgent, HealthStatus, StudioSettings, LicenseConsent } from "@shared/protocol";

const STORAGE_KEY = "hercules-studio-mock";

interface MockState {
  connections: Connection[];
  consent: LicenseConsent | null;
  settings: StudioSettings;
}

function loadState(): MockState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw);
  } catch {
    /* ignore */
  }
  return {
    connections: [],
    consent: null,
    settings: {
      theme: "dark",
      locale: "en",
      typingEffect: true,
      compactMode: false,
      scan: {
        portStart: 8421,
        portEnd: 8521,
        legacyPort: 5000,
        enableProcessScan: true,
        autoScanOnStartup: false,
        concurrent: 50,
        timeoutMs: 300,
      },
      notifications: { enabled: true, consensus: true, workflow: true, escalation: true, chat: false },
      autoUpdate: true,
      minimizeToTray: false,
    },
  };
}

function saveState(state: MockState): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

let state = loadState();

// Simple ID generator
let idCounter = 0;
function genId(): string {
  idCounter++;
  return `mock-${Date.now()}-${idCounter}`;
}

const mockApi: IpcApi = {
  connections: {
    async list(): Promise<Connection[]> {
      return state.connections;
    },
    async add(conn: NewConnection): Promise<Connection> {
      // Mock health probe — try to fetch manifest
      let agentId = "unknown";
      let displayName = conn.name;
      let online = false;
      try {
        const res = await fetch(`${conn.baseUrl}/agent.manifest.json`, {
          headers: { "X-Api-Key": conn.apiKey },
          signal: AbortSignal.timeout(3000),
        });
        if (res.ok) {
          const manifest = (await res.json()) as { agentId?: string; displayName?: string };
          agentId = manifest.agentId ?? "unknown";
          displayName = manifest.displayName ?? conn.name;
          online = true;
        }
      } catch {
        // Agent not running — still add as offline
      }

      const connection: Connection = {
        id: genId(),
        name: conn.name,
        baseUrl: conn.baseUrl,
        agentId,
        displayName,
        authScheme: "apikey",
        lastSeen: new Date().toISOString(),
        status: online ? "online" : "offline",
        checkedOut: false,
        checkedOutBy: null,
        hasSystemKey: conn.systemKey != null && conn.systemKey.length > 0,
      };
      state.connections.push(connection);
      saveState(state);
      return connection;
    },
    async remove(id: string): Promise<void> {
      state.connections = state.connections.filter((c) => c.id !== id);
      saveState(state);
    },
    async update(id: string, patch: Partial<Connection>): Promise<Connection> {
      const idx = state.connections.findIndex((c) => c.id === id);
      if (idx === -1) throw new Error("Connection not found");
      state.connections[idx] = { ...state.connections[idx], ...patch };
      saveState(state);
      return state.connections[idx];
    },
    async healthCheck(id: string): Promise<HealthStatus> {
      const conn = state.connections.find((c) => c.id === id);
      if (!conn) return { online: false, agentId: null, displayName: null, latencyMs: null, error: "Not found" };
      try {
        const start = Date.now();
        const res = await fetch(`${conn.baseUrl}/agent.manifest.json`, {
          signal: AbortSignal.timeout(3000),
        });
        const health: HealthStatus = {
          online: res.ok,
          agentId: conn.agentId,
          displayName: conn.displayName,
          latencyMs: Date.now() - start,
          error: res.ok ? null : `HTTP ${res.status}`,
        };
        const idx = state.connections.findIndex((c) => c.id === id);
        if (idx !== -1) {
          state.connections[idx].status = health.online ? "online" : "offline";
          saveState(state);
        }
        return health;
      } catch (e) {
        return { online: false, agentId: null, displayName: null, latencyMs: null, error: e instanceof Error ? e.message : String(e) };
      }
    },
    async setActive(id: string): Promise<void> {
      // In browser, active is managed by Pinia store
    },
  },
  scanner: {
    async scan(): Promise<DiscoveredAgent[]> {
      // Mock scan — try common ports
      const ports = [8421, 8422, 5000];
      const results: DiscoveredAgent[] = [];
      for (const port of ports) {
        try {
          const res = await fetch(`http://localhost:${port}/agent.manifest.json`, {
            signal: AbortSignal.timeout(500),
          });
          if (res.ok) {
            const manifest = (await res.json()) as { agentId?: string; displayName?: string };
            results.push({
              port,
              agentId: manifest.agentId ?? null,
              displayName: manifest.displayName ?? null,
              endpoint: `http://localhost:${port}`,
              authRequired: false,
              foundVia: "port",
            });
          } else if (res.status === 401) {
            results.push({
              port,
              agentId: null,
              displayName: null,
              endpoint: `http://localhost:${port}`,
              authRequired: true,
              foundVia: "port",
            });
          }
        } catch {
          // Port not open
        }
      }
      return results;
    },
    onProgress() {},
    offProgress() {},
  },
  native: {
    async readFile(path: string): Promise<string> {
      return "";
    },
    async writeFile(path: string, content: string): Promise<void> {},
    async openExternal(url: string): Promise<void> {
      window.open(url, "_blank");
    },
    async showNotification(title: string, body: string): Promise<void> {
      if ("Notification" in window) {
        new Notification(title, { body });
      }
    },
    async setTray() {},
    async spawnTerminal(): Promise<number> {
      return 0;
    },
    onTerminalOutput() {},
    async killTerminal(): Promise<void> {},
  },
  db: {
    async query(): Promise<never[]> {
      return [];
    },
    async execute(): Promise<{ changes: number; lastInsertRowid: number }> {
      return { changes: 0, lastInsertRowid: 0 };
    },
  },
  license: {
    async getConsent(): Promise<LicenseConsent | null> {
      return state.consent;
    },
    async acceptConsent(type: "nonprofit" | "commercial", key?: string): Promise<void> {
      state.consent = {
        consentVersion: 1,
        acceptedAt: new Date().toISOString(),
        type,
        licenseKey: key ?? null,
      };
      saveState(state);
    },
  },
  settings: {
    async get(): Promise<StudioSettings> {
      return state.settings;
    },
    async update(patch: Partial<StudioSettings>): Promise<StudioSettings> {
      state.settings = { ...state.settings, ...patch };
      saveState(state);
      return state.settings;
    },
  },
  keys: {
    async getSystemKey(): Promise<string | null> {
      return null;
    },
    async setSystemKey(): Promise<void> {},
    async removeSystemKey(): Promise<void> {},
    async getContributeKey(connectionId: string): Promise<string | null> {
      // In browser, we can't store keys securely. Return a mock or stored key.
      return localStorage.getItem(`mock-key-${connectionId}`);
    },
  },
  app: {
    async getVersion(): Promise<string> {
      return "0.1.0";
    },
    async getDataPath(): Promise<string> {
      return "";
    },
  },
};

// Install mock API if running in browser (not Electron)
if (typeof window !== "undefined" && !window.studioAPI) {
  (window as unknown as { studioAPI: IpcApi }).studioAPI = mockApi;
}