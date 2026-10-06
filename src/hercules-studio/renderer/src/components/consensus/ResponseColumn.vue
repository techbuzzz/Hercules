<script setup lang="ts">
/**
 * Stage 7.4 — one agent's answer in a consensus round.
 *
 * Rendered as plain text with `whitespace-pre-wrap`, matching `ChatView`. The Stage 7
 * task file proposed markdown-it + highlight.js, but neither is a dependency and
 * ChatView does not render markdown either; adding a renderer only here would make the
 * same agent reply look different in two views. Consistency is the cheaper fix, and
 * markdown can be added across both views in one deliberate change if it is wanted.
 */
import { useI18n } from "vue-i18n";
import type { ConsensusResponse } from "../../stores/consensus";

const props = defineProps<{
  response: ConsensusResponse;
  /** Manual-pick mode offers a "Best" affordance; the judge mode does not. */
  pickable: boolean;
  picked: boolean;
}>();

const emit = defineEmits<(e: "pick", connectionId: string) => void>();

const { t } = useI18n();

/** int32/double arrive as `number | string` in the generated schema. */
function asNumber(value: unknown): number | null {
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function confidencePercent(value: unknown): number | null {
  // The agent reports confidence as a 0..1 fraction; tolerate a 0..100 scale too.
  const n = asNumber(value);
  if (n === null) return null;
  return n > 1 ? Math.round(n) : Math.round(n * 100);
}

function borderClass(): string {
  if (props.response.status === "error") return "border-red-500/50";
  if (props.picked) return "border-emerald-500";
  return "border-app";
}
</script>

<template>
  <div class="flex min-h-0 flex-col rounded-xl border bg-secondary p-3" :class="borderClass()">
    <div class="mb-2 flex items-center justify-between gap-2">
      <span class="truncate text-sm font-medium text-app">{{ response.agentName }}</span>
      <button
        v-if="pickable && response.status === 'done'"
        type="button"
        class="shrink-0 rounded-lg px-2 py-0.5 text-[11px] transition-colors"
        :class="
          picked
            ? 'bg-emerald-600 text-white'
            : 'border border-app text-secondary hover:bg-tertiary'
        "
        @click="emit('pick', response.connectionId)"
      >
        {{ picked ? t("consensus.bestPicked") : t("consensus.best") }}
      </button>
    </div>

    <p v-if="response.status === 'querying'" class="text-xs text-secondary">
      {{ t("consensus.querying", { name: response.agentName }) }}
    </p>

    <p v-else-if="response.status === 'error'" class="text-xs text-red-400">
      {{ response.error ?? t("consensus.failed") }}
    </p>

    <div v-else class="min-h-0 flex-1 overflow-y-auto whitespace-pre-wrap text-xs leading-relaxed text-app">
      {{ response.response?.answer }}
    </div>

    <div
      v-if="response.response"
      class="mt-2 flex flex-wrap gap-1.5 text-[10px] text-secondary"
    >
      <span v-if="response.response.mode" class="rounded bg-tertiary px-1.5 py-0.5">
        {{ response.response.mode }}
      </span>
      <span
        v-if="confidencePercent(response.response.confidence) !== null"
        class="rounded bg-tertiary px-1.5 py-0.5"
      >
        {{ t("consensus.confidence", { n: confidencePercent(response.response.confidence) }) }}
      </span>
      <span v-if="response.response.provider" class="rounded bg-tertiary px-1.5 py-0.5">
        {{ response.response.provider }}
      </span>
      <span v-if="response.response.skill?.name" class="rounded bg-tertiary px-1.5 py-0.5">
        {{ response.response.skill.name }}
      </span>
    </div>
  </div>
</template>