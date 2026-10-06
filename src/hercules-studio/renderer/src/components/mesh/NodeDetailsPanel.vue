<script setup lang="ts">
/**
 * Stage 4.3 — node details panel.
 *
 * Shown when a canvas node is clicked. Read-only by default: the registry exposes
 * touch/remove/circuit-reset as endpoints, but this panel reports the agent's current
 * state so the operator decides what to do from the Mesh view rather than from a
 * right-click menu that can fire a destructive action without confirmation.
 */
import { useI18n } from "vue-i18n";
import type { MeshAgentDto } from "../../sdk/types";

defineProps<{ agent: MeshAgentDto | null }>();

const emit = defineEmits<{ close: [] }>();

const { t } = useI18n();

const asNumber = (value: unknown) => {
  const n = Number(value);
  return Number.isFinite(n) ? n.toFixed(2) : "—";
}

const band = (score: number) => {
  if (score >= 0.7) return "text-emerald-400";
  if (score >= 0.4) return "text-amber-400";
  return "text-red-400";
}
</script>

<template>
  <aside v-if="agent" class="rounded-xl border border-app bg-secondary p-3">
    <div class="mb-2 flex items-start justify-between">
      <div>
        <h3 class="text-sm font-medium text-app">{{ agent.displayName || agent.agentId }}</h3>
        <p class="text-[11px] text-secondary">{{ agent.agentId }}</p>
      </div>
      <button
        type="button"
        class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary"
        @click="emit('close')"
      >
        {{ t("common.close") }}
      </button>
    </div>

    <dl class="space-y-1 text-[11px]">
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailEndpoint") }}</dt>
        <dd class="truncate font-mono text-app" :title="agent.endpoint">{{ agent.endpoint || "—" }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailHealth") }}</dt>
        <dd :class="band(Number(agent.healthScore))">{{ asNumber(agent.healthScore) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailQuality") }}</dt>
        <dd class="text-app">{{ asNumber(agent.qualityScore) }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailLatency") }}</dt>
        <dd class="text-app">{{ asNumber(agent.latencyMs) }} ms</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailTrust") }}</dt>
        <dd class="text-app">{{ agent.trustLevel || "—" }}</dd>
      </div>
      <div class="flex justify-between gap-2">
        <dt class="text-secondary">{{ t("mesh.detailLastSeen") }}</dt>
        <dd class="text-app">{{ agent.lastSeen || "—" }}</dd>
      </div>
    </dl>

    <div v-if="agent.capabilities?.length" class="mt-2">
      <p class="mb-1 text-[11px] text-secondary">
        {{ t("mesh.detailCapabilities") }} ({{ agent.capabilities.length }})
      </p>
      <div class="flex flex-wrap gap-1">
        <span
          v-for="cap in agent.capabilities"
          :key="cap"
          class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary"
        >
          {{ cap }}
        </span>
      </div>
    </div>
  </aside>
</template>