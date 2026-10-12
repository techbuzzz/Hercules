<script setup lang="ts">
/**
 * Stage 7 — Consensus: fan one prompt out to several agents and aggregate.
 *
 * Orchestrated entirely in Studio (the task file's own dependency line: "нет — Studio
 * orchestrates parallel /api/chat"), so this view talks to N agents at once through
 * `connections.clientFor` rather than the single active client.
 *
 * Not to be confused with `DecisionsView.vue`, the human-in-the-loop approvals queue.
 * The two shared the name "Consensus" before Stage 7 was built.
 */
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import AgentSelector from "../components/consensus/AgentSelector.vue";
import ResponseColumn from "../components/consensus/ResponseColumn.vue";
import LlmJudgePanel from "../components/consensus/LlmJudgePanel.vue";
import { useConsensusStore } from "../stores/consensus";
import { useConnectionsStore } from "../stores/connections";
import { useNotifications } from "../composables/useNotifications";
import type { ConsensusSessionDto } from "../sdk/types";

const { t } = useI18n();
const consensus = useConsensusStore();
const connections = useConnectionsStore();
const { notify } = useNotifications();

const judgeError = ref<string | null>(null);

// ---- History (Stage 7.8) ----
// Persisted agent-side, not in the browser: the record of what several agents said is
// worth keeping across a restart, and answers are not the browser's business to store.
const history = ref<ConsensusSessionDto[]>([]);
const historyOpen = ref(false);
const historyLoading = ref(false);
const viewing = ref<ConsensusSessionDto | null>(null);

async function loadHistory(): Promise<void> {
  if (!connections.client) return;
  historyLoading.value = true;
  try {
    const res = await connections.client.listConsensusSessions();
    history.value = res.items ?? [];
  } catch {
    history.value = [];
  } finally {
    historyLoading.value = false;
  }
}

function toggleHistory(): void {
  historyOpen.value = !historyOpen.value;
  viewing.value = null;
  if (historyOpen.value) void loadHistory();
}

/** Best-effort: a history write failing must not disturb the round that just finished. */
async function recordRound(): Promise<void> {
  const api = connections.client;
  if (!api) return;
  const answered = consensus.responses.filter(
    (r): r is typeof r & { response: NonNullable<typeof r.response> } =>
      r.status === "done" && r.response !== null,
  );
  if (answered.length === 0 || !consensus.prompt.trim()) return;

  try {
    await api.saveConsensusSession({
      prompt: consensus.prompt.trim(),
      selectedAgents: consensus.selectedIds,
      aggregationMode: consensus.aggregationMode,
      responses: answered.map((r) => ({
        agentName: r.agentName,
        connectionId: r.connectionId,
        answer: r.response.answer ?? "",
      })),
      ...(consensus.aggregatedResult ? { result: consensus.aggregatedResult } : {}),
      ...(consensus.judgeRationale ? { judgeRationale: consensus.judgeRationale } : {}),
    });
    if (historyOpen.value) await loadHistory();
  } catch {
    // Deliberately silent — see the comment above.
  }
}

const hasRound = computed(() => consensus.responses.length > 0);
const busy = computed(() => consensus.status === "querying" || consensus.status === "aggregating");

async function runJudge(): Promise<void> {
  judgeError.value = null;
  try {
    await consensus.aggregateLlmJudge();
  } catch (e) {
    // The per-agent answers stay on screen; only the judge failed.
    judgeError.value = e instanceof Error ? e.message : String(e);
  }
}

function pick(connectionId: string): void {
  judgeError.value = null;
  consensus.aggregateManual(connectionId);
}

// Stage 7: a fan-out across several agents can take a while, and the operator may have
// navigated away. Notify on completion — but only with a count, since a round with a dead
// agent is not the same outcome as a clean one.
watch(
  () => consensus.status,
  (next, prev) => {
    if (next !== "done" || prev === "done") return;
    void notify(
      t("consensus.title"),
      t("consensus.notifyDone", {
        done: consensus.doneCount,
        failed: consensus.errorCount,
      }),
    );
    void recordRound();
  },
);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto flex w-full max-w-6xl flex-1 flex-col">
      <div class="mb-4 flex items-start justify-between">
        <div>
          <h1 class="text-lg font-semibold text-app">{{ t("consensus.title") }}</h1>
          <p class="text-sm text-secondary">{{ t("consensus.subtitle") }}</p>
        </div>
        <button
          v-if="hasRound"
          class="rounded-lg border border-app px-3 py-1.5 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
          :disabled="busy"
          @click="consensus.clearRound()"
        >
          {{ t("consensus.newRound") }}
        </button>
        <button
          class="rounded-lg border border-app px-3 py-1.5 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
          :disabled="!connections.client"
          @click="toggleHistory"
        >
          {{ historyOpen ? t("consensus.hideHistory") : t("consensus.history") }}
          <span v-if="history.length > 0">({{ history.length }})</span>
        </button>
      </div>

      <!-- Stage 7.8 — past rounds, read-only. -->
      <div v-if="historyOpen" class="mb-4 rounded-xl border border-app bg-secondary p-3">
        <p v-if="historyLoading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="history.length === 0" class="text-xs text-secondary">
          {{ t("consensus.historyEmpty") }}
        </p>

        <div v-else class="space-y-1">
          <button
            v-for="s in history"
            :key="s.id"
            type="button"
            class="block w-full rounded-lg border border-app bg-app px-3 py-2 text-left transition-colors hover:border-emerald-500/50"
            :aria-pressed="viewing?.id === s.id"
            @click="viewing = viewing?.id === s.id ? null : s"
          >
            <p class="truncate text-xs text-app">{{ s.prompt }}</p>
            <p class="text-[10px] text-secondary">
              {{ s.responses.length }} {{ t("consensus.historyAnswers") }} ·
              {{ s.aggregationMode }} · {{ new Date(s.createdAt).toLocaleString() }}
            </p>
          </button>
        </div>

        <!-- Read-only detail: what each agent said, and what was chosen. -->
        <div
          v-if="viewing"
          class="mt-2 rounded-lg border border-app bg-app p-3"
          data-testid="consensus-history-detail"
        >
          <p class="mb-2 text-xs text-app">{{ t("consensus.historyDetail") }}</p>
          <div
            v-for="r in viewing.responses"
            :key="r.connectionId"
            class="mb-2 rounded border border-app bg-secondary px-2 py-1.5"
          >
            <p class="text-[10px] text-secondary">{{ r.agentName }}</p>
            <p class="whitespace-pre-wrap text-[11px] text-app">{{ r.answer }}</p>
          </div>
          <p
            v-if="viewing.result"
            class="mt-2 border-l-2 border-emerald-600/50 pl-2 text-[11px] text-app"
          >
            {{ t("consensus.historyChosen") }}: {{ viewing.result }}
          </p>
          <p v-if="viewing.judgeRationale" class="mt-1 text-[10px] italic text-secondary">
            {{ viewing.judgeRationale }}
          </p>
        </div>
      </div>

      <!-- Setup: agents + prompt -->
      <div class="mb-4 space-y-3 rounded-xl border border-app bg-secondary p-4">
        <AgentSelector />

        <textarea
          v-model="consensus.prompt"
          rows="3"
          :placeholder="t('consensus.promptPlaceholder')"
          class="w-full resize-y rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500 disabled:opacity-50"
          :disabled="busy"
        />

        <div class="flex items-center gap-2">
          <button
            type="button"
            class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
            :disabled="!consensus.canSend"
            @click="consensus.send()"
          >
            {{ consensus.status === "querying" ? t("consensus.queryingAll") : t("consensus.sendToAll") }}
          </button>
          <button
            v-if="hasRound"
            type="button"
            class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
            :disabled="busy"
            @click="consensus.reset()"
          >
            {{ t("consensus.reset") }}
          </button>
          <span v-if="hasRound && !busy" class="text-[11px] text-secondary">
            {{ t("consensus.tally", { done: consensus.doneCount, failed: consensus.errorCount }) }}
          </span>
        </div>
      </div>

      <!-- Empty state -->
      <p v-if="!hasRound" class="py-10 text-center text-sm text-secondary">
        {{ t("consensus.empty") }}
      </p>

      <!-- Response columns -->
      <div
        v-else
        class="grid min-h-0 flex-1 gap-2"
        :style="{ gridTemplateColumns: `repeat(${Math.min(consensus.responses.length, 4)}, minmax(0, 1fr))` }"
      >
        <ResponseColumn
          v-for="r in consensus.responses"
          :key="r.connectionId"
          :response="r"
          :pickable="consensus.aggregationMode === 'manual'"
          :picked="consensus.pickedConnectionId === r.connectionId"
          @pick="pick"
        />
      </div>

      <!-- Aggregation -->
      <div v-if="hasRound" class="mt-4 rounded-xl border border-app bg-secondary p-4">
        <div class="mb-3 flex items-center gap-2">
          <h2 class="text-sm font-medium text-app">{{ t("consensus.aggregate") }}</h2>
          <div class="flex rounded-lg border border-app p-0.5">
            <button
              type="button"
              class="rounded-md px-2.5 py-1 text-xs transition-colors disabled:opacity-50"
              :class="consensus.aggregationMode === 'manual' ? 'bg-emerald-600 text-white' : 'text-secondary'"
              :disabled="busy"
              @click="consensus.aggregationMode = 'manual'"
            >
              {{ t("consensus.modeManual") }}
            </button>
            <button
              type="button"
              class="rounded-md px-2.5 py-1 text-xs transition-colors disabled:opacity-50"
              :class="consensus.aggregationMode === 'llm-judge' ? 'bg-emerald-600 text-white' : 'text-secondary'"
              :disabled="busy"
              @click="consensus.aggregationMode = 'llm-judge'"
            >
              {{ t("consensus.modeJudge") }}
            </button>
          </div>
        </div>

        <!-- Shown in both modes: an incomplete round is worth knowing about before
             choosing an aggregation mode, not only once the judge is involved. -->
        <p v-if="consensus.errorCount > 0" class="mb-3 text-[11px] text-amber-400">
          {{
            t("consensus.partialRound", {
              done: consensus.doneCount,
              failed: consensus.errorCount,
            })
          }}
        </p>

        <p v-if="consensus.aggregationMode === 'manual'" class="text-xs text-secondary">
          {{ t("consensus.manualHint") }}
        </p>

        <LlmJudgePanel v-else @run="runJudge" />

        <p v-if="judgeError" class="mt-2 text-xs text-red-400">{{ judgeError }}</p>

        <div
          v-if="consensus.aggregatedResult !== null"
          class="mt-3 rounded-lg border border-emerald-600/40 bg-emerald-500/5 p-3"
        >
          <p class="mb-1 text-[11px] text-secondary">
            {{ consensus.aggregatedByJudge() ? t("consensus.judgeResult") : t("consensus.pickedResult") }}
          </p>
          <!-- Stage 7.5: the judge's reason for its pick. -->
          <p
            v-if="consensus.judgeRationale"
            class="mb-2 border-l-2 border-emerald-600/50 pl-2 text-[11px] italic text-secondary"
          >
            {{ consensus.judgeRationale }}
          </p>
          <div class="whitespace-pre-wrap text-sm leading-relaxed text-app">
            {{ consensus.aggregatedResult }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>