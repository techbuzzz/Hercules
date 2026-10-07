<script setup lang="ts">
/**
 * Stage 7.5 — LLM-judge aggregation panel.
 *
 * The judge may be one of the agents already queried or any other connected agent. It is
 * always an ordinary `/api/chat` round: the store builds the comparison prompt, so this
 * component only decides *who* judges and surfaces the result.
 */
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useConsensusStore } from "../../stores/consensus";

const { t } = useI18n();
const connections = useConnectionsStore();
const consensus = useConsensusStore();

const candidates = computed(() => connections.list.filter((c) => c.status === "online"));

const running = computed(() => consensus.status === "aggregating");
const ready = computed(() => consensus.doneCount > 0 && !running.value);

/**
 * Emits rather than calling the store directly, so the view that owns this panel can
 * surface a judge failure without discarding the per-agent answers underneath.
 */
const emit = defineEmits<(e: "run") => void>();
</script>

<template>
  <div class="space-y-2">
    <label class="flex flex-col gap-1">
      <span class="text-[11px] text-secondary">{{ t("consensus.judge") }}</span>
      <select
        v-model="consensus.judgeConnectionId"
        class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500 disabled:opacity-50"
        :disabled="running"
      >
        <option :value="null">{{ t("consensus.judgePick") }}</option>
        <option v-for="c in candidates" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select>
    </label>

    <button
      type="button"
      class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
      :disabled="!ready || !consensus.judgeConnectionId"
      @click="emit('run')"
    >
      {{ running ? t("common.saving") : t("consensus.runJudge") }}
    </button>
  </div>
</template>