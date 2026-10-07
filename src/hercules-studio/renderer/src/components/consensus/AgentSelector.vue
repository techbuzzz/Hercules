<script setup lang="ts">
/**
 * Stage 7.3 — agent selector.
 *
 * Consensus is meaningless with a single agent, so the minimum of two is enforced here
 * as well as in the store: the store guards the action, this makes the requirement
 * visible before the user reaches the Send button.
 */
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import {
  MAX_CONSENSUS_AGENTS,
  MIN_CONSENSUS_AGENTS,
  useConsensusStore,
} from "../../stores/consensus";

const { t } = useI18n();
const connections = useConnectionsStore();
const consensus = useConsensusStore();

/** Unreachable agents cannot answer, so they are not offered by default. */
const onlineOnly = computed(() => connections.list.filter((c) => c.status === "online"));
const offline = computed(() => connections.list.filter((c) => c.status !== "online"));
const candidates = computed(() => (onlineOnly.value.length > 0 ? onlineOnly.value : connections.list));

const locked = computed(() => consensus.status === "querying" || consensus.status === "aggregating");
const atMax = computed(() => consensus.selectedIds.length >= MAX_CONSENSUS_AGENTS);
const underMin = computed(() => consensus.selectedIds.length < MIN_CONSENSUS_AGENTS);

function statusClass(id: string): string {
  return connections.list.find((c) => c.id === id)?.status === "online"
    ? "bg-emerald-600 text-white"
    : "bg-tertiary text-secondary";
}
</script>

<template>
  <div class="space-y-1.5">
    <div class="flex flex-wrap items-center gap-1.5">
      <button
        v-for="conn in candidates"
        :key="conn.id"
        type="button"
        class="flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs transition-colors disabled:opacity-50"
        :class="consensus.selectedIds.includes(conn.id) ? statusClass(conn.id) : 'bg-tertiary text-secondary hover:bg-app'"
        :disabled="locked || (!consensus.selectedIds.includes(conn.id) && atMax)"
        :aria-pressed="consensus.selectedIds.includes(conn.id)"
        @click="consensus.toggleAgent(conn.id)"
      >
        {{ conn.name }}
        <span v-if="conn.status !== 'online'" class="text-[10px] text-secondary">offline</span>
      </button>

      <p v-if="candidates.length === 0" class="text-xs text-secondary">
        {{ t("consensus.noAgents") }}
      </p>
    </div>

    <p class="text-[11px] text-secondary">
      {{ t("consensus.selectionCount", { n: consensus.selectedIds.length, max: MAX_CONSENSUS_AGENTS }) }}
      <span v-if="offline.length > 0 && onlineOnly.length > 0">
        · {{ t("consensus.offlineHidden", { n: offline.length }) }}
      </span>
    </p>

    <p v-if="underMin && consensus.selectedIds.length > 0" class="text-[11px] text-amber-400">
      {{ t("consensus.needTwo") }}
    </p>
  </div>
</template>