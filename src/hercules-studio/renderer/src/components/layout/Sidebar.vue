<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import type { Connection } from "@shared/protocol";

const { t } = useI18n();
const connections = useConnectionsStore();

defineProps<{ activeView: string }>();

const onlineConnections = computed(() =>
  connections.list.filter((c) => c.status === "online"),
);
const offlineConnections = computed(() =>
  connections.list.filter((c) => c.status !== "online"),
);

function selectAgent(conn: Connection): void {
  connections.setActive(conn.id);
}
</script>

<template>
  <div class="flex w-56 flex-col overflow-y-auto border-r border-app bg-secondary">
    <!-- Agents sidebar -->
    <template v-if="activeView === 'agents'">
      <div class="px-3 py-2 text-xs font-medium uppercase text-secondary">
        {{ t("activity.agents") }}
      </div>
      <div v-if="connections.list.length === 0" class="px-3 py-2 text-sm text-secondary">
        {{ t("status.noAgent") }}
      </div>
      <div v-if="onlineConnections.length > 0" class="mb-2">
        <div class="px-3 py-1 text-[10px] font-medium uppercase text-secondary">
          {{ t("status.online") }}
        </div>
        <button
          v-for="conn in onlineConnections"
          :key="conn.id"
          class="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-tertiary"
          :class="conn.id === connections.activeId ? 'bg-tertiary text-app' : 'text-secondary'"
          @click="selectAgent(conn)"
        >
          <span class="h-2 w-2 shrink-0 rounded-full bg-green-500" />
          <span class="truncate">{{ conn.displayName }}</span>
        </button>
      </div>
      <div v-if="offlineConnections.length > 0">
        <div class="px-3 py-1 text-[10px] font-medium uppercase text-secondary">
          {{ t("status.offline") }}
        </div>
        <button
          v-for="conn in offlineConnections"
          :key="conn.id"
          class="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-tertiary"
          :class="conn.id === connections.activeId ? 'bg-tertiary text-app' : 'text-secondary'"
          @click="selectAgent(conn)"
        >
          <span class="h-2 w-2 shrink-0 rounded-full bg-zinc-500" />
          <span class="truncate">{{ conn.displayName }}</span>
        </button>
      </div>
    </template>

    <!-- Other sidebars (placeholder) -->
    <template v-else>
      <div class="px-3 py-2 text-xs font-medium uppercase text-secondary">
        {{ t(`activity.${activeView}`) }}
      </div>
      <div class="px-3 py-2 text-sm text-secondary">
        {{ connections.active ? t("status.checkedIn") : t("status.noAgent") }}
      </div>
    </template>
  </div>
</template>