import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import type { StudioSettings } from "@shared/protocol";

let settingsPath = "";

const DEFAULT_SETTINGS: StudioSettings = {
  theme: "dark",
  locale: "en",
  typingEffect: true,
  compactMode: false,
  scan: {
    portStart: 8421,
    portEnd: 8521,
    legacyPort: 5000,
    enableProcessScan: true,
    autoScanOnStartup: true,
    concurrent: 50,
    timeoutMs: 300,
  },
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

export function initSettings(userDataPath: string): void {
  settingsPath = join(userDataPath, "settings.json");
  if (!existsSync(settingsPath)) {
    writeFileSync(settingsPath, JSON.stringify(DEFAULT_SETTINGS, null, 2), "utf-8");
  }
}

export function getSettings(): StudioSettings {
  try {
    if (!existsSync(settingsPath)) {
      return DEFAULT_SETTINGS;
    }
    const data = readFileSync(settingsPath, "utf-8");
    return { ...DEFAULT_SETTINGS, ...JSON.parse(data) };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

export function updateSettings(patch: Partial<StudioSettings>): StudioSettings {
  const current = getSettings();
  const updated = { ...current, ...patch };
  writeFileSync(settingsPath, JSON.stringify(updated, null, 2), "utf-8");
  return updated;
}