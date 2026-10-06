import { defineStore } from "pinia";
import { ref } from "vue";
import { platform, DEFAULT_SETTINGS } from "@renderer/platform";
import type { StudioSettings } from "@renderer/platform/capabilities";

export const useSettingsStore = defineStore("settings", () => {
  const data = ref<StudioSettings>({ ...DEFAULT_SETTINGS });
  const loaded = ref(false);

  function applyTheme(): void {
    document.documentElement.classList.toggle("dark", data.value.theme === "dark");
  }

  async function load(): Promise<void> {
    data.value = await platform.settings.get();
    loaded.value = true;
    applyTheme();
  }

  async function update(patch: Partial<StudioSettings>): Promise<void> {
    data.value = await platform.settings.update(patch);
    applyTheme();
  }

  return { data, loaded, load, update };
});