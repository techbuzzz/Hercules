// shared/protocol.ts — IPC contract: types shared between main and renderer.
// Renderer sees only `window.studioAPI: IpcApi` (exposed via preload contextBridge).

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
  hasSystemKey: boolean;
}

export interface NewConnection {
  name: string;
  baseUrl: string;
  apiKey: string;
  systemKey?: string;
}

export type ConnectionStatus = "online" | "offline" | "degraded" | "connecting" | "disconnected";

export interface HealthStatus {
  online: boolean;
  agentId: string | null;
  displayName: string | null;
  latencyMs: number | null;
  error: string | null;
}

// ---- Scanner types ----

export interface DiscoveredAgent {
  port: number;
  agentId: string | null;
  displayName: string | null;
  endpoint: string;
  authRequired: boolean;
  foundVia: "port" | "process";
  pid?: number;
}

export interface ScanProgress {
  scanned: number;
  total: number;
  found: number;
  currentPort: number;
}

export interface ScanSettings {
  portStart: number;
  portEnd: number;
  legacyPort: number | null;
  enableProcessScan: boolean;
  autoScanOnStartup: boolean;
  concurrent: number;
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

// ---- Native types ----

export interface TrayMenu {
  items: TrayMenuItem[];
}

export interface TrayMenuItem {
  label: string;
  action?: string;
  separator?: boolean;
  enabled?: boolean;
  checked?: boolean;
}

// ---- IPC API ----

export interface IpcApi {
  // Connections
  connections: {
    list(): Promise<Connection[]>;
    add(conn: NewConnection): Promise<Connection>;
    remove(id: string): Promise<void>;
    update(id: string, patch: Partial<Connection>): Promise<Connection>;
    healthCheck(id: string): Promise<HealthStatus>;
    setActive(id: string): Promise<void>;
  };
  // Scanner
  scanner: {
    scan(): Promise<DiscoveredAgent[]>;
    onProgress(callback: (progress: ScanProgress) => void): void;
    offProgress(): void;
  };
  // Native
  native: {
    readFile(path: string): Promise<string>;
    writeFile(path: string, content: string): Promise<void>;
    openExternal(url: string): Promise<void>;
    showNotification(title: string, body: string): Promise<void>;
    setTray(icon: string, menu: TrayMenu): Promise<void>;
    spawnTerminal(cmd: string, args: string[], cwd?: string): Promise<number>;
    onTerminalOutput(callback: (pid: number, data: string) => void): void;
    killTerminal(pid: number): Promise<void>;
  };
  // SQLite
  db: {
    query<T>(sql: string, params?: unknown[]): Promise<T[]>;
    execute(sql: string, params?: unknown[]): Promise<{ changes: number; lastInsertRowid: number | bigint }>;
  };
  // License
  license: {
    getConsent(): Promise<LicenseConsent | null>;
    acceptConsent(type: "nonprofit" | "commercial", key?: string): Promise<void>;
  };
  // Settings
  settings: {
    get(): Promise<StudioSettings>;
    update(patch: Partial<StudioSettings>): Promise<StudioSettings>;
  };
  // System keys
  keys: {
    getSystemKey(connectionId: string): Promise<string | null>;
    setSystemKey(connectionId: string, key: string): Promise<void>;
    removeSystemKey(connectionId: string): Promise<void>;
  };
  // App info
  app: {
    getVersion(): Promise<string>;
    getDataPath(): Promise<string>;
  };
}

// ---- IPC Channels (internal, used by main + preload) ----

export const IpcChannels = {
  CONNECTIONS_LIST: "connections:list",
  CONNECTIONS_ADD: "connections:add",
  CONNECTIONS_REMOVE: "connections:remove",
  CONNECTIONS_UPDATE: "connections:update",
  CONNECTIONS_HEALTH: "connections:health",
  CONNECTIONS_SET_ACTIVE: "connections:setActive",

  SCANNER_SCAN: "scanner:scan",
  SCANNER_PROGRESS: "scanner:progress",

  NATIVE_READ_FILE: "native:readFile",
  NATIVE_WRITE_FILE: "native:writeFile",
  NATIVE_OPEN_EXTERNAL: "native:openExternal",
  NATIVE_NOTIFICATION: "native:notification",
  NATIVE_TRAY: "native:tray",
  NATIVE_SPAWN: "native:spawn",
  NATIVE_TERMINAL_OUTPUT: "native:terminalOutput",
  NATIVE_KILL: "native:kill",

  DB_QUERY: "db:query",
  DB_EXECUTE: "db:execute",

  LICENSE_GET: "license:get",
  LICENSE_ACCEPT: "license:accept",

  SETTINGS_GET: "settings:get",
  SETTINGS_UPDATE: "settings:update",

  KEYS_GET_SYSTEM: "keys:getSystem",
  KEYS_SET_SYSTEM: "keys:setSystem",
  KEYS_REMOVE_SYSTEM: "keys:removeSystem",

  APP_VERSION: "app:version",
  APP_DATA_PATH: "app:dataPath",
} as const;