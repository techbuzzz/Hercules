/**
 * Platform capability contract.
 *
 * Replaces the former `IpcApi` (shared/protocol.ts), which existed only to serve
 * Electron's preload/contextBridge boundary. Studio is now a browser SPA, so the
 * surface is a plain typed object resolved by `platform/index.ts`.
 *
 * Rule: nothing in here may reference Electron, Node built-ins, or a local
 * database. If a capability cannot work in a browser, it is not part of the
 * contract — see `docs/EPIC_Hercules_Studio/adr/0009-web-first-studio.md`.
 */

// ---- Connection types ----

export interface Connection {
  id: string;
  name: string;
  baseUrl: string;
  agentId: string;
  displayName: string;
  authScheme: string;
  lastSeen: string | null;
  status: ConnectionStatus;
  checkedOut: boolean;
  checkedOutBy: string | null;
  /** True once a session token has been obtained in this tab. Never implies a stored key. */
  hasSession: boolean;
}

export type ConnectionStatus = "online" | "offline" | "degraded" | "connecting" | "disconnected";

/**
 * `apiKey` is a one-shot credential. It is exchanged for a short-lived session
 * token at add-time and is never written to storage. See `session.exchange`.
 */
export interface NewConnection {
  name: string;
  baseUrl: string;
  apiKey: string;
  systemKey?: string;
}

export interface HealthStatus {
  online: boolean;
  agentId: string | null;
  displayName: string | null;
  latencyMs: number | null;
  error: string | null;
}

// ---- Discovery types ----

/** Port/process scanning is not reachable from a browser; discovery is A2A-based. */
export interface DiscoveredAgent {
  agentId: string | null;
  displayName: string | null;
  endpoint: string;
  baseUrl: string;
  version: string | null;
  authRequired: boolean;
  foundVia: "wellknown" | "configured" | "supervisor";
}

export interface ScanProgress {
  scanned: number;
  total: number;
  found: number;
  currentEndpoint: string;
}

export interface ScanSettings {
  /** Endpoints probed via `/agent-card.json`, in order. */
  endpoints: string[];
  enableRemoteDiscovery: boolean;
  timeoutMs: number;
}

// ---- License types ----

export interface LicenseConsent {
  consentVersion: number;
  acceptedAt: string;
  type: "nonprofit" | "commercial";
  licenseKey: string | null;
}

// ---- Settings types ----

export interface StudioSettings {
  theme: "dark" | "light";
  locale: "en" | "ru";
  typingEffect: boolean;
  compactMode: boolean;
  scan: ScanSettings;
  notifications: {
    enabled: boolean;
    consensus: boolean;
    workflow: boolean;
    escalation: boolean;
    chat: boolean;
  };
  autoUpdate: boolean;
  minimizeToTray: boolean;
}

// ---- Session types (replaces the former `keys` namespace) ----

export interface SessionInfo {
  connectionId: string;
  role: "contribute" | "system";
  agentId: string;
  displayName: string;
  expiresAt: string;
  /** Authoritative actions this session may perform. */
  capabilities: string[];
}

export type SessionExpiredReason = "expired" | "revoked" | "invalidated";

// ---- Workflow server types ----

/**
 * The workflow server is a *separate* service with its own auth
 * (`X-Client-Id` / `X-Client-Secret`), not the agent's API-key/session model.
 * Secrets live in memory only, for the same reason the agent key does.
 */
export interface WorkflowCredentials {
  baseUrl: string;
  clientId: string;
  clientSecret: string;
}

export interface WorkflowSummaryDto {
  id: string;
  name: string;
  version: number;
  description: string | null;
  createdAt: string;
  updatedAt?: string | null;
}

export interface WorkflowListDto {
  items: WorkflowSummaryDto[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface WorkflowDetailDto {
  id: string;
  name: string;
  version: number;
  description: string | null;
  graphJson: unknown;
}

// ---- Platform contract ----

export interface PlatformCapabilities {
  connections: {
    list(): Promise<Connection[]>;
    /** Performs the key exchange internally; the key is never returned to the caller. */
    add(conn: NewConnection): Promise<Connection>;
    remove(id: string): Promise<void>;
    update(id: string, patch: Partial<Connection>): Promise<Connection>;
    healthCheck(id: string): Promise<HealthStatus>;
    setActive(id: string): Promise<void>;
  };
  scanner: {
    /** Probes `/agent-card.json` on known endpoints. */
    discover(): Promise<DiscoveredAgent[]>;
    /** Subscribes to progress; returns an unsubscribe function. */
    onProgress(cb: (progress: ScanProgress) => void): () => void;
  };
  files: {
    /** File System Access API. Import/export only — arbitrary paths are impossible in a browser. */
    isSupported(): boolean;
    /** Returns null when the user cancels. */
    pickAndRead(accept?: Record<string, string[]>): Promise<{ name: string; content: string } | null>;
    /** Returns false when the user cancels. */
    save(suggestedName: string, content: string): Promise<boolean>;
  };
  notify: {
    supported(): boolean;
    permission(): NotificationPermission;
    request(): Promise<NotificationPermission>;
    show(title: string, body: string): Promise<void>;
  };
  license: {
    getConsent(): Promise<LicenseConsent | null>;
    acceptConsent(type: "nonprofit" | "commercial", key?: string): Promise<void>;
  };
  settings: {
    get(): Promise<StudioSettings>;
    update(patch: Partial<StudioSettings>): Promise<StudioSettings>;
  };
  session: {
    /** Exchanges a contribute key for a short-lived token. Token lives in memory only. */
    exchange(connectionId: string, baseUrl: string, apiKey: string): Promise<SessionInfo>;
    info(connectionId: string): SessionInfo | null;
    token(connectionId: string): string | null;
    drop(connectionId: string): void;
    /** Notifies when a session expires so the UI can prompt for the key again. */
    onExpired(cb: (connectionId: string, reason: SessionExpiredReason) => void): () => void;
  };
  workflows: {
    /** Holds credentials in memory only. Never persisted. */
    configure(creds: WorkflowCredentials): void;
    configured(): boolean;
    baseUrl(): string | null;
    list(opts?: { limit?: number; cursor?: string }): Promise<WorkflowListDto>;
    get(id: string): Promise<WorkflowDetailDto>;
    remove(id: string): Promise<void>;
    run(id: string): Promise<unknown>;
  };
  app: {
    version(): string;
    /** Stable, non-secret client identifier used for CheckIn/CheckOut. Safe to persist. */
    studioId(): string;
  };
}