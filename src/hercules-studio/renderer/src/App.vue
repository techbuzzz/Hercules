<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from "vue";
import { useI18n } from "vue-i18n";
import ActivityBar from "./components/layout/ActivityBar.vue";
import Sidebar from "./components/layout/Sidebar.vue";
import MainWorkbench from "./components/layout/MainWorkbench.vue";
import StatusBar from "./components/layout/StatusBar.vue";
import CommandPalette from "./components/layout/CommandPalette.vue";
import LicenseDialog from "./components/common/LicenseDialog.vue";
import ToastContainer from "./components/common/ToastContainer.vue";
import { useConnectionsStore } from "./stores/connections";
import { useSettingsStore } from "./stores/settings";
import { useErrorStore } from "./stores/error";

const { t, locale } = useI18n();
const connections = useConnectionsStore();
const settings = useSettingsStore();
const errorStore = useErrorStore();

const showLicense = ref(false);
const showPalette = ref(false);
const activeView = ref("agents");
const appLoading = ref(true);
const fatalError = ref<string | null>(null);

onMounted(async () => {
  // Install global error handlers
  errorStore.installGlobalHandlers();

  try {
    // Load settings first
    await settings.load();
    applyTheme();
    applyLocale();

    // Check license consent
    const consent = await window.studioAPI.license.getConsent();
    if (!consent) {
      showLicense.value = true;
    }

    // Load connections
    await connections.load();
    if (connections.list.length > 0 && !connections.activeId) {
      await connections.setActive(connections.list[0].id);
    }

    // Auto-scan if enabled
    if (settings.data.scan.autoScanOnStartup) {
      connections.scan();
    }
  } catch (e) {
    fatalError.value = e instanceof Error ? e.message : String(e);
    errorStore.report(fatalError.value, "App.onMounted", e instanceof Error ? e.stack : undefined);
  } finally {
    appLoading.value = false;
  }
});

onUnmounted(() => {
  window.removeEventListener("keydown", onKeydown);
});

function applyTheme(): void {
  document.documentElement.classList.toggle("dark", settings.data.theme === "dark");
}

function applyLocale(): void {
  locale.value = settings.data.locale;
}

watch(() => settings.data.theme, applyTheme);
watch(() => settings.data.locale, applyLocale);

function handleLicenseAccepted(): void {
  showLicense.value = false;
}

function handleSelectView(view: string): void {
  activeView.value = view;
}

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent): void {
  if (e.ctrlKey && e.shiftKey && e.key === "P") {
    e.preventDefault();
    showPalette.value = !showPalette.value;
    return;
  }
  if (e.ctrlKey && e.key === "k" && !e.shiftKey) {
    e.preventDefault();
    showPalette.value = !showPalette.value;
    return;
  }
  if (e.key === "Escape") {
    showPalette.value = false;
  }
}

window.addEventListener("keydown", onKeydown);
</script>

<template>
  <div class="flex h-screen w-screen flex-col bg-app text-app">
    <!-- Fatal error state -->
    <div v-if="fatalError" class="flex flex-1 items-center justify-center p-8">
      <div class="max-w-md text-center">
        <h1 class="mb-2 text-xl font-semibold text-red-400">Fatal Error</h1>
        <p class="mb-4 text-sm text-secondary">{{ fatalError }}</p>
        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500"
          @click="fatalError = null"
        >
          Dismiss
        </button>
      </div>
    </div>

    <!-- Loading state -->
    <div v-else-if="appLoading" class="flex flex-1 items-center justify-center">
      <div class="text-center">
        <div class="mx-auto mb-3 h-8 w-8 animate-spin rounded-full border-2 border-zinc-600 border-t-emerald-500" />
        <p class="text-sm text-secondary">Loading Hercules Studio...</p>
      </div>
    </div>

    <!-- Main app -->
    <template v-else>
      <!-- License dialog (first-run) -->
      <LicenseDialog v-if="showLicense" @accepted="handleLicenseAccepted" />

      <!-- Main layout -->
      <div class="flex flex-1 overflow-hidden">
        <ActivityBar @select="handleSelectView" />
        <Sidebar :active-view="activeView" />
        <MainWorkbench :active-view="activeView" />
      </div>

      <!-- Status bar -->
      <StatusBar />

      <!-- Command palette -->
      <CommandPalette v-if="showPalette" @close="showPalette = false" />

      <!-- Toast notifications -->
      <ToastContainer />
    </template>
  </div>
</template>