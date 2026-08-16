import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import { safeStorage } from "electron";
import type { Connection, NewConnection, HealthStatus, ConnectionStatus } from "@shared/protocol";

let connectionsPath = "";
let keysPath = "";
let activeConnectionId: string | null = null;

// In-memory state
let connections: Connection[] = [];

interface StoredConnection {
  id: string;
  name: string;
  baseUrl: string;
  agentId: string;
  displayName: string;
  authScheme: string;
  lastSeen: string | null;
  hasSystemKey: boolean;
}

export function initConnections(userDataPath: string): void {
  connectionsPath = join(userDataPath, "connections.json");
  keysPath = join(userDataPath, "api-keys.enc");

  if (existsSync(connectionsPath)) {
    try {
      const data = readFileSync(connectionsPath, "utf-8");
      const stored = JSON.parse(data) as StoredConnection[];
      connections = stored.map((s) => ({
        ...s,
        status: "disconnected" as ConnectionStatus,
        checkedOut: false,
        checkedOutBy: null,
      }));
    } catch {
      connections = [];
    }
  }
}

function saveConnections(): void {
  const stored: StoredConnection[] = connections.map((c) => ({
    id: c.id,
    name: c.name,
    baseUrl: c.baseUrl,
    agentId: c.agentId,
    displayName: c.displayName,
    authScheme: c.authScheme,
    lastSeen: c.lastSeen,
    hasSystemKey: c.hasSystemKey,
  }));
  writeFileSync(connectionsPath, JSON.stringify(stored, null, 2), "utf-8");
}

// --- API key storage via safeStorage ---

export function encryptKey(connectionId: string, key: string, role: "contribute" | "system"): void {
  if (!safeStorage.isEncryptionAvailable()) {
    // Fallback: store in plaintext file (dev only, warn in console)
    console.warn("[safeStorage] Encryption not available, storing key in plaintext (dev mode)");
    const fallbackPath = `${keysPath}.plain`;
    let store: Record<string, Record<string, string>> = {};
    if (existsSync(fallbackPath)) {
      store = JSON.parse(readFileSync(fallbackPath, "utf-8"));
    }
    if (!store[connectionId]) store[connectionId] = {};
    store[connectionId][role] = key;
    writeFileSync(fallbackPath, JSON.stringify(store, null, 2), "utf-8");
    return;
  }
  const encrypted = safeStorage.encryptString(key);
  const store: Record<string, Record<string, string>> = {};
  if (existsSync(keysPath)) {
    const raw = readFileSync(keysPath, "utf-8");
    Object.assign(store, JSON.parse(raw));
  }
  if (!store[connectionId]) store[connectionId] = {};
  store[connectionId][role] = encrypted.toString("base64");
  writeFileSync(keysPath, JSON.stringify(store, null, 2), "utf-8");
}

export function decryptKey(connectionId: string, role: "contribute" | "system"): string | null {
  const tryPath = existsSync(keysPath) ? keysPath : `${keysPath}.plain`;
  if (!existsSync(tryPath)) return null;
  const raw = readFileSync(tryPath, "utf-8");
  const store = JSON.parse(raw) as Record<string, Record<string, string>>;
  const entry = store[connectionId]?.[role];
  if (!entry) return null;

  if (safeStorage.isEncryptionAvailable() && tryPath === keysPath) {
    return safeStorage.decryptString(Buffer.from(entry, "base64"));
  }
  return entry;
}

// --- Public API ---

export async function addConnection(conn: NewConnection): Promise<Connection> {
  // Validate via health check
  const health = await probeHealth(conn.baseUrl, conn.apiKey);

  const connection: Connection = {
    id: randomUUID(),
    name: conn.name,
    baseUrl: conn.baseUrl,
    agentId: health.agentId ?? "unknown",
    displayName: health.displayName ?? conn.name,
    authScheme: "apikey",
    lastSeen: new Date().toISOString(),
    status: health.online ? "online" : "offline",
    checkedOut: false,
    checkedOutBy: null,
    hasSystemKey: conn.systemKey != null && conn.systemKey.length > 0,
  };

  // Store keys
  encryptKey(connection.id, conn.apiKey, "contribute");
  if (conn.systemKey) {
    encryptKey(connection.id, conn.systemKey, "system");
  }

  connections.push(connection);
  saveConnections();

  return connection;
}

export function listConnections(): Connection[] {
  return connections;
}

export function removeConnection(id: string): void {
  connections = connections.filter((c) => c.id !== id);
  saveConnections();
  if (activeConnectionId === id) {
    activeConnectionId = null;
  }
}

export function updateConnection(id: string, patch: Partial<Connection>): Connection {
  const idx = connections.findIndex((c) => c.id === id);
  if (idx === -1) {
    throw new Error(`Connection ${id} not found`);
  }
  connections[idx] = { ...connections[idx], ...patch };
  saveConnections();
  return connections[idx];
}

export async function healthCheck(id: string): Promise<HealthStatus> {
  const idx = connections.findIndex((c) => c.id === id);
  if (idx === -1) {
    return { online: false, agentId: null, displayName: null, latencyMs: null, error: "Connection not found" };
  }
  const conn = connections[idx];
  const key = decryptKey(id, "contribute");
  const health = await probeHealth(conn.baseUrl, key ?? "");
  connections[idx].status = health.online ? "online" : "offline";
  connections[idx].lastSeen = new Date().toISOString();
  saveConnections();
  return health;
}

export function setActive(id: string): void {
  activeConnectionId = id;
}

export function getActiveId(): string | null {
  return activeConnectionId;
}

export function getApiKey(connectionId: string, role: "contribute" | "system" = "contribute"): string | null {
  return decryptKey(connectionId, role);
}

// --- Health probe ---

async function probeHealth(
  baseUrl: string,
  apiKey: string,
): Promise<HealthStatus> {
  const start = Date.now();
  try {
    // Try manifest first (requires API key)
    const res = await fetch(`${baseUrl}/agent.manifest.json`, {
      headers: { "X-Api-Key": apiKey },
      signal: AbortSignal.timeout(3000),
    });

    if (res.status === 401) {
      return {
        online: false,
        agentId: null,
        displayName: null,
        latencyMs: Date.now() - start,
        error: "Authentication failed (401). Check API key.",
      };
    }

    if (!res.ok) {
      return {
        online: false,
        agentId: null,
        displayName: null,
        latencyMs: Date.now() - start,
        error: `HTTP ${res.status}`,
      };
    }

    const manifest = (await res.json()) as { agentId?: string; displayName?: string };
    return {
      online: true,
      agentId: manifest.agentId ?? null,
      displayName: manifest.displayName ?? null,
      latencyMs: Date.now() - start,
      error: null,
    };
  } catch (e) {
    return {
      online: false,
      agentId: null,
      displayName: null,
      latencyMs: null,
      error: e instanceof Error ? e.message : String(e),
    };
  }
}