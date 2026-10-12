<script setup lang="ts">
/**
 * Stage 4.7 — dashboard panels for the data the mesh dashboard already returns but the
 * view was discarding: traffic counters, the skill heatmap and the evaluation summary.
 *
 * No new endpoints: all three come from the same `GET /api/mesh/dashboard` payload the
 * topology canvas already fetches.
 */
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import type { MeshDashboardDto } from "../../sdk/types";

const props = defineProps<{ dashboard: MeshDashboardDto | null }>();

const { t } = useI18n();

/** int32/int64/double all generate as `number | string`; the wire value is a number. */
const num = (value: unknown, digits = 0): string => {
  const n = Number(value);
  if (!Number.isFinite(n)) return "—";
  return n.toFixed(digits);
};

const traffic = computed(() => props.dashboard?.traffic ?? null);

const trafficRows = computed(() => {
  const tr = traffic.value;
  if (!tr) return [];
  return [
    { key: "totalRequests", label: t("mesh.traffic.total"), value: num(tr.totalRequests) },
    { key: "fanOut", label: t("mesh.traffic.fanOut"), value: num(tr.fanOutRequests) },
    { key: "delegations", label: t("mesh.traffic.delegations"), value: num(tr.meshDelegations) },
    { key: "routerHits", label: t("mesh.traffic.routerHits"), value: num(tr.meshRouterHits) },
    { key: "rejections", label: t("mesh.traffic.rejections"), value: num(tr.circuitBreakerRejections) },
    { key: "avgLatency", label: t("mesh.traffic.avgLatency"), value: `${num(tr.avgLatencyMs, 1)} ms` },
  ];
});

const heatmap = computed(() => props.dashboard?.skillHeatmap ?? null);
const heatEntries = computed(() => heatmap.value?.skills ?? []);

/** Busiest skill, used to scale the bar widths so the chart has a visible maximum. */
const heatMax = computed(() =>
  Math.max(1, ...heatEntries.value.map((s) => Number(s.totalUses) || 0)),
);

const evals = computed(() => props.dashboard?.evalSummary ?? null);
</script>

<template>
  <div v-if="dashboard" class="grid gap-3 lg:grid-cols-3">
    <!-- Traffic -->
    <div class="rounded-xl border border-app bg-secondary p-3">
      <h3 class="mb-2 text-sm font-medium text-app">{{ t("mesh.trafficTitle") }}</h3>
      <dl v-if="traffic" class="space-y-1 text-[11px]">
        <div v-for="row in trafficRows" :key="row.key" class="flex justify-between gap-2">
          <dt class="text-secondary">{{ row.label }}</dt>
          <dd class="font-mono text-app">{{ row.value }}</dd>
        </div>
      </dl>
      <p v-else class="text-[11px] text-secondary">{{ t("mesh.trafficEmpty") }}</p>
    </div>

    <!-- Skill heatmap -->
    <div class="rounded-xl border border-app bg-secondary p-3">
      <h3 class="mb-2 text-sm font-medium text-app">
        {{ t("mesh.heatmapTitle") }}
        <span v-if="heatmap" class="ml-1 text-[11px] text-secondary">
          {{ heatmap.totalSkills }} {{ t("mesh.heatmapSkills") }}
        </span>
      </h3>
      <p v-if="heatEntries.length === 0" class="text-[11px] text-secondary">
        {{ t("mesh.heatmapEmpty") }}
      </p>
      <div v-else class="space-y-1">
        <div v-for="s in heatEntries" :key="s.skillId" class="text-[11px]">
          <div class="flex justify-between gap-2">
            <span class="truncate text-secondary">{{ s.skillName || s.skillId }}</span>
            <span class="font-mono text-app">{{ num(s.totalUses) }}</span>
          </div>
          <div class="mt-0.5 h-1 w-full rounded bg-tertiary">
            <div
              class="h-1 rounded bg-emerald-600/70"
              :style="{ width: `${Math.min(100, ((Number(s.totalUses) || 0) / heatMax) * 100)}%` }"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Eval summary -->
    <div class="rounded-xl border border-app bg-secondary p-3">
      <h3 class="mb-2 text-sm font-medium text-app">{{ t("mesh.evalTitle") }}</h3>
      <p v-if="!evals" class="text-[11px] text-secondary">{{ t("mesh.evalEmpty") }}</p>
      <template v-else>
        <div class="mb-2 flex gap-3 text-[11px]">
          <span class="text-secondary">
            {{ t("mesh.evalTotal") }}: <span class="font-mono text-app">{{ num(evals.totalRuns) }}</span>
          </span>
          <span class="text-emerald-400">
            {{ t("mesh.evalPassed") }}: <span class="font-mono">{{ num(evals.passedRuns) }}</span>
          </span>
          <span class="text-red-400">
            {{ t("mesh.evalFailed") }}: <span class="font-mono">{{ num(evals.failedRuns) }}</span>
          </span>
        </div>
        <p v-if="evals.recentRuns?.length" class="mb-1 text-[11px] text-secondary">
          {{ t("mesh.evalRecent") }}
        </p>
        <div
          v-for="run in evals.recentRuns ?? []"
          :key="run.runId"
          class="flex items-center justify-between gap-2 text-[11px]"
        >
          <span class="truncate text-secondary">{{ run.scenarioType }}</span>
          <span :class="run.passed ? 'text-emerald-400' : 'text-red-400'">
            {{ run.passed ? t("mesh.evalPass") : t("mesh.evalFail") }} · {{ num(run.score, 2) }}
          </span>
        </div>
      </template>
    </div>
  </div>
</template>