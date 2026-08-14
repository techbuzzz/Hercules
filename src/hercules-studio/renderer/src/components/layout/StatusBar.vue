<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";

const { t } = useI18n();
const connections = useConnectionsStore();

const activeAgent = computed(() => connections.active);
const version = "0.1.0";
</script>

<template>
  <div class="flex h-6 items-center gap-4 border-t border-app bg-secondary px-3 text-xs text-secondary">
    <!-- Agent status -->
    <template v-if="activeAgent">
      <span class="flex items-center gap-1.5">
        <span
          class="h-2 w-2 rounded-full"
          :class="{
            'bg-green-500': activeAgent.status === 'online',
            'bg-amber-500': activeAgent.status === 'degraded',
            'bg-red-500': activeAgent.status === 'offline',
          }"
        />
        <span class="text-app">{{ activeAgent.displayName }}</span>
      </span>
      <span v-if="activeAgent.lastSeen">{{ t("status.checkedIn") }}</span>
    </template>
    <template v-else>
      <span>{{ t("status.noAgent") }}</span>
    </template>

    <!-- Spacer -->
    <div class="flex-1" />

    <!-- Version -->
    <span>v{{ version }}</span>
  </div>
</template>