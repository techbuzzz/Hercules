/**
 * Web platform implementation.
 *
 * Everything here must run in a plain browser tab: no Node built-ins, no native
 * modules, no local database. Credentials are held in a module-level Map that
 * dies with the tab — nothing secret is ever written to localStorage.
 */

import type {
  Connection,
  ConnectionStatus,
  DiscoveredAgent,
  HealthStatus,
  LicenseConsent,
  NewConnection,
  PlatformCapabilities,
  ScanProgress,
  ScanSettings,
  SessionExpiredReason,
  SessionInfo,
  StudioSettings,
  WorkflowCredentials,
  WorkflowDetailDto,
  WorkflowListDto,
} from "./capabilities";

// ---- Storage keys (versioned) ----

const KEY_SETTINGS = "hercules-studio.state.v1";
const KEY_CONNECTIONS = "hercules-studio.connections.v1";
const KEY_LICENSE = "hercules-studio.license.v1";
/** Non-secret client identifier for CheckIn/CheckOut. */
const KEY_STUDIO_ID = "hercules-studio.identity.v1";
/** Pre-migration blob written by the removed Electron-era mock API. */
const LEGACY_KEY = "hercules-studio-mock";

export const DEFAULT_SCAN_SETTINGS: ScanSettings = {
  endpoints: ["http://localhost:8421", "http://127.0.0.1:8421", "http://localhost:5000"],
  enableRemoteDiscovery: true,
  timeoutMs: 3000,
};

export const DEFAULT_SETTINGS: StudioSettings = {
  theme: "dark",
  locale: "en",
  typingEffect: true,
  compactMode: false,
  scan: { ...DEFAULT_SCAN_SETTINGS },
  notifications: {
    enabled: true,
    consensus: true,
    workflow: true,
    escalation: true,
    chat: false,
  },
  autoUpdate: true,
  minimizeToTray: false,
};

// ---- Safe storage helpers ----

/**
 * Reads and parses a versioned key. Corrupt data must never break startup:
 * we fall back to the default and surface a warning instead of throwing.
 */
function readJson<T>(key: string, fallback: T, onCorrupt?: (key: string) => void): T {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return fallback;
    return JSON.parse(raw) as T;
  } catch {
    onCorrupt?.(key);
    return fallback;
  }
}

function writeJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Quota or private-mode failure — non-fatal, state stays in memory only.
  }
}

// ---- Session registry (memory only) ----

interface SessionRecord {
  info: SessionInfo;
  token: string;
}

const sessions = new Map<string, SessionRecord>();
const expiryTimers = new Map<string, ReturnType<typeof setTimeout>>();
const expiryListeners = new Set<(connectionId: string, reason: SessionExpiredReason) => void>();

function scheduleExpiry(record: SessionRecord): void {
  const existing = expiryTimers.get(record.info.connectionId);
  if (existing) clearTimeout(existing);

  const ttl = new Date(record.info.expiresAt).getTime() - Date.now();
  if (!Number.isFinite(ttl) || ttl <= 0) {
    // Already expired (clock skew or short TTL) — surface on next read.
    return;
  }
  // setTimeout caps at ~24.8 days; re-arm for longer horizons.
  const delay = Math.min(ttl, 2_147_483_000);
  const timer = setTimeout(() => {
    const current = sessions.get(record.info.connectionId);
    if (current && current.token === record.token) {
      dropSession(record.info.connectionId, "expired");
    }
  }, delay);
  expiryTimers.set(record.info.connectionId, timer);
}

function dropSession(connectionId: string, reason: SessionExpiredReason): void {
  const timer = expiryTimers.get(connectionId);
  if (timer) {
    clearTimeout(timer);
    expiryTimers.delete(connectionId);
  }
  if (!sessions.delete(connectionId)) return;
  for (const listener of expiryListeners) {
    try {
      listener(connectionId, reason);
    } catch {
      /* a broken listener must not block the others */
    }
  }
}

// ---- HTTP helper ----

export interface AgentRequestInit {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  /** Session token (from `session.exchange`). Sent as `X-Session-Token`. */
  token?: string | null;
  /** Raw API key. Only ever valid on the one-shot exchange call. Sent as `X-Api-Key`. */
  apiKey?: string;
  timeoutMs?: number;
}

async function agentFetch<T>(baseUrl: string, path: string, init: AgentRequestInit = {}): Promise<T> {
  const { method = "GET", body, token, apiKey, timeoutMs = 10000 } = init;
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (token) headers["X-Session-Token"] = token;
  if (apiKey) headers["X-Api-Key"] = apiKey;

  let res: Response;
  try {
    res = await fetch(`${baseUrl.replace(/\/$/, "")}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
  } catch (e) {
    throw new AgentError(
      e instanceof Error && e.name === "TimeoutError" ? `Timeout after ${timeoutMs}ms` : "Network unreachable",
      0,
    );
  }

  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const payload = await res.json();
      if (payload?.error) message = String(payload.error);
    } catch {
      /* keep status-based message */
    }
    throw new AgentError(message, res.status);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export class AgentError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "AgentError";
  }
}

// ---- Legacy migration ----

interface LegacyBlob {
  connections?: Connection[];
  consent?: LicenseConsent | null;
  settings?: Partial<StudioSettings>;
}

/**
 * One-shot migration from the removed mock storage. Strips anything that is not
 * an explicit `Connection` field so an old blob cannot reintroduce key fields.
 */
function migrateLegacy(): void {
  let legacy: LegacyBlob;
  try {
    const raw = localStorage.getItem(LEGACY_KEY);
    if (!raw) return;
    legacy = JSON.parse(raw) as LegacyBlob;
  } catch {
    localStorage.removeItem(LEGACY_KEY);
    return;
  }

  try {
    if (Array.isArray(legacy.connections) && !localStorage.getItem(KEY_CONNECTIONS)) {
      const cleaned = legacy.connections.map((c) => ({
        id: String(c.id ?? crypto.randomUUID()),
        name: String(c.name ?? "agent"),
        baseUrl: String(c.baseUrl ?? ""),
        agentId: String(c.agentId ?? "unknown"),
        displayName: String(c.displayName ?? c.name ?? "agent"),
        authScheme: "apikey",
        lastSeen: c.lastSeen ?? null,
        status: (c.status ?? "offline") as ConnectionStatus,
        checkedOut: false,
        checkedOutBy: null,
        hasSession: false,
      }));
      writeJson(KEY_CONNECTIONS, cleaned);
    }

    if (legacy.consent && !localStorage.getItem(KEY_LICENSE)) {
      writeJson(KEY_LICENSE, legacy.consent);
    }

    if (legacy.settings && !localStorage.getItem(KEY_SETTINGS)) {
      writeJson(KEY_SETTINGS, { ...DEFAULT_SETTINGS, ...legacy.settings });
    }
  } finally {
    localStorage.removeItem(LEGACY_KEY);
  }
}

// ---- Discovery ----

interface AgentCardShape {
  agentId?: string;
  displayName?: string;
  name?: string;
  version?: string;
  url?: string;
}

async function probeWellKnown(endpoint: string, timeoutMs: number): Promise<DiscoveredAgent | null> {
  const base = endpoint.replace(/\/$/, "");
  try {
    const res = await fetch(`${base}/agent.manifest.json`, {
      signal: AbortSignal.timeout(timeoutMs),
      headers: { Accept: "application/json" },
    });
    if (res.status === 401 || res.status === 403) {
      // Agent is up but requires auth — report it so the UI can prompt for a key.
      return {
        agentId: null,
        displayName: null,
        endpoint: `${base}/agent.manifest.json`,
        baseUrl: base,
        version: null,
        authRequired: true,
        foundVia: "wellknown",
      };
    }
    if (!res.ok) return null;

    const card = (await res.json()) as AgentCardShape;
    return {
      agentId: card.agentId ?? null,
      displayName: card.displayName ?? card.name ?? null,
      endpoint: `${base}/agent.manifest.json`,
      baseUrl: base,
      version: card.version ?? null,
      authRequired: false,
      foundVia: "wellknown",
    };
  } catch {
    return null;
  }
}

// ---- Progress fan-out ----

const progressListeners = new Set<(p: ScanProgress) => void>();

function emitProgress(p: ScanProgress): void {
  for (const listener of progressListeners) {
    try {
      listener(p);
    } catch {
      /* isolate listener failures */
    }
  }
}

/**
 * Rebuilds a ScanSettings from an untrusted blob.
 *
 * A spread merge is not safe here: the legacy Electron-era config used
 * portStart/portEnd/legacyPort/enableProcessScan, and those keys would survive
 * into the new shape. Only known keys are read.
 */
function normalizeScan(raw: unknown): ScanSettings {
  if (!raw || typeof raw !== "object") return { ...DEFAULT_SCAN_SETTINGS };
  const s = raw as Partial<ScanSettings>;
  return {
    endpoints: Array.isArray(s.endpoints) && s.endpoints.length > 0 ? [...s.endpoints] : [...DEFAULT_SCAN_SETTINGS.endpoints],
    enableRemoteDiscovery: typeof s.enableRemoteDiscovery === "boolean" ? s.enableRemoteDiscovery : DEFAULT_SCAN_SETTINGS.enableRemoteDiscovery,
    timeoutMs: typeof s.timeoutMs === "number" && s.timeoutMs > 0 ? s.timeoutMs : DEFAULT_SCAN_SETTINGS.timeoutMs,
  };
}

function normalizeNotifications(raw: unknown): StudioSettings["notifications"] {
  if (!raw || typeof raw !== "object") return { ...DEFAULT_SETTINGS.notifications };
  const n = raw as Partial<StudioSettings["notifications"]>;
  return {
    enabled: typeof n.enabled === "boolean" ? n.enabled : DEFAULT_SETTINGS.notifications.enabled,
    consensus: typeof n.consensus === "boolean" ? n.consensus : DEFAULT_SETTINGS.notifications.consensus,
    workflow: typeof n.workflow === "boolean" ? n.workflow : DEFAULT_SETTINGS.notifications.workflow,
    escalation: typeof n.escalation === "boolean" ? n.escalation : DEFAULT_SETTINGS.notifications.escalation,
    chat: typeof n.chat === "boolean" ? n.chat : DEFAULT_SETTINGS.notifications.chat,
  };
}

// ---- Workflow server access (separate service, own credentials) ----

let workflowCreds: WorkflowCredentials | null = null;

async function workflowFetch<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  if (!workflowCreds) throw new AgentError("Workflow server is not configured", 0);
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (workflowCreds.clientId) headers["X-Client-Id"] = workflowCreds.clientId;
  if (workflowCreds.clientSecret) headers["X-Client-Secret"] = workflowCreds.clientSecret;

  const res = await fetch(`${workflowCreds.baseUrl}${path}`, {
    method: init.method ?? "GET",
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
    signal: AbortSignal.timeout(15_000),
  });

  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { error?: string; detail?: string; title?: string };
      message = body.error ?? body.detail ?? body.title ?? message;
    } catch {
      /* keep status-based message */
    }
    throw new AgentError(message, res.status);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

// ---- Capability implementation ----

export function createWebCapabilities(): PlatformCapabilities {
  migrateLegacy();

  const loadConnections = (): Connection[] => readJson<Connection[]>(KEY_CONNECTIONS, []);
  const saveConnections = (list: Connection[]): void => writeJson(KEY_CONNECTIONS, list);

  return {
    connections: {
      async list(): Promise<Connection[]> {
        return loadConnections();
      },

      async add(conn: NewConnection): Promise<Connection> {
        const baseUrl = conn.baseUrl.replace(/\/$/, "");
        if (!baseUrl) throw new AgentError("baseUrl is required", 0);

        // Exchange the key for a session token now. A failure here is surfaced to
        // the caller but the connection is still saved, so an offline agent can
        // be configured before it is started.
        let agentId = "unknown";
        let displayName = conn.name;
        let status: ConnectionStatus = "offline";
        let authenticated = false;

        const id = crypto.randomUUID();
        try {
          const probe = await probeWellKnown(baseUrl, 3000);
          if (probe && !probe.authRequired) {
            agentId = probe.agentId ?? "unknown";
            displayName = probe.displayName ?? conn.name;
          }
          const session = await exchangeSession(id, baseUrl, conn.apiKey);
          agentId = session.agentId || agentId;
          displayName = session.displayName || displayName;
          status = "online";
          authenticated = true;
        } catch (e) {
          status = "offline";
          if (e instanceof AgentError && (e.status === 401 || e.status === 403)) {
            throw new AgentError("Invalid API key", e.status);
          }
        }

        const connection: Connection = {
          id,
          name: conn.name,
          baseUrl,
          agentId,
          displayName,
          authScheme: "session",
          lastSeen: new Date().toISOString(),
          status,
          checkedOut: false,
          checkedOutBy: null,
          hasSession: authenticated,
        };

        const list = loadConnections();
        list.push(connection);
        saveConnections(list);
        return connection;
      },

      async remove(id: string): Promise<void> {
        dropSession(id, "invalidated");
        saveConnections(loadConnections().filter((c) => c.id !== id));
      },

      async update(id: string, patch: Partial<Connection>): Promise<Connection> {
        const list = loadConnections();
        const idx = list.findIndex((c) => c.id === id);
        if (idx === -1) throw new AgentError("Connection not found", 404);
        // Identity and credential fields are not client-writable.
        const { id: _i, baseUrl: _b, ...safe } = patch;
        list[idx] = { ...list[idx], ...safe };
        saveConnections(list);
        return list[idx];
      },

      async healthCheck(id: string): Promise<HealthStatus> {
        const conn = loadConnections().find((c) => c.id === id);
        if (!conn) {
          return { online: false, agentId: null, displayName: null, latencyMs: null, error: "Not found" };
        }

        // Public health endpoint — no token required.
        const started = Date.now();
        try {
          const res = await fetch(`${conn.baseUrl}/api/health`, {
            signal: AbortSignal.timeout(3000),
          });
          const latencyMs = Date.now() - started;
          const health: HealthStatus = {
            online: res.ok,
            agentId: conn.agentId,
            displayName: conn.displayName,
            latencyMs,
            error: res.ok ? null : `HTTP ${res.status}`,
          };
          patchStatus(id, health);
          return health;
        } catch (e) {
          const health: HealthStatus = {
            online: false,
            agentId: conn.agentId,
            displayName: conn.displayName,
            latencyMs: null,
            error: e instanceof Error ? e.message : String(e),
          };
          patchStatus(id, health);
          return health;
        }
      },

      async setActive(): Promise<void> {
        // Active-connection selection is UI state, owned by the connections store.
      },
    },

    scanner: {
      async discover(): Promise<DiscoveredAgent[]> {
        const settings = readJson<StudioSettings>(KEY_SETTINGS, DEFAULT_SETTINGS);
        const known = loadConnections().map((c) => c.baseUrl);
        const endpoints = [...new Set([...known, ...settings.scan.endpoints])];

        const found: DiscoveredAgent[] = [];
        let scanned = 0;

        // Bounded concurrency: browsers cap ~6 connections per host anyway.
        const queue = [...endpoints];
        const workers = Array.from({ length: Math.min(4, queue.length) }, async () => {
          for (;;) {
            const endpoint = queue.shift();
            if (!endpoint) return;
            scanned += 1;
            emitProgress({ scanned, total: endpoints.length, found: found.length, currentEndpoint: endpoint });
            const hit = await probeWellKnown(endpoint, settings.scan.timeoutMs);
            if (hit) found.push(hit);
          }
        });
        await Promise.all(workers);

        emitProgress({ scanned, total: endpoints.length, found: found.length, currentEndpoint: "" });
        return found;
      },

      onProgress(cb: (p: ScanProgress) => void): () => void {
        progressListeners.add(cb);
        return () => progressListeners.delete(cb);
      },
    },

    files: {
      isSupported(): boolean {
        return typeof window !== "undefined" && "showOpenFilePicker" in window;
      },

      async pickAndRead(accept): Promise<{ name: string; content: string } | null> {
        if (!this.isSupported()) {
          throw new AgentError("File System Access API unavailable in this browser", 0);
        }
        const picker = (
          window as unknown as {
            showOpenFilePicker: (opts?: unknown) => Promise<Array<{ name: string; getFile(): Promise<File> }>>;
          }
        ).showOpenFilePicker;
        let handles: Array<{ name: string; getFile(): Promise<File> }>;
        try {
          handles = await picker({ multiple: false, types: accept ? [{ description: "Files", accept }] : undefined });
        } catch (e) {
          // AbortError === user cancelled.
          if (e instanceof DOMException && e.name === "AbortError") return null;
          throw e;
        }
        if (!handles?.[0]) return null;
        const file = await handles[0].getFile();
        return { name: handles[0].name, content: await file.text() };
      },

      async save(suggestedName: string, content: string): Promise<boolean> {
        const w = window as unknown as {
          showSaveFilePicker?: (opts?: unknown) => Promise<{
            createWritable: () => Promise<{ write: (d: BlobPart) => Promise<void>; close: () => Promise<void> }>;
          }>;
        };
        if (typeof w.showSaveFilePicker !== "function") {
          // Fallback for Firefox/Safari: trigger a normal download.
          const blob = new Blob([content], { type: "text/plain;charset=utf-8" });
          const url = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = url;
          a.download = suggestedName;
          a.click();
          URL.revokeObjectURL(url);
          return true;
        }
        try {
          const handle = await w.showSaveFilePicker({ suggestedName });
          const writable = await handle.createWritable();
          await writable.write(content);
          await writable.close();
          return true;
        } catch (e) {
          if (e instanceof DOMException && e.name === "AbortError") return false;
          throw e;
        }
      },
    },

    notify: {
      supported(): boolean {
        return typeof window !== "undefined" && "Notification" in window;
      },
      permission(): NotificationPermission {
        return this.supported() ? Notification.permission : "denied";
      },
      async request(): Promise<NotificationPermission> {
        if (!this.supported()) return "denied";
        return Notification.requestPermission();
      },
      async show(title: string, body: string): Promise<void> {
        if (!this.supported() || Notification.permission !== "granted") return;
        new Notification(title, { body });
      },
    },

    license: {
      async getConsent(): Promise<LicenseConsent | null> {
        return readJson<LicenseConsent | null>(KEY_LICENSE, null);
      },
      async acceptConsent(type, key): Promise<void> {
        writeJson(KEY_LICENSE, {
          consentVersion: 1,
          acceptedAt: new Date().toISOString(),
          type,
          licenseKey: key ?? null,
        } satisfies LicenseConsent);
      },
    },

    settings: {
      async get(): Promise<StudioSettings> {
        const stored = readJson<Partial<StudioSettings>>(KEY_SETTINGS, {});
        // Normalise rather than spread: unknown legacy keys must not survive.
        return {
          ...DEFAULT_SETTINGS,
          ...stored,
          scan: normalizeScan(stored.scan),
          notifications: normalizeNotifications(stored.notifications),
        };
      },
      async update(patch): Promise<StudioSettings> {
        const current = await this.get();
        const next: StudioSettings = {
          ...current,
          ...patch,
          scan: patch.scan ? normalizeScan(patch.scan) : current.scan,
          notifications: patch.notifications
            ? normalizeNotifications(patch.notifications)
            : current.notifications,
        };
        writeJson(KEY_SETTINGS, next);
        return next;
      },
    },

    session: {
      async exchange(connectionId, baseUrl, apiKey): Promise<SessionInfo> {
        return exchangeSession(connectionId, baseUrl, apiKey);
      },
      info(connectionId): SessionInfo | null {
        return sessions.get(connectionId)?.info ?? null;
      },
      token(connectionId): string | null {
        return sessions.get(connectionId)?.token ?? null;
      },
      drop(connectionId): void {
        dropSession(connectionId, "invalidated");
      },
      onExpired(cb): () => void {
        expiryListeners.add(cb);
        return () => expiryListeners.delete(cb);
      },
    },

    workflows: {
      configure(creds): void {
        // Memory only — deliberately NOT persisted, mirroring the agent key rule.
        // A reload asks the operator for the credentials again.
        workflowCreds = {
          baseUrl: creds.baseUrl.replace(/\/$/, ""),
          clientId: creds.clientId,
          clientSecret: creds.clientSecret,
        };
      },
      configured(): boolean {
        return workflowCreds !== null;
      },
      baseUrl(): string | null {
        return workflowCreds?.baseUrl ?? null;
      },
      async list(opts = {}): Promise<WorkflowListDto> {
        const query = new URLSearchParams();
        if (opts.limit) query.set("limit", String(opts.limit));
        if (opts.cursor) query.set("cursor", opts.cursor);
        const qs = query.toString();
        return workflowFetch<WorkflowListDto>(`/api/workflows${qs ? `?${qs}` : ""}`);
      },
      async get(id: string): Promise<WorkflowDetailDto> {
        return workflowFetch<WorkflowDetailDto>(`/api/workflows/${encodeURIComponent(id)}`);
      },
      async remove(id: string): Promise<void> {
        await workflowFetch(`/api/workflows/${encodeURIComponent(id)}`, { method: "DELETE" });
      },
      async run(id: string): Promise<unknown> {
        return workflowFetch(`/api/workflows/${encodeURIComponent(id)}/run`, { method: "POST" });
      },
    },

    app: {
      version(): string {
        return typeof __APP_VERSION__ === "string" ? __APP_VERSION__ : "0.0.0-dev";
      },
      studioId(): string {
        const existing = localStorage.getItem(KEY_STUDIO_ID);
        if (existing) return existing;
        const generated = `studio-${crypto.randomUUID()}`;
        try {
          localStorage.setItem(KEY_STUDIO_ID, generated);
        } catch {
          // Non-fatal: a per-tab id still works for CheckIn.
        }
        return generated;
      },
    },
  };

  function patchStatus(id: string, health: HealthStatus): void {
    const list = loadConnections();
    const idx = list.findIndex((c) => c.id === id);
    if (idx === -1) return;
    list[idx].status = health.online ? "online" : "offline";
    list[idx].lastSeen = new Date().toISOString();
    saveConnections(list);
  }
}

/** Shape returned by `POST /api/studio/session` on the agent. */
export interface SessionResponse {
  token: string;
  role: "contribute" | "system";
  agentId: string;
  displayName: string;
  expiresAt: string;
  capabilities: string[];
}

/** Exchanges a contribute key for a short-lived session token held in memory. */
async function exchangeSession(
  connectionId: string,
  baseUrl: string,
  apiKey: string,
): Promise<SessionInfo> {
  const response = await agentFetch<SessionResponse>(baseUrl, "/api/studio/session", {
    method: "POST",
    // The raw key authenticates this one call; every later request uses the token.
    apiKey,
    timeoutMs: 5000,
  });

  const record: SessionRecord = {
    info: {
      connectionId,
      role: response.role,
      agentId: response.agentId,
      displayName: response.displayName,
      expiresAt: response.expiresAt,
      capabilities: response.capabilities ?? [],
    },
    token: response.token,
  };
  sessions.set(connectionId, record);
  scheduleExpiry(record);
  return record.info;
}

/** Test seam: clears all in-memory state between specs. */
export function __resetSessionRegistry(): void {
  for (const timer of expiryTimers.values()) clearTimeout(timer);
  expiryTimers.clear();
  sessions.clear();
  progressListeners.clear();
  expiryListeners.clear();
  // Workflow credentials are memory-only; never let one leak into the next spec.
  workflowCreds = null;
}