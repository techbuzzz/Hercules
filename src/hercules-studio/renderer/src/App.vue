<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from "vue";
import { useI18n } from "vue-i18n";
import ActivityBar from "./components/layout/ActivityBar.vue";
import Sidebar from "./components/layout/Sidebar.vue";
import MainWorkbench from "./components/layout/MainWorkbench.vue";
import StatusBar from "./components/layout/StatusBar.vue";
import CommandPalette from "./components/layout/CommandPalette.vue";
import LicenseDialog from "./components/common/LicenseDialog.vue";
import ReauthDialog from "./components/common/ReauthDialog.vue";
import AddConnectionDialog from "./components/common/AddConnectionDialog.vue";
import ToastContainer from "./components/common/ToastContainer.vue";
import { useConnectionsStore } from "./stores/connections";
import { useSettingsStore } from "./stores/settings";
import { useErrorStore } from "./stores/error";
import { useToastStore } from "./stores/toast";
import { useUiStore } from "./stores/ui";
import { platform } from "./platform";

const { t, locale } = useI18n();
const connections = useConnectionsStore();
const settings = useSettingsStore();
const errorStore = useErrorStore();
const toast = useToastStore();
const ui = useUiStore();

const showLicense = ref(false);
const showPalette = ref(false);
const booting = ref(true);

function onKeydown(e: KeyboardEvent): void {
  if (e.key === "Escape") {
    showPalette.value = false;
    return;
  }
  // Ctrl/Cmd+K and Ctrl/Cmd+Shift+P both open the command palette.
  if ((e.ctrlKey || e.metaKey) && (e.key.toLowerCase() === "k" || e.key.toLowerCase() === "p")) {
    e.preventDefault();
    showPalette.value = !showPalette.value;
  }
}

function applyTheme(): void {
  document.documentElement.classList.toggle("dark", settings.data.theme === "dark");
}

function applyLocale(): void {
  locale.value = settings.data.locale;
}

onMounted(async () => {
  errorStore.installGlobalHandlers();

  // Settings and connections load independently: one failure must not block the
  // other, and neither may replace the UI with a full-screen error.
  await Promise.allSettled([settings.load(), connections.load()]);
  connections.initSessionWatcher();
  connections.startMonitor();

  applyTheme();
  applyLocale();

  try {
    const consent = await platform.license.getConsent();
    if (!consent) showLicense.value = true;
  } catch {
    toast.warn(t("startup.licenseCheckFailed"));
  }

  if (settings.data.scan.endpoints.length > 0) {
    void connections.scan();
  }

  window.addEventListener("keydown", onKeydown);
  booting.value = false;
});

onBeforeUnmount(() => {
  window.removeEventListener("keydown", onKeydown);
  connections.dispose();
});

watch(
  () => settings.data.theme,
  applyTheme,
);
watch(
  () => settings.data.locale,
  applyLocale,
);
</script>

<template>
  <div class="flex h-screen w-screen flex-col bg-app text-app">
    <!-- First-run license consent -->
    <LicenseDialog v-if="showLicense" @accepted="showLicense = false" />

    <!-- Session expired — the key is never stored, so it must be re-entered -->
    <ReauthDialog />

    <!-- Reachable from both the empty state and the agents view -->
    <AddConnectionDialog />

    <!-- Main layout renders immediately; startup work never blocks it -->
    <div class="flex flex-1 overflow-hidden" :class="{ 'opacity-60': booting }">
      <ActivityBar @select="ui.select($event)" />
      <Sidebar :active-view="ui.activeView" />
      <MainWorkbench :active-view="ui.activeView" />
    </div>

    <StatusBar />

    <CommandPalette v-if="showPalette" @close="showPalette = false" />

    <ToastContainer />
  </div>
</template>