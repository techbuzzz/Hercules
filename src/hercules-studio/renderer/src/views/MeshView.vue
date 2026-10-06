<script setup lang="ts">
/**
 * Mesh view — agent registry health and trust at a glance.
 *
 * Renders whatever the registry actually returns rather than assuming a fixed
 * entry shape: `/api/mesh/agents` returns full capability-registry records whose
 * fields vary by registration source.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { MeshHealthDto, MeshHealthEntryDto } from "../sdk/types";

interface RegistryAgent {
  agentId?: string;
  displayName?: string;
  endpoint?: string;
  trustLevel?: string;
  healthScore?: number;
  qualityScore?: number;
  latencyMs?: number;
  lastSeen?: string | null;
  capabilities?: unknown[];
}

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const agents = ref<RegistryAgent[]>([]);
const health = ref<MeshHealthDto | null>(null);
const denials = ref<Record<string, unknown>[]>([]);
const loading = ref(false);
const search = ref("");

const client = computed(() => connections.client);

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase();
  if (!q) return agents.value;
  return agents.value.filter((a) =>
    `${a.agentId ?? ""} ${a.displayName ?? ""} ${a.endpoint ?? ""} ${a.trustLevel ?? ""}`
      .toLowerCase()
      .includes(q),
  );
});

/** Merge health rows onto registry entries so the table can show both. */
const rows = computed(() =>
  filtered.value.map((a) => {
    const h: MeshHealthEntryDto | undefined = health.value?.agents?.find(
      (x) => x.agentId === a.agentId,
    );
    return {
      agentId: a.agentId ?? "—",
      displayName: a.displayName ?? a.agentId ?? "—",
      endpoint: a.endpoint ?? "—",
      trustLevel: a.trustLevel ?? "—",
      healthScore: h?.healthScore ?? a.healthScore ?? null,
      healthStatus: h?.healthStatus ?? null,
      circuitState: h?.circuitState ?? null,
      avgLatencyMs: h?.avgLatencyMs ?? a.latencyMs ?? null,
      qualityScore: a.qualityScore ?? null,
      lastSeen: a.lastSeen ?? h?.lastSeen ?? null,
    };
  }),
);

function scoreClass(score: number | null): string {
  if (score === null) return "text-secondary";
  if (score >= 0.8) return "text-emerald-400";
  if (score >= 0.5) return "text-amber-400";
  return "text-red-400";
}

function circuitClass(state: string | null): string {
  if (state === "closed") return "text-emerald-400";
  if (state === "half_open") return "text-amber-400";
  if (state) return "text-red-400";
  return "text-secondary";
}

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    // Health is the useful part; treat the rest as optional so one failing
    // sub-request does not blank the whole view.
    const [agentList, healthData] = await Promise.allSettled([
      client.value.getMeshAgents(),
      client.value.getMeshHealth(),
    ]);

    agents.value =
      agentList.status === "fulfilled" && Array.isArray(agentList.value)
        ? (agentList.value as RegistryAgent[])
        : [];
    health.value = healthData.status === "fulfilled" ? healthData.value : null;

    client.value
      .getMeshDenials(10)
      .then((d) => {
        denials.value = Array.isArray(d) ? (d as Record<string, unknown>[]) : [];
      })
      .catch(() => {
        denials.value = [];
      });
  } catch (e) {
    toast.error(`${t("mesh.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-6xl">
      <!-- Header -->
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h1 class="text-lg font-semibold text-app">{{ t("mesh.title") }}</h1>
          <p class="text-sm text-secondary">{{ t("mesh.subtitle") }}</p>
        </div>
        <div class="flex items-center gap-2">
          <input
            v-model="search"
            type="search"
            :placeholder="t('common.search')"
            class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
          />
          <button
            class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
            :disabled="loading"
            @click="load"
          >
            {{ t("common.refresh") }}
          </button>
        </div>
      </div>

      <!-- Summary -->
      <div v-if="health" class="mb-4 grid grid-cols-3 gap-3">
        <div class="rounded-xl border border-app bg-secondary p-3">
          <div class="text-xs text-secondary">{{ t("mesh.healthy") }}</div>
          <div class="text-2xl font-semibold text-emerald-400">{{ health.healthyCount }}</div>
        </div>
        <div class="rounded-xl border border-app bg-secondary p-3">
          <div class="text-xs text-secondary">{{ t("mesh.degraded") }}</div>
          <div class="text-2xl font-semibold text-amber-400">{{ health.degradedCount }}</div>
        </div>
        <div class="rounded-xl border border-app bg-secondary p-3">
          <div class="text-xs text-secondary">{{ t("mesh.unhealthy") }}</div>
          <div class="text-2xl font-semibold text-red-400">{{ health.unhealthyCount }}</div>
        </div>
      </div>

      <!-- Agents -->
      <div class="mb-6 overflow-hidden rounded-xl border border-app">
        <table class="w-full text-left text-sm">
          <thead class="bg-secondary text-xs text-secondary">
            <tr>
              <th class="px-3 py-2 font-medium">{{ t("mesh.agent") }}</th>
              <th class="px-3 py-2 font-medium">{{ t("mesh.endpoint") }}</th>
              <th class="px-3 py-2 font-medium">{{ t("mesh.trust") }}</th>
              <th class="px-3 py-2 font-medium">{{ t("mesh.health") }}</th>
              <th class="px-3 py-2 font-medium">{{ t("mesh.circuit") }}</th>
              <th class="px-3 py-2 font-medium">{{ t("mesh.latency") }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading">
              <td colspan="6" class="px-3 py-4 text-secondary">{{ t("common.loading") }}</td>
            </tr>
            <tr v-else-if="rows.length === 0">
              <td colspan="6" class="px-3 py-4 text-secondary">{{ t("mesh.noAgents") }}</td>
            </tr>
            <tr v-for="row in rows" v-else :key="row.agentId" class="border-t border-app hover:bg-tertiary">
              <td class="px-3 py-2">
                <div class="text-app">{{ row.displayName }}</div>
                <div class="text-xs text-secondary">{{ row.agentId }}</div>
              </td>
              <td class="px-3 py-2 text-xs text-secondary">{{ row.endpoint }}</td>
              <td class="px-3 py-2 text-xs">{{ row.trustLevel }}</td>
              <td class="px-3 py-2">
                <span v-if="row.healthStatus" class="mr-1 text-xs">{{ row.healthStatus }}</span>
                <span class="text-xs" :class="scoreClass(row.healthScore)">
                  {{ row.healthScore === null ? "—" : Math.round(row.healthScore * 100) + "%" }}
                </span>
              </td>
              <td class="px-3 py-2 text-xs" :class="circuitClass(row.circuitState)">
                {{ row.circuitState ?? "—" }}
              </td>
              <td class="px-3 py-2 text-xs text-secondary">
                {{ row.avgLatencyMs === null ? "—" : `${Math.round(row.avgLatencyMs)}ms` }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Policy denials -->
      <div>
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("mesh.denials") }}</h2>
        <p v-if="denials.length === 0" class="text-sm text-secondary">{{ t("mesh.noDenials") }}</p>
        <div v-else class="space-y-1">
          <div
            v-for="(d, i) in denials"
            :key="i"
            class="rounded-lg border border-app bg-secondary px-3 py-2 font-mono text-xs text-secondary"
          >
            {{ JSON.stringify(d) }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>