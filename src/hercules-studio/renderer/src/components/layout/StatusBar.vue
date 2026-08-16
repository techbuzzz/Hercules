<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useErrorStore } from "../../stores/error";

const { t } = useI18n();
const connections = useConnectionsStore();
const errorStore = useErrorStore();

const activeAgent = computed(() => connections.active);
const hasErrors = computed(() => errorStore.hasErrors);
const errorCount = computed(() => errorStore.errors.length);
const version = "0.1.0";

const statusColor = computed(() => {
  if (!activeAgent.value) return "bg-zinc-500";
  switch (activeAgent.value.status) {
    case "online":
      return "bg-green-500";
    case "degraded":
      return "bg-amber-500";
    case "offline":
      return "bg-red-500";
    default:
      return "bg-zinc-500";
  }
});
</script>

<template>
  <div class="flex h-6 items-center gap-4 border-t border-app bg-secondary px-3 text-xs text-secondary">
    <!-- Agent status -->
    <template v-if="activeAgent">
      <span class="flex items-center gap-1.5">
        <span class="h-2 w-2 rounded-full" :class="statusColor" />
        <span class="text-app">{{ activeAgent.displayName }}</span>
      </span>
      <span v-if="activeAgent.lastSeen" class="opacity-70">{{ t("status.checkedIn") }}</span>
    </template>
    <template v-else>
      <span>{{ t("status.noAgent") }}</span>
    </template>

    <!-- Error indicator -->
    <span v-if="hasErrors" class="flex items-center gap-1 text-red-400" :title="`${errorCount} errors`">
      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
        <path d="M12 9v4M12 17h.01" />
      </svg>
      {{ errorCount }}
    </span>

    <!-- Spacer -->
    <div class="flex-1" />

    <!-- Version -->
    <span class="opacity-70">v{{ version }}</span>
  </div>
</template>