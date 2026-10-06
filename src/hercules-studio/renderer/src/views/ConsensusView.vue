<script setup lang="ts">
/**
 * Consensus view — the human-in-the-loop queue.
 *
 * Backs onto the agent's approvals (tool calls awaiting a human) and escalations
 * (decisions an agent could not make on its own). This is where a multi-agent
 * system actually needs a person, so both lists are actionable rather than
 * read-only, and every action re-reads the pending set instead of guessing
 * whether the item left the queue.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { ApprovalDto, EscalationDto } from "../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const approvals = ref<ApprovalDto[]>([]);
const escalations = ref<EscalationDto[]>([]);
const loading = ref(false);
const minSeverity = ref<string | null>(null);
const busy = ref<Set<string>>(new Set());
const expanded = ref<Set<string>>(new Set());

const client = computed(() => connections.client);
const totalPending = computed(() => approvals.value.length + escalations.value.length);

function severityClass(severity: string): string {
  switch (severity.toLowerCase()) {
    case "critical":
      return "bg-red-500/20 text-red-400";
    case "high":
      return "bg-amber-500/20 text-amber-400";
    case "low":
      return "bg-tertiary text-secondary";
    default:
      return "bg-sky-500/20 text-sky-400";
  }
}

function mark(id: string, on: boolean): void {
  const next = new Set(busy.value);
  if (on) next.add(id);
  else next.delete(id);
  busy.value = next;
}

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    // Each list is independent: an escalation outage must not hide approvals.
    const [a, e] = await Promise.allSettled([
      client.value.getPendingApprovals(),
      client.value.getPendingEscalations(minSeverity.value ?? undefined),
    ]);
    approvals.value = a.status === "fulfilled" && Array.isArray(a.value.approvals) ? a.value.approvals : [];
    escalations.value =
      e.status === "fulfilled" && Array.isArray(e.value.escalations) ? e.value.escalations : [];
  } finally {
    loading.value = false;
  }
}

async function act(
  id: string,
  action: () => Promise<unknown>,
  successMessage: string,
): Promise<void> {
  if (!client.value || busy.value.has(id)) return;
  mark(id, true);
  try {
    await action();
    toast.success(successMessage);
    await load(); // re-read: never assume the item left the queue
  } catch (err) {
    toast.error(`${t("consensus.actionFailed")}: ${err instanceof Error ? err.message : String(err)}`);
  } finally {
    mark(id, false);
  }
}

const approveApproval = (id: string) =>
  act(id, () => client.value!.approveApproval(id), t("consensus.approved"));
const denyApproval = (id: string) =>
  act(id, () => client.value!.denyApproval(id), t("consensus.denied"));
const approveEscalation = (id: string) =>
  act(id, () => client.value!.approveEscalation(id), t("consensus.approved"));
const denyEscalation = (id: string) =>
  act(id, () => client.value!.denyEscalation(id), t("consensus.denied"));

async function approveAll(): Promise<void> {
  const ids = escalations.value.map((e) => e.escalationId);
  if (ids.length === 0 || !client.value) return;
  await act("__all__", () => client.value!.batchApproveEscalations(ids), t("consensus.batchApproved"));
}

function toggle(id: string): void {
  const next = new Set(expanded.value);
  if (next.has(id)) next.delete(id);
  else next.add(id);
  expanded.value = next;
}

function pretty(json: string): string {
  if (!json) return "";
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

onMounted(load);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-4xl">
      <!-- Header -->
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h1 class="text-lg font-semibold text-app">{{ t("consensus.title") }}</h1>
          <p class="text-sm text-secondary">{{ t("consensus.subtitle") }}</p>
        </div>
        <button
          class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
          :disabled="loading"
          @click="load"
        >
          {{ t("common.refresh") }}
        </button>
      </div>

      <p v-if="!loading && totalPending === 0" class="text-sm text-secondary">
        {{ t("consensus.empty") }}
      </p>
      <p v-else-if="loading" class="text-sm text-secondary">{{ t("common.loading") }}</p>

      <!-- Approvals -->
      <section v-if="approvals.length > 0" class="mb-6">
        <h2 class="mb-2 text-sm font-medium text-app">
          {{ t("consensus.approvals") }} ({{ approvals.length }})
        </h2>
        <div class="space-y-2">
          <div
            v-for="a in approvals"
            :key="a.requestId"
            class="rounded-xl border border-app bg-secondary p-3"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-medium text-app">{{ a.toolName }}</span>
                  <span class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary">
                    {{ a.status }}
                  </span>
                </div>
                <p v-if="a.reason" class="mt-0.5 text-xs text-secondary">{{ a.reason }}</p>
                <p class="mt-0.5 text-[11px] text-secondary">{{ a.requestedAt }}</p>
              </div>
              <div class="flex shrink-0 gap-2">
                <button
                  class="rounded-lg bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
                  :disabled="busy.has(a.requestId)"
                  :aria-label="`${t('common.confirm')} ${a.toolName}`"
                  @click="approveApproval(a.requestId)"
                >
                  {{ t("common.confirm") }}
                </button>
                <button
                  class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary disabled:opacity-50"
                  :disabled="busy.has(a.requestId)"
                  :aria-label="`${t('common.no')} ${a.toolName}`"
                  @click="denyApproval(a.requestId)"
                >
                  {{ t("common.no") }}
                </button>
              </div>
            </div>
            <details
              v-if="a.argumentsJson"
              class="mt-2 cursor-pointer text-xs text-secondary"
              @toggle="toggle(a.requestId)"
            >
              <summary>{{ t("consensus.arguments") }}</summary>
              <pre class="mt-1 overflow-auto rounded-lg bg-tertiary p-2 font-mono text-[11px] text-app">{{
                pretty(a.argumentsJson)
              }}</pre>
            </details>
          </div>
        </div>
      </section>

      <!-- Escalations -->
      <section v-if="escalations.length > 0 || approvals.length === 0">
        <div class="mb-2 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">
            {{ t("consensus.escalations") }} ({{ escalations.length }})
          </h2>
          <div class="flex items-center gap-2">
            <select
              v-model="minSeverity"
              class="rounded-lg border border-app bg-tertiary px-2 py-1 text-xs text-app outline-none"
              @change="load"
            >
              <option :value="null">{{ t("consensus.allSeverities") }}</option>
              <option value="low">low</option>
              <option value="medium">medium</option>
              <option value="high">high</option>
              <option value="critical">critical</option>
            </select>
            <button
              v-if="escalations.length > 1"
              class="rounded-lg border border-app px-2 py-1 text-xs text-app hover:bg-tertiary disabled:opacity-50"
              :disabled="busy.has('__all__')"
              @click="approveAll"
            >
              {{ t("consensus.approveAll") }}
            </button>
          </div>
        </div>

        <p v-if="escalations.length === 0" class="text-sm text-secondary">
          {{ t("consensus.noEscalations") }}
        </p>

        <div class="space-y-2">
          <div
            v-for="e in escalations"
            :key="e.escalationId"
            class="rounded-xl border border-app bg-secondary p-3"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="text-sm font-medium text-app">
                    {{ e.toolOrIntentName || e.type }}
                  </span>
                  <span class="rounded px-1.5 py-0.5 text-[10px]" :class="severityClass(e.severity)">
                    {{ e.severity }}
                  </span>
                </div>
                <p v-if="e.actionPlan" class="mt-1 text-xs text-secondary">{{ e.actionPlan }}</p>
                <p v-if="e.context" class="mt-0.5 text-[11px] text-secondary">{{ e.context }}</p>
                <p class="mt-0.5 text-[11px] text-secondary">
                  {{ e.requestedBy }} · {{ e.createdAt }}
                </p>
              </div>
              <div class="flex shrink-0 gap-2">
                <button
                  class="rounded-lg bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
                  :disabled="busy.has(e.escalationId)"
                  :aria-label="`${t('common.confirm')} ${e.toolOrIntentName || e.type}`"
                  @click="approveEscalation(e.escalationId)"
                >
                  {{ t("common.confirm") }}
                </button>
                <button
                  class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary disabled:opacity-50"
                  :disabled="busy.has(e.escalationId)"
                  :aria-label="`${t('common.no')} ${e.toolOrIntentName || e.type}`"
                  @click="denyEscalation(e.escalationId)"
                >
                  {{ t("common.no") }}
                </button>
              </div>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>