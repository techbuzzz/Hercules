<script setup lang="ts">
/**
 * Context view — token budget and session distillation (Stage 6.6).
 *
 * The distillation service already exists on the agent (task_102); this surfaces
 * it. Two things it deliberately does NOT do: claim a saving that did not happen
 * (a run over zero tokens reports 0%, not a fabricated figure), and hide the
 * case where distillation is switched off in config — the agent rejects it and
 * the reason is shown as-is.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { ContextBudgetDto, ContextSummaryDto, ContextDistillResultDto } from "../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const sessionId = ref("default");
const budget = ref<ContextBudgetDto | null>(null);
const summary = ref<ContextSummaryDto | null>(null);
const lastRun = ref<ContextDistillResultDto | null>(null);
const loading = ref(false);
const distilling = ref(false);
const summaryError = ref<string | null>(null);

const client = computed(() => connections.client);

/** `budgetUsedPct` is a double; clamp so a rounding overshoot cannot overflow the bar. */
const usedPct = computed(() => {
  const raw = budget.value ? Number(budget.value.budgetUsedPct) : 0;
  return Number.isFinite(raw) ? Math.max(0, Math.min(100, raw)) : 0;
});

const barClass = computed(() =>
  usedPct.value >= 90 ? "bg-red-500" : usedPct.value >= 70 ? "bg-amber-500" : "bg-emerald-500",
);

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    budget.value = await client.value.getContextBudget();
  } catch (e) {
    budget.value = null;
    toast.warn(`${t("context.budgetFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
  await loadSummary();
}

async function loadSummary(): Promise<void> {
  if (!client.value || !sessionId.value.trim()) return;
  summaryError.value = null;
  try {
    summary.value = await client.value.getContextSummary(sessionId.value.trim());
  } catch (e) {
    summary.value = null;
    // The agent 404s when the context assembly is disabled — surface that
    // verbatim rather than showing an empty panel that looks like "no data".
    summaryError.value = e instanceof Error ? e.message : String(e);
  }
}

async function distill(): Promise<void> {
  if (!client.value || distilling.value || !sessionId.value.trim()) return;
  distilling.value = true;
  try {
    const result = await client.value.distillContext(sessionId.value.trim());
    lastRun.value = result;
    toast.success(
      `${t("context.distilled")}: ${result.summariesCreated} ${t("context.summaries")}, ` +
        `${result.keyFactsExtracted} ${t("context.keyFacts")}`,
    );
    await loadSummary();
    await load();
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    toast.error(`${t("context.distillFailed")}: ${msg}`);
  } finally {
    distilling.value = false;
  }
}

function fmt(n: number | string | null | undefined): string {
  if (n === null || n === undefined) return "—";
  return Number(n).toLocaleString();
}

onMounted(load);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-4xl">
      <div class="mb-4">
        <h1 class="text-lg font-semibold text-app">{{ t("context.title") }}</h1>
        <p class="text-sm text-secondary">{{ t("context.subtitle") }}</p>
      </div>

      <!-- Token budget -->
      <section class="mb-6 rounded-xl border border-app bg-secondary p-4">
        <div class="mb-2 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">{{ t("context.budget") }}</h2>
          <button
            class="text-xs text-secondary hover:text-app"
            :disabled="loading"
            @click="load"
          >
            {{ t("common.refresh") }}
          </button>
        </div>

        <p v-if="loading" class="text-sm text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="!budget" class="text-sm text-secondary">{{ t("context.budgetUnavailable") }}</p>

        <template v-else>
          <div class="mb-1 flex items-baseline justify-between text-xs text-secondary">
            <span>{{ t("context.used") }}: {{ fmt(budget.usedTokens) }}</span>
            <span>{{ t("context.of") }} {{ fmt(budget.maxTokens) }}</span>
          </div>
          <div class="mb-2 h-2 overflow-hidden rounded-full bg-tertiary">
            <div class="h-full rounded-full transition-all" :class="barClass" :style="{ width: `${usedPct}%` }" />
          </div>
          <div class="flex justify-between text-xs text-secondary">
            <span>{{ t("context.remaining") }}: {{ fmt(budget.remainingTokens) }}</span>
            <span>{{ usedPct }}%</span>
          </div>
        </template>
      </section>

      <!-- Distillation -->
      <section class="mb-6 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("context.distillation") }}</h2>

        <label class="mb-3 flex flex-col gap-1 text-xs text-secondary">
          {{ t("context.sessionId") }}
          <input
            v-model="sessionId"
            type="text"
            spellcheck="false"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-sm text-app outline-none focus:border-emerald-500"
            @keyup.enter="distill"
          />
        </label>

        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50"
          :disabled="distilling || !client || !sessionId.trim()"
          @click="distill"
        >
          {{ distilling ? t("common.saving") : t("context.distillNow") }}
        </button>

        <div
          v-if="lastRun"
          class="mt-4 grid grid-cols-2 gap-3 border-t border-app pt-3 text-xs sm:grid-cols-4"
        >
          <div>
            <div class="text-secondary">{{ t("context.tokensBefore") }}</div>
            <div class="text-app">{{ fmt(lastRun.tokensBefore) }}</div>
          </div>
          <div>
            <div class="text-secondary">{{ t("context.tokensAfter") }}</div>
            <div class="text-app">{{ fmt(lastRun.tokensAfter) }}</div>
          </div>
          <div>
            <div class="text-secondary">{{ t("context.savings") }}</div>
            <div class="text-app">{{ Number(lastRun.tokenSavingsPct) }}%</div>
          </div>
          <div>
            <div class="text-secondary">{{ t("context.keyFacts") }}</div>
            <div class="text-app">{{ fmt(lastRun.keyFactsExtracted) }}</div>
          </div>
        </div>
      </section>

      <!-- Summary -->
      <section class="rounded-xl border border-app bg-secondary p-4">
        <div class="mb-2 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">{{ t("context.summary") }}</h2>
          <button class="text-xs text-secondary hover:text-app" @click="loadSummary">
            {{ t("common.refresh") }}
          </button>
        </div>

        <p v-if="summaryError" class="text-sm text-amber-400">{{ summaryError }}</p>
        <p v-else-if="summary?.empty" class="text-sm text-secondary">{{ t("context.summaryEmpty") }}</p>
        <pre
          v-else-if="summary"
          class="max-h-96 overflow-auto whitespace-pre-wrap rounded-lg bg-tertiary p-3 font-mono text-xs text-app"
          >{{ summary.summary }}</pre
        >
      </section>
    </div>
  </div>
</template>