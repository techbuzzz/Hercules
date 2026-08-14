import { ipcMain, app, shell, Notification } from "electron";
import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import { spawn, ChildProcess } from "node:child_process";
import Database from "better-sqlite3";
import {
  IpcChannels,
  type IpcApi,
  type NewConnection,
  type Connection,
  type ScanSettings,
  type StudioSettings,
  type LicenseConsent,
  type TrayMenu,
} from "@shared/protocol";
import { getDb, initSqlite } from "./sqlite";
import { getConsent, acceptConsent, initLicense } from "./license";
import {
  listConnections,
  addConnection,
  removeConnection,
  updateConnection,
  healthCheck,
  setActive,
  initConnections,
} from "./connections";
import { scanAgents, initScanner } from "./scanner";
import { getSettings, updateSettings, initSettings } from "./settings";

const terminalProcesses = new Map<number, ChildProcess>();

export function registerIpcHandlers(): void {
  // ---- Connections ----
  ipcMain.handle(IpcChannels.CONNECTIONS_LIST, async (): Promise<Connection[]> => {
    return listConnections();
  });

  ipcMain.handle(IpcChannels.CONNECTIONS_ADD, async (_e, conn: NewConnection): Promise<Connection> => {
    return addConnection(conn);
  });

  ipcMain.handle(IpcChannels.CONNECTIONS_REMOVE, async (_e, id: string): Promise<void> => {
    return removeConnection(id);
  });

  ipcMain.handle(IpcChannels.CONNECTIONS_UPDATE, async (_e, id: string, patch: Partial<Connection>): Promise<Connection> => {
    return updateConnection(id, patch);
  });

  ipcMain.handle(IpcChannels.CONNECTIONS_HEALTH, async (_e, id: string) => {
    return healthCheck(id);
  });

  ipcMain.handle(IpcChannels.CONNECTIONS_SET_ACTIVE, async (_e, id: string): Promise<void> => {
    return setActive(id);
  });

  // ---- Scanner ----
  ipcMain.handle(IpcChannels.SCANNER_SCAN, async () => {
    return scanAgents();
  });

  // ---- Native ----
  ipcMain.handle(IpcChannels.NATIVE_READ_FILE, async (_e, path: string): Promise<string> => {
    return readFileSync(path, "utf-8");
  });

  ipcMain.handle(IpcChannels.NATIVE_WRITE_FILE, async (_e, path: string, content: string): Promise<void> => {
    writeFileSync(path, content, "utf-8");
  });

  ipcMain.handle(IpcChannels.NATIVE_OPEN_EXTERNAL, async (_e, url: string): Promise<void> => {
    await shell.openExternal(url);
  });

  ipcMain.handle(IpcChannels.NATIVE_NOTIFICATION, async (_e, title: string, body: string): Promise<void> => {
    if (Notification.isSupported()) {
      new Notification({ title, body }).show();
    }
  });

  ipcMain.handle(IpcChannels.NATIVE_TRAY, async (_e, _icon: string, _menu: TrayMenu): Promise<void> => {
    // TODO: implement tray in Stage 9
  });

  ipcMain.handle(IpcChannels.NATIVE_SPAWN, async (_e, cmd: string, args: string[], cwd?: string): Promise<number> => {
    const proc = spawn(cmd, args, { cwd, shell: true });
    terminalProcesses.set(proc.pid ?? 0, proc);
    // Output forwarding is handled via webContents.send
    return proc.pid ?? 0;
  });

  ipcMain.handle(IpcChannels.NATIVE_KILL, async (_e, pid: number): Promise<void> => {
    const proc = terminalProcesses.get(pid);
    if (proc) {
      proc.kill();
      terminalProcesses.delete(pid);
    }
  });

  // ---- SQLite ----
  ipcMain.handle(IpcChannels.DB_QUERY, async (_e, sql: string, params?: unknown[]) => {
    const db = getDb();
    return db.prepare(sql).all(...(params ?? []));
  });

  ipcMain.handle(IpcChannels.DB_EXECUTE, async (_e, sql: string, params?: unknown[]) => {
    const db = getDb();
    const result = db.prepare(sql).run(...(params ?? []));
    return { changes: result.changes, lastInsertRowid: result.lastInsertRowid };
  });

  // ---- License ----
  ipcMain.handle(IpcChannels.LICENSE_GET, async (): Promise<LicenseConsent | null> => {
    return getConsent();
  });

  ipcMain.handle(IpcChannels.LICENSE_ACCEPT, async (_e, type: "nonprofit" | "commercial", key?: string): Promise<void> => {
    await acceptConsent(type, key);
  });

  // ---- Settings ----
  ipcMain.handle(IpcChannels.SETTINGS_GET, async (): Promise<StudioSettings> => {
    return getSettings();
  });

  ipcMain.handle(IpcChannels.SETTINGS_UPDATE, async (_e, patch: Partial<StudioSettings>): Promise<StudioSettings> => {
    return updateSettings(patch);
  });

  // ---- Keys ----
  ipcMain.handle(IpcChannels.KEYS_GET_SYSTEM, async (_e, _connectionId: string): Promise<string | null> => {
    // TODO: implement safeStorage in connections.ts
    return null;
  });

  ipcMain.handle(IpcChannels.KEYS_SET_SYSTEM, async (_e, _connectionId: string, _key: string): Promise<void> => {
    // TODO: implement safeStorage in connections.ts
  });

  ipcMain.handle(IpcChannels.KEYS_REMOVE_SYSTEM, async (_e, _connectionId: string): Promise<void> => {
    // TODO: implement safeStorage in connections.ts
  });

  // ---- App info ----
  ipcMain.handle(IpcChannels.APP_VERSION, async (): Promise<string> => {
    return app.getVersion();
  });

  ipcMain.handle(IpcChannels.APP_DATA_PATH, async (): Promise<string> => {
    return app.getPath("userData");
  });
}