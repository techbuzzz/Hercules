<script setup lang="ts">
import { ref, onMounted, computed } from "vue";
import { useI18n } from "vue-i18n";
import ActivityBar from "./components/layout/ActivityBar.vue";
import Sidebar from "./components/layout/Sidebar.vue";
import MainWorkbench from "./components/layout/MainWorkbench.vue";
import StatusBar from "./components/layout/StatusBar.vue";
import CommandPalette from "./components/layout/CommandPalette.vue";
import LicenseDialog from "./components/common/LicenseDialog.vue";
import { useConnectionsStore } from "./stores/connections";
import { useSettingsStore } from "./stores/settings";

const { t } = useI18n();
const connections = useConnectionsStore();
const settings = useSettingsStore();

const showLicense = ref(false);
const showPalette = ref(false);
const activeView = ref("empty");

onMounted(async () => {
  // Load settings
  await settings.load();

  // Check license consent
  const consent = await window.studioAPI.license.getConsent();
  if (!consent) {
    showLicense.value = true;
  }

  // Load connections
  await connections.load();

  // Auto-scan if enabled
  if (settings.data.scan.autoScanOnStartup) {
    connections.scan();
  }
});

function handleLicenseAccepted() {
  showLicense.value = false;
}

function handleSelectView(view: string) {
  activeView.value = view;
}

// Keyboard shortcuts
function onKeydown(e: KeyboardEvent) {
  if (e.ctrlKey && e.shiftKey && e.key === "P") {
    e.preventDefault();
    showPalette.value = !showPalette.value;
  }
  if (e.ctrlKey && e.key === "k" && !e.shiftKey) {
    e.preventDefault();
    showPalette.value = !showPalette.value;
  }
  if (e.key === "Escape") {
    showPalette.value = false;
  }
}

window.addEventListener("keydown", onKeydown);
</script>

<template>
  <div class="flex h-screen w-screen flex-col bg-app text-app">
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
  </div>
</template>