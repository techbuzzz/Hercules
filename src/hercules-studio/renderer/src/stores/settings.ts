import { defineStore } from "pinia";
import { ref } from "vue";
import type { StudioSettings } from "@shared/protocol";

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

export const useSettingsStore = defineStore("settings", () => {
  const data = ref<StudioSettings>(DEFAULT_SETTINGS);
  const loaded = ref(false);

  async function load() {
    data.value = await window.studioAPI.settings.get();
    loaded.value = true;
    applyTheme();
  }

  async function update(patch: Partial<StudioSettings>) {
    data.value = await window.studioAPI.settings.update(patch);
    applyTheme();
  }

  function applyTheme() {
    document.documentElement.classList.toggle("dark", data.value.theme === "dark");
  }

  return { data, loaded, load, update };
});