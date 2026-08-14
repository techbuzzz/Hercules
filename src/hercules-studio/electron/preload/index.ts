import { contextBridge, ipcRenderer } from "electron";
import { IpcChannels, type IpcApi } from "@shared/protocol";

const api: IpcApi = {
  connections: {
    list: () => ipcRenderer.invoke(IpcChannels.CONNECTIONS_LIST),
    add: (conn) => ipcRenderer.invoke(IpcChannels.CONNECTIONS_ADD, conn),
    remove: (id) => ipcRenderer.invoke(IpcChannels.CONNECTIONS_REMOVE, id),
    update: (id, patch) => ipcRenderer.invoke(IpcChannels.CONNECTIONS_UPDATE, id, patch),
    healthCheck: (id) => ipcRenderer.invoke(IpcChannels.CONNECTIONS_HEALTH, id),
    setActive: (id) => ipcRenderer.invoke(IpcChannels.CONNECTIONS_SET_ACTIVE, id),
  },
  scanner: {
    scan: () => ipcRenderer.invoke(IpcChannels.SCANNER_SCAN),
    onProgress: (callback) => {
      const handler = (_e: unknown, progress: unknown) => callback(progress as never);
      ipcRenderer.on(IpcChannels.SCANNER_PROGRESS, handler);
    },
    offProgress: () => {
      ipcRenderer.removeAllListeners(IpcChannels.SCANNER_PROGRESS);
    },
  },
  native: {
    readFile: (path) => ipcRenderer.invoke(IpcChannels.NATIVE_READ_FILE, path),
    writeFile: (path, content) => ipcRenderer.invoke(IpcChannels.NATIVE_WRITE_FILE, path, content),
    openExternal: (url) => ipcRenderer.invoke(IpcChannels.NATIVE_OPEN_EXTERNAL, url),
    showNotification: (title, body) => ipcRenderer.invoke(IpcChannels.NATIVE_NOTIFICATION, title, body),
    setTray: (icon, menu) => ipcRenderer.invoke(IpcChannels.NATIVE_TRAY, icon, menu),
    spawnTerminal: (cmd, args, cwd) => ipcRenderer.invoke(IpcChannels.NATIVE_SPAWN, cmd, args, cwd),
    onTerminalOutput: (callback) => {
      const handler = (_e: unknown, pid: number, data: string) => callback(pid, data);
      ipcRenderer.on(IpcChannels.NATIVE_TERMINAL_OUTPUT, handler);
    },
    killTerminal: (pid) => ipcRenderer.invoke(IpcChannels.NATIVE_KILL, pid),
  },
  db: {
    query: (sql, params) => ipcRenderer.invoke(IpcChannels.DB_QUERY, sql, params),
    execute: (sql, params) => ipcRenderer.invoke(IpcChannels.DB_EXECUTE, sql, params),
  },
  license: {
    getConsent: () => ipcRenderer.invoke(IpcChannels.LICENSE_GET),
    acceptConsent: (type, key) => ipcRenderer.invoke(IpcChannels.LICENSE_ACCEPT, type, key),
  },
  settings: {
    get: () => ipcRenderer.invoke(IpcChannels.SETTINGS_GET),
    update: (patch) => ipcRenderer.invoke(IpcChannels.SETTINGS_UPDATE, patch),
  },
  keys: {
    getSystemKey: (connectionId) => ipcRenderer.invoke(IpcChannels.KEYS_GET_SYSTEM, connectionId),
    setSystemKey: (connectionId, key) => ipcRenderer.invoke(IpcChannels.KEYS_SET_SYSTEM, connectionId, key),
    removeSystemKey: (connectionId) => ipcRenderer.invoke(IpcChannels.KEYS_REMOVE_SYSTEM, connectionId),
  },
  app: {
    getVersion: () => ipcRenderer.invoke(IpcChannels.APP_VERSION),
    getDataPath: () => ipcRenderer.invoke(IpcChannels.APP_DATA_PATH),
  },
};

contextBridge.exposeInMainWorld("studioAPI", api);