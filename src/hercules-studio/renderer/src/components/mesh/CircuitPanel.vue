<script setup lang="ts">
/**
 * Stage 4.7 — circuit breaker panel.
 *
 * `GET /api/mesh/circuits` answers a map keyed by agent id, normalised to rows by the
 * SDK. Reset goes through `POST /api/mesh/circuits/{id}/reset`.
 */
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useToastStore } from "../../stores/toast";
import type { MeshCircuitState } from "../../sdk/client";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const circuits = ref<MeshCircuitState[]>([]);
const loading = ref(false);
const busyId = ref<string | null>(null);

const openCount = computed(() => circuits.value.filter((c) => c.state.toLowerCase() === "open").length);

async function load(): Promise<void> {
  if (!connections.client) return;
  loading.value = true;
  try {
    circuits.value = await connections.client.getMeshCircuits();
  } catch {
    circuits.value = [];
  } finally {
    loading.value = false;
  }
}

async function reset(agentId: string): Promise<void> {
  if (!connections.client || busyId.value) return;
  busyId.value = agentId;
  try {
    await connections.client.resetMeshCircuit(agentId);
    toast.success(t("mesh.circuitResetDone", { id: agentId }));
    await load();
  } catch (e) {
    toast.error(`${t("mesh.circuitResetFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    busyId.value = null;
  }
}

function stateClass(state: string): string {
  switch (state.toLowerCase()) {
    case "closed":
      return "text-emerald-400";
    case "open":
      return "text-red-400";
    case "halfopen":
    case "half-open":
      return "text-amber-400";
    default:
      return "text-secondary";
  }
}

onMounted(load);
</script>

<template>
  <div class="rounded-xl border border-app bg-secondary p-3">
    <div class="mb-2 flex items-center justify-between">
      <h3 class="text-sm font-medium text-app">{{ t("mesh.circuitsTitle") }}</h3>
      <div class="flex items-center gap-2">
        <span v-if="openCount > 0" class="text-[11px] text-red-400">
          {{ t("mesh.circuitsOpen", { n: openCount }) }}
        </span>
        <button
          class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary disabled:opacity-50"
          :disabled="loading || !connections.client"
          @click="load"
        >
          {{ t("common.refresh") }}
        </button>
      </div>
    </div>

    <p v-if="loading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
    <p v-else-if="circuits.length === 0" class="text-xs text-secondary">
      {{ t("mesh.circuitsEmpty") }}
    </p>

    <div v-else class="space-y-1">
      <div
        v-for="c in circuits"
        :key="c.agentId"
        class="flex items-center justify-between gap-2 rounded border border-app bg-app px-2 py-1.5"
      >
        <span class="truncate font-mono text-[11px] text-app">{{ c.agentId }}</span>
        <span class="flex items-center gap-2">
          <span class="text-[11px]" :class="stateClass(c.state)">{{ c.state }}</span>
          <button
            class="rounded border border-app px-2 py-0.5 text-[10px] text-app hover:bg-tertiary disabled:opacity-50"
            :disabled="busyId === c.agentId || !connections.client"
            @click="reset(c.agentId)"
          >
            {{ t("mesh.circuitReset") }}
          </button>
        </span>
      </div>
    </div>
  </div>
</template>