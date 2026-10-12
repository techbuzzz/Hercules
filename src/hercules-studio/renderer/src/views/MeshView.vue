<script setup lang="ts">
/**
 * Mesh view — agent registry health and trust at a glance.
 *
 * Renders whatever the registry actually returns rather than assuming a fixed
 * entry shape: `/api/mesh/agents` returns full capability-registry records whose
 * fields vary by registration source.
 */
import { ref, computed, onMounted, onBeforeUnmount } from "vue";
import { useI18n } from "vue-i18n";
import MeshCanvas from "../components/mesh/MeshCanvas.vue";
import NodeDetailsPanel from "../components/mesh/NodeDetailsPanel.vue";
import CircuitPanel from "../components/mesh/CircuitPanel.vue";
import MeshDashboardPanels from "../components/mesh/MeshDashboardPanels.vue";
import SharedMemoryPanel from "../components/mesh/SharedMemoryPanel.vue";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type {
  MeshHealthDto,
  MeshHealthEntryDto,
  MeshRoutesResponseDto,
  MeshAgentDto,
  MeshDashboardDto,
} from "../sdk/types";

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

/** int32/double fields generate as `number | string`; the wire value is a number. */
function num(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  const parsed = Number(value);
  return Number.isNaN(parsed) ? null : parsed;
}

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

    // `/api/mesh/agents` answers `{ count, agents }`, not a bare array — the
    // previous `Array.isArray(...)` guard therefore swallowed every real
    // response and rendered an empty list against a live agent. Unwrap the
    // envelope and fall back only when the shape is genuinely unusable.
    agents.value = agentList.status === "fulfilled" ? unwrapAgents(agentList.value) : [];
    health.value = healthData.status === "fulfilled" ? healthData.value : null;

    void loadTopology();

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

/** Accepts either the `{count, agents}` envelope or a bare array, so both work. */
function unwrapAgents(value: unknown): RegistryAgent[] {
  if (Array.isArray(value)) return value as RegistryAgent[];
  if (value && typeof value === "object") {
    const nested = (value as { agents?: unknown }).agents;
    if (Array.isArray(nested)) return nested as RegistryAgent[];
  }
  return [];
}

// ---- Topology canvas (Stage 4) ----
// The canvas reads the typed dashboard rather than the untyped registry endpoint:
// `/api/mesh/dashboard` returns `MeshDashboardDto.topology`, which is generated from the
// OpenAPI document, so node fields are typed end to end.
const topology = ref<MeshAgentDto[]>([]);
const selectedAgentId = ref<string | null>(null);
const selectedAgent = computed(
  () => topology.value.find((a) => a.agentId === selectedAgentId.value) ?? null,
);

// The dashboard carries far more than topology; these are rendered below rather than
// discarded. All int32/int64 fields generate as `number | string`.
const dashboard = ref<MeshDashboardDto | null>(null);

async function loadTopology(): Promise<void> {
  if (!client.value) return;
  try {
    const dash = await client.value.getMeshDashboard();
    dashboard.value = dash;
    const agents = dash.topology?.agents;
    topology.value = Array.isArray(agents) ? (agents as MeshAgentDto[]) : [];
  } catch {
    // The canvas is additive; a missing dashboard must not break the rest of the view.
    dashboard.value = null;
    topology.value = [];
  }
}

function selectNode(agentId: string): void {
  selectedAgentId.value = agentId === selectedAgentId.value ? null : agentId;
}

// ---- Stage 4.4 context-menu actions ----
// "Remove" is the only destructive one and always confirms; the rest are additive.
async function onTouch(agentId: string): Promise<void> {
  if (!client.value) return;
  try {
    await client.value.touchMeshAgent(agentId);
    toast.success(t("mesh.touched", { id: agentId }));
    await load();
    await loadTopology();
  } catch (e) {
    toast.error(`${t("mesh.touchFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function onRemoveNode(agentId: string): Promise<void> {
  if (!client.value) return;
  if (!window.confirm(t("mesh.removeAgentConfirm", { id: agentId }))) return;

  try {
    await client.value.removeMeshAgent(agentId);
    selectedAgentId.value = null;
    toast.success(t("mesh.agentRemoved", { id: agentId }));
    await load();
    await loadTopology();
  } catch (e) {
    toast.error(`${t("mesh.removeAgentFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function onResetCircuit(agentId: string): Promise<void> {
  if (!client.value) return;
  try {
    await client.value.resetMeshCircuit(agentId);
    toast.success(t("mesh.circuitResetDone", { id: agentId }));
  } catch (e) {
    toast.error(`${t("mesh.circuitResetFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

/** Stage 4.4 — register a peer from its well-known manifest URL. */
async function onRegisterPeer(): Promise<void> {
  if (!client.value) return;
  const url = window.prompt(t("mesh.registerPrompt"), "http://localhost:9001/agent.manifest.json");
  if (!url?.trim()) return;

  try {
    await client.value.registerPeerFromUrl(url.trim());
    toast.success(t("mesh.peerRegistered"));
    await load();
    await loadTopology();
  } catch (e) {
    toast.error(`${t("mesh.registerFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

/** Stage 4.4 — drop registry entries whose TTL has lapsed. Destructive, so it confirms. */
async function onCleanupStale(): Promise<void> {
  if (!client.value) return;
  if (!window.confirm(t("mesh.cleanupConfirm"))) return;

  try {
    const res = await client.value.cleanupStaleAgents();
    const n = Number(res.removedCount) || 0;
    toast.success(t("mesh.cleanupDone", { n }));
    await load();
    await loadTopology();
  } catch (e) {
    toast.error(`${t("mesh.cleanupFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

// ---- Routing inspector (Stage 6.4) ----
// "Which peers would serve capability X, and on what basis" — the router's
// ranking, trust verdict and circuit state. This is a query, not an editor:
// the router exposes no configuration surface to write back.
const capability = ref("");
const maxCost = ref("");
const routes = ref<MeshRoutesResponseDto | null>(null);
const routing = ref(false);
const routeError = ref<string | null>(null);

// ---- Stage 4.5 — phrase search, sorting and filters ----
const capabilitySuggestions = ref<string[]>([]);
const sortBy = ref<"composite" | "health" | "latency" | "cost">("composite");
const onlyTrustPassed = ref(false);
const onlyHealthy = ref(false);
const onlyCircuitClosed = ref(false);

/**
 * Semantic lookup over the registry. The router takes a capability name, so a phrase has
 * to be resolved to one first; the first suggestion is used when several match.
 */
async function searchCapabilities(phrase: string): Promise<void> {
  if (!client.value || !phrase.trim()) return;
  try {
    const found = await client.value.searchMeshCapabilities(phrase.trim());
    const names = Array.isArray(found) ? (found as unknown[]) : [];
    capabilitySuggestions.value = names
      .map((n) => {
        if (typeof n === "string") return n;
        const rec = n as Record<string, unknown>;
        return String(rec.name ?? rec.capability ?? "");
      })
      .filter(Boolean);
  } catch {
    capabilitySuggestions.value = [];
  }
}

const n = (value: unknown): number => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
};

/**
 * Filters and sorts are applied client-side over the router's own ranking: the endpoint
 * returns every candidate with its scores, so re-ranking here is presentation only and
 * cannot change which peers the router would actually pick.
 */
const visibleCandidates = computed(() => {
  const all = routes.value?.candidates ?? [];
  const kept = all.filter(
    (c) =>
      (!onlyTrustPassed.value || c.trustPassed) &&
      (!onlyHealthy.value || n(c.healthScore) >= 0.7) &&
      (!onlyCircuitClosed.value || !/open/i.test(c.circuitState ?? "")),
  );

  const sorted = [...kept];
  switch (sortBy.value) {
    case "health":
      sorted.sort((a, b) => n(b.healthScore) - n(a.healthScore));
      break;
    case "latency":
      sorted.sort((a, b) => n(a.latencyMs) - n(b.latencyMs));
      break;
    case "cost":
      sorted.sort((a, b) => n(a.costHintUsd) - n(b.costHintUsd));
      break;
    default:
      sorted.sort((a, b) => n(b.compositeScore) - n(a.compositeScore));
  }
  return sorted;
});

async function inspectRouting(): Promise<void> {
  if (!client.value || !capability.value.trim() || routing.value) return;
  routing.value = true;
  routeError.value = null;
  routes.value = null;
  try {
    const cost = maxCost.value.trim() ? Number(maxCost.value) : undefined;
    routes.value = await client.value.getMeshRoutes(
      capability.value.trim(),
      Number.isFinite(cost) ? cost : undefined,
    );
  } catch (e) {
    routeError.value = e instanceof Error ? e.message : String(e);
  } finally {
    routing.value = false;
  }
}

onMounted(load);

// Stage 4.1/4.2 — auto-refresh every 30s, alongside the manual Refresh button.
// Skipped while the tab is hidden: polling six mesh endpoints for a view nobody is
// looking at is pure load. Cleared on unmount so switching views stops the timer.
const AUTO_REFRESH_MS = 30_000;
let refreshTimer: ReturnType<typeof setInterval> | null = null;

function startAutoRefresh(): void {
  if (refreshTimer !== null) return;
  refreshTimer = setInterval(() => {
    if (typeof document !== "undefined" && document.visibilityState === "hidden") return;
    void load();
  }, AUTO_REFRESH_MS);
}

function stopAutoRefresh(): void {
  if (refreshTimer === null) return;
  clearInterval(refreshTimer);
  refreshTimer = null;
}

function onVisibilityChange(): void {
  // Coming back to the tab should show current data immediately rather than up to
  // 30 seconds later.
  if (document.visibilityState === "visible") void load();
}

onMounted(() => {
  startAutoRefresh();
  document.addEventListener("visibilitychange", onVisibilityChange);
});

onBeforeUnmount(() => {
  stopAutoRefresh();
  document.removeEventListener("visibilitychange", onVisibilityChange);
});
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-6xl">
      <!-- Stage 4.2/4.3 — topology canvas + node details. -->
      <div class="mb-4 grid gap-3 lg:grid-cols-[1fr_20rem]">
        <MeshCanvas
          :agents="topology"
          :selected-id="selectedAgentId"
          @select="selectNode"
          @touch="onTouch"
          @remove="onRemoveNode"
          @circuit="onResetCircuit"
          @register="onRegisterPeer"
          @cleanup="onCleanupStale"
        />
        <NodeDetailsPanel :agent="selectedAgent" @close="selectedAgentId = null" />
      </div>

      <!-- Stage 4.7 — traffic, skill heatmap and eval summary, from the same dashboard call. -->
      <div class="mb-4">
        <MeshDashboardPanels :dashboard="dashboard" />
      </div>

      <!-- Stage 4.6 / 4.8 — shared memory browser + circuit breakers. -->
      <div class="mb-4 grid gap-3 lg:grid-cols-2">
        <SharedMemoryPanel />
        <CircuitPanel />
      </div>

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
                <span class="text-xs" :class="scoreClass(num(row.healthScore))">
                  {{ num(row.healthScore) === null ? "—" : Math.round(num(row.healthScore)! * 100) + "%" }}
                </span>
              </td>
              <td class="px-3 py-2 text-xs" :class="circuitClass(row.circuitState)">
                {{ row.circuitState ?? "—" }}
              </td>
              <td class="px-3 py-2 text-xs text-secondary">
                {{ num(row.avgLatencyMs) === null ? "—" : `${Math.round(num(row.avgLatencyMs)!)}ms` }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Routing inspector (Stage 6.4) -->
      <div class="mb-6 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-1 text-sm font-medium text-app">{{ t("mesh.routing") }}</h2>
        <p class="mb-3 text-xs text-secondary">{{ t("mesh.routingHint") }}</p>

        <div class="flex flex-wrap items-end gap-2">
          <label class="flex min-w-48 flex-1 flex-col gap-1 text-xs text-secondary">
            {{ t("mesh.capability") }}
            <input
              v-model="capability"
              type="text"
              spellcheck="false"
              placeholder="code-review"
              :aria-label="t('mesh.capability')"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
              @keyup.enter="searchCapabilities(capability)"
            />
          </label>
          <label class="flex w-36 flex-col gap-1 text-xs text-secondary">
            {{ t("mesh.maxCost") }}
            <input
              v-model="maxCost"
              type="text"
              inputmode="decimal"
              spellcheck="false"
              placeholder="—"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
              @keyup.enter="inspectRouting"
            />
          </label>
          <button
            class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50"
            :disabled="routing || !capability.trim() || !client"
            @click="inspectRouting"
          >
            {{ routing ? t("common.saving") : t("mesh.route") }}
          </button>
        </div>

        <p v-if="routeError" class="mt-3 text-sm text-amber-400">{{ routeError }}</p>
        <p
          v-else-if="routes && routes.count === 0"
          class="mt-3 text-sm text-secondary"
        >
          {{ t("mesh.noRoutes") }}
        </p>

        <div v-else-if="routes" class="mt-3 space-y-1">
          <!-- 4.5 — sort and filters, applied over the router's own ranking. -->
          <div class="mb-2 flex flex-wrap items-center gap-3 text-[11px]">
            <label class="flex items-center gap-1">
              {{ t("mesh.sortBy") }}
              <select
                v-model="sortBy"
                :aria-label="t('mesh.sortBy')"
                class="rounded border border-app bg-app px-2 py-0.5 text-[11px] text-app"
              >
                <option value="composite">{{ t("mesh.sortComposite") }}</option>
                <option value="health">{{ t("mesh.sortHealth") }}</option>
                <option value="latency">{{ t("mesh.sortLatency") }}</option>
                <option value="cost">{{ t("mesh.sortCost") }}</option>
              </select>
            </label>
            <label class="flex items-center gap-1 text-secondary">
              <input v-model="onlyTrustPassed" type="checkbox" />
              {{ t("mesh.filterTrust") }}
            </label>
            <label class="flex items-center gap-1 text-secondary">
              <input v-model="onlyHealthy" type="checkbox" />
              {{ t("mesh.filterHealthy") }}
            </label>
            <label class="flex items-center gap-1 text-secondary">
              <input v-model="onlyCircuitClosed" type="checkbox" />
              {{ t("mesh.filterCircuit") }}
            </label>
          </div>

          <div
            v-for="(c, i) in visibleCandidates"
            :key="c.agentId"
            data-testid="route-row"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-xs"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="text-app">
                <span class="text-secondary">#{{ i + 1 }}</span> {{ c.displayName }}
              </span>
              <span class="flex items-center gap-2">
                <span :class="c.trustPassed ? 'text-emerald-400' : 'text-red-400'">
                  {{ c.trustPassed ? t("mesh.trustPassed") : t("mesh.trustFailed") }}
                </span>
                <span class="text-secondary">{{ c.circuitState }}</span>
                <span class="text-app">score {{ num(c.compositeScore) }}</span>
              </span>
            </div>
            <div class="mt-1 flex flex-wrap gap-3 text-[11px] text-secondary">
              <span>{{ t("mesh.health") }}: {{ num(c.healthScore) }}</span>
              <span>{{ t("mesh.quality") }}: {{ num(c.qualityScore) }}</span>
              <span>{{ t("mesh.latency") }}: {{ num(c.latencyMs) }}ms</span>
              <span>{{ t("mesh.cost") }}: ${{ num(c.costHintUsd) }}</span>
              <span>{{ t("mesh.trust") }}: {{ c.trustLevel }}</span>
            </div>
          </div>
        </div>
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