<script setup lang="ts">
/**
 * Skills view — list, edit and sandbox-test skills.
 *
 * The run panel replaces the local terminal the Electron shell used to spawn
 * (ADR-0009): execution happens inside the agent's sandbox and output arrives
 * over SSE. There is no client-side process and no PTY.
 */
import { ref, computed, onMounted, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import { useUiStore } from "../stores/ui";
import CodeEditor from "../components/common/CodeEditor.vue";
import PushSkillDialog from "../components/skills/PushSkillDialog.vue";
import SkillTemplateGallery from "../components/skills/SkillTemplateGallery.vue";
import type { SkillTemplate } from "../skills/templates";
import { buildSkillPackage, packageFileName } from "../skills/skillPackage";
import { useNotifications } from "../composables/useNotifications";
import {
  collapseContext,
  diffLines as diffLinesRaw,
  exceedsDiffLimit,
  summarize,
  type DiffLine,
} from "../skills/promptDiff";
import type { SkillDto, SkillDetailDto, SkillPromptRevisionDto } from "../sdk/types";
import type { CodeRunStatus } from "../sdk/client";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();
const { notify } = useNotifications();
const ui = useUiStore();

const skills = ref<SkillDto[]>([]);
const selectedId = ref<string | null>(null);
const detail = ref<SkillDetailDto | null>(null);
const search = ref("");
const loading = ref(false);
const saving = ref(false);
const promptDraft = ref("");

// ---- sandbox run ----
const runCode = ref('Console.WriteLine("hello");\n');
const runState = ref<"idle" | "starting" | "running" | "done" | "failed">("idle");
const runLines = ref<string[]>([]);
const runMeta = ref<{ exitCode?: number; durationMs?: number } | null>(null);
let unsubscribe: (() => void) | null = null;

const client = computed(() => connections.client);

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase();
  if (!q) return skills.value;
  return skills.value.filter(
    (s) =>
      s.name.toLowerCase().includes(q) ||
      s.description.toLowerCase().includes(q) ||
      s.phraseReceivers.some((t) => t.toLowerCase().includes(q)),
  );
});

// ---- Prompt history / diff (Stage 2b) ----
// The agent records every prompt it is about to overwrite, so an edit is
// reversible and a diff has a baseline.
const history = ref<SkillPromptRevisionDto[]>([]);
const historyOpen = ref(false);
const historyLoading = ref(false);
const restoring = ref(false);

/** Stage 3 — cross-agent push dialog. */
const pushOpen = ref(false);

// Stage 3.4 — creating a skill. Studio previously had no create path at all: the editor
// only opened existing skills, so `POST /api/skills` was never called from the client.
const galleryOpen = ref(false);
const creating = ref(false);

/** Stage 3.3 — package the current editor state as a .skillpkg and download it. */
const packaging = ref(false);

async function downloadPackage(): Promise<void> {
  if (!detail.value || packaging.value) return;
  packaging.value = true;
  try {
    const meta = detail.value.meta;
    const version = Number(meta.version) || 1;
    const bytes = buildSkillPackage({
      id: meta.id,
      name: meta.name,
      description: detail.value.descriptionMarkdown,
      phraseReceivers: meta.phraseReceivers,
      prompt: promptDraft.value,
      version,
      createdAt: String(meta.createdAt ?? "").slice(0, 10) || undefined,
    });

    // Blob from raw bytes — the ZIP writer emits Uint8Array, not a string.
    const blob = new Blob([bytes as BlobPart], { type: "application/zip" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = packageFileName(meta.id, version);
    anchor.click();
    // Release the object URL; leaving it pinned leaks the buffer for the session.
    URL.revokeObjectURL(url);
    toast.success(t("skills.packageDownloaded", { name: anchor.download }));
  } catch (e) {
    toast.error(`${t("skills.packageFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    packaging.value = false;
  }
}

async function createFromTemplate(tpl: SkillTemplate): Promise<void> {
  if (!client.value || creating.value) return;
  creating.value = true;
  try {
    const created = await client.value.createSkill({
      name: tpl.draft.name,
      trigger: tpl.draft.phraseReceivers[0] ?? "",
      phraseReceivers: tpl.draft.phraseReceivers,
      prompt: tpl.draft.prompt,
      description: tpl.draft.description,
    });
    galleryOpen.value = false;
    toast.success(t("skills.created", { name: created.name }));
    await load();
    // `select` already sets selectedId, so the operator lands in the editor directly.
    await select(created.id);
  } catch (e) {
    toast.error(`${t("skills.createFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    creating.value = false;
  }
}

// Which revision is being compared against the prompt currently in the editor.
// Diffing against the draft rather than the saved skill is the useful question here:
// "what would restoring this actually change?".
const diffing = ref<SkillPromptRevisionDto | null>(null);

const diffLines = computed<DiffLine[]>(() => {
  if (!diffing.value) return [];
  return collapseContext(diffLinesRaw(diffing.value.prompt ?? "", promptDraft.value));
});

const diffCounts = computed(() => summarize(diffLines.value));
const diffTooLarge = computed(
  () =>
    diffing.value !== null &&
    exceedsDiffLimit(diffing.value.prompt ?? "", promptDraft.value),
);
const diffIsEmpty = computed(
  () => diffing.value !== null && diffCounts.value.added === 0 && diffCounts.value.removed === 0,
);

function toggleDiff(revision: SkillPromptRevisionDto): void {
  diffing.value = diffing.value === revision ? null : revision;
}

async function toggleHistory(): Promise<void> {
  historyOpen.value = !historyOpen.value;
  if (!historyOpen.value || history.value.length > 0 || !client.value || !selectedId.value) return;

  historyLoading.value = true;
  try {
    const res = await client.value.getSkillPromptHistory(selectedId.value);
    history.value = Array.isArray(res.revisions) ? res.revisions : [];
  } catch (e) {
    toast.warn(`${t("skills.historyFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    historyLoading.value = false;
  }
}

async function restore(revision: SkillPromptRevisionDto): Promise<void> {
  if (!client.value || !selectedId.value || restoring.value) return;
  restoring.value = true;
  const previous = promptDraft.value;
  try {
    promptDraft.value = revision.prompt;
    await save();
    // Reload so the list reflects the new revision rather than a stale cache.
    history.value = [];
    await toggleHistory();
    await toggleHistory();
  } catch {
    promptDraft.value = previous;
  } finally {
    restoring.value = false;
  }
}

/** int/double fields generate as `number | string`; the wire value is a number. */
function successRate(skill: SkillDto): number {
  return Number(skill.successRate) || 0;
}

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    skills.value = await client.value.listSkills();
    if (!selectedId.value && skills.value.length > 0) {
      const first = skills.value[0];
        if (first) await select(first.id);
    }
  } catch (e) {
    toast.error(`${t("skills.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

async function select(id: string): Promise<void> {
  if (!client.value) return;
  selectedId.value = id;
  try {
    detail.value = await client.value.getSkill(id);
    promptDraft.value = detail.value.prompt;
  } catch (e) {
    toast.error(`${t("skills.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

const dirty = computed(
  () => detail.value !== null && promptDraft.value !== detail.value.prompt,
);

async function save(): Promise<void> {
  if (!client.value || !detail.value) return;
  saving.value = true;
  try {
    await client.value.updateSkill(detail.value.meta.id, { prompt: promptDraft.value });
    detail.value = { ...detail.value, prompt: promptDraft.value };
    toast.success(t("skills.saved"));
  } catch (e) {
    toast.error(`${t("skills.saveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    saving.value = false;
  }
}

/**
 * Stage 3.5 / 6.10 — dangerous-code pre-check, run before every sandbox execution.
 *
 * The verdict comes from the agent's own `DangerousCodeScanner` via the scan-only endpoint,
 * not from a Studio-side copy of the rules: a duplicate regex set would drift the moment the
 * agent tightened one. A denial blocks the run until the operator explicitly overrides,
 * because some legitimate work does trip the scanner and silently refusing would just push
 * people to work around it in less visible places.
 */
const scanReasons = ref<string[]>([]);
const scanLines = ref<number[]>([]);
const scanOverride = ref(false);
const scanning = ref(false);
/** The exact code the current verdict — and any override — applies to. */
const scannedFor = ref<string | null>(null);

async function run(): Promise<void> {
  if (!client.value || runState.value === "starting" || runState.value === "running") return;

  // The override is scoped to the code the operator actually reviewed: re-running the same
  // snippet keeps it, editing the snippet invalidates it. Clearing it on every invocation
  // made the checkbox undo itself on the second click, so an agreed override was unusable.
  if (scannedFor.value !== runCode.value) {
    scanReasons.value = [];
    scanLines.value = [];
    scanOverride.value = false;
  }

  if (runCode.value.trim()) {
    scanning.value = true;
    try {
      const res = await client.value.scanCode(runCode.value);
      scanReasons.value = res.reasons ?? [];
      // int32[] generates as (string | number)[]; the wire value is a number.
      scanLines.value = (res.lineNumbers ?? []).map((n) => Number(n) || 0);
    } catch (e) {
      // A scan that could not run is not a pass — report it rather than defaulting to clean.
      scanReasons.value = [e instanceof Error ? e.message : String(e)];
      scanLines.value = [];
    } finally {
      scannedFor.value = runCode.value;
      scanning.value = false;
    }
  }

  runLines.value = [];
  runMeta.value = null;

  // Denied and not overridden: stop here and show the report rather than starting a run
  // the agent will reject anyway.
  if (scanReasons.value.length > 0 && !scanOverride.value) {
    runState.value = "idle";
    toast.error(t("skills.scanBlocked"));
    return;
  }

  runState.value = "starting";

  try {
    const { runId } = await client.value.startCodeRun({
      code: runCode.value,
      language: "csharp",
      timeoutMs: 120_000,
      skillId: selectedId.value ?? undefined,
    });
    runState.value = "running";

    unsubscribe = client.value.streamCodeRun(runId, (type, payload) => {
      const data = payload as Record<string, unknown>;
      switch (type) {
        case "start":
          runLines.value.push(`▶ ${t("skills.runStarted")} (${String(data.executor ?? "?")})`);
          break;
        case "rejected":
          runLines.value.push(`✖ ${t("skills.runRejected")}`);
          break;
        case "result": {
          const stdout = String(data.stdout ?? "");
          const stderr = String(data.stderr ?? "");
          if (stdout) runLines.value.push(...stdout.replace(/\r/g, "").split("\n").filter(Boolean));
          if (stderr) runLines.value.push(...stderr.replace(/\r/g, "").split("\n").filter(Boolean));
          runMeta.value = { exitCode: data.exitCode as number, durationMs: data.durationMs as number };
          break;
        }
        case "exception":
          runLines.value.push(`✖ ${String(data.message ?? t("skills.runFailed"))}`);
          runState.value = "failed";
          break;
        case "error":
          runLines.value.push(`✖ ${String((data as { message?: string }).message ?? t("skills.streamFailed"))}`);
          runState.value = "failed";
          break;
        case "done": {
          const done = data as unknown as CodeRunStatus;
          runState.value = done.status === "completed" ? "done" : "failed";
          const r = done.result;
          if (r) {
            runLines.value.push(
              `— exit ${r.exitCode} · ${r.durationMs}ms · ${t(`skills.run_${r.status}`)}`,
            );
          }
          unsubscribe?.();
          unsubscribe = null;
          // Stage 7: a sandbox run can take two minutes; notify once it has settled.
          void notify(
            t("skills.run"),
            t("skills.notifyRunDone", {
              status: t(`skills.run_${done.status}`),
              code: r?.exitCode ?? 0,
            }),
          );
          break;
        }
        default:
          break;
      }
    });
  } catch (e) {
    runLines.value.push(`✖ ${e instanceof Error ? e.message : String(e)}`);
    runState.value = "failed";
  }
}

function cancelRun(): void {
  unsubscribe?.();
  unsubscribe = null;
  runLines.value.push(`— ${t("skills.streamDisconnected")}`);
  runState.value = "failed";
}

watch(
  () => connections.activeId,
  () => {
    detail.value = null;
    selectedId.value = null;
    void load();
  },
);

// Cross-view navigation: Chat can propose improving a specific skill.
watch(
  () => ui.focusedSkillId,
  async (id) => {
    if (!id || id === selectedId.value) return;
    await load();
    if (skills.value.some((s) => s.id === id)) await select(id);
  },
);

onMounted(load);
</script>

<template>
  <div class="flex flex-1 overflow-hidden bg-app">
    <!-- List -->
    <div class="flex w-64 shrink-0 flex-col border-r border-app">
      <div class="border-b border-app p-3">
        <input
          v-model="search"
          type="search"
          :placeholder="t('skills.search')"
          class="w-full rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
        />
      </div>

      <div class="flex-1 overflow-y-auto p-2">
        <p v-if="loading" class="p-3 text-sm text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="filtered.length === 0" class="p-3 text-sm text-secondary">
          {{ t("skills.none") }}
        </p>
        <button
          v-for="skill in filtered"
          :key="skill.id"
          class="mb-1 w-full rounded-lg px-3 py-2 text-left transition-colors"
          :class="selectedId === skill.id ? 'bg-tertiary text-app' : 'text-secondary hover:bg-tertiary'"
          @click="select(skill.id)"
        >
          <div class="truncate text-sm font-medium">{{ skill.name }}</div>
          <div class="truncate text-xs text-secondary">
            {{ skill.phraseReceivers.join(", ") || t("skills.noTriggers") }}
          </div>
        </button>
      </div>
    </div>

    <!-- Detail + run -->
    <div class="flex flex-1 flex-col overflow-y-auto p-4">
      <!-- Stage 3.4 — create from a template. Shown instead of "select one" when the
           operator has not opened a skill yet. -->
      <SkillTemplateGallery
        v-if="!detail && galleryOpen"
        @pick="createFromTemplate"
      />

      <div v-else-if="!detail" class="flex flex-1 flex-col items-center justify-center gap-3 text-secondary">
        <p class="text-sm">{{ t("skills.selectOne") }}</p>
        <button
          class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          :disabled="!client || creating"
          @click="galleryOpen = true"
        >
          {{ creating ? t("common.saving") : t("skills.newFromTemplate") }}
        </button>
      </div>

      <template v-else>
        <div class="mb-4">
          <h1 class="text-lg font-semibold text-app">{{ detail.meta.name }}</h1>
          <p class="text-sm text-secondary">{{ detail.descriptionMarkdown }}</p>
          <div class="mt-2 flex flex-wrap gap-2 text-xs text-secondary">
            <span class="rounded bg-tertiary px-2 py-0.5">v{{ detail.meta.version }}</span>
            <span class="rounded bg-tertiary px-2 py-0.5">
              {{ t("skills.uses") }}: {{ detail.meta.totalUses }}
            </span>
            <span class="rounded bg-tertiary px-2 py-0.5">
              {{ t("skills.success") }}: {{ Math.round(successRate(detail.meta) * 100) }}%
            </span>
          </div>
        </div>

        <!-- Prompt -->
        <div class="mb-4">
          <div class="mb-1 flex items-center justify-between">
            <label class="text-sm font-medium text-app">{{ t("skills.prompt") }}</label>
            <button
              v-if="dirty"
              class="rounded-lg bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
              :disabled="saving"
              @click="save"
            >
              {{ saving ? t("common.saving") : t("common.save") }}
            </button>
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="!detail || packaging"
              :title="t('skills.packageHint')"
              @click="downloadPackage"
            >
              {{ t("skills.downloadPackage") }}
            </button>
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="!detail || pushOpen"
              @click="pushOpen = true"
            >
              {{ t("skills.pushOpen") }}
            </button>
          </div>
          <!-- Monaco (Stage 2). Markdown, because skill prompts are markdown. -->
          <div
            class="h-72 overflow-hidden rounded-lg border border-app"
            :class="dirty ? 'border-emerald-500/50' : ''"
          >
            <CodeEditor
              v-model="promptDraft"
              language="markdown"
              :placeholder="t('skills.promptPlaceholder')"
            />
          </div>

          <!-- Cross-agent push (Stage 3) + MCP pre-check (Stage 5) -->
          <PushSkillDialog
            v-if="pushOpen"
            class="mt-2"
            :skill="detail"
            @close="pushOpen = false"
            @done="load()"
          />

          <!-- Prompt history (Stage 2b) -->
          <div class="mt-2">
            <button
              class="text-xs text-secondary hover:text-app"
              :disabled="historyLoading"
              @click="toggleHistory"
            >
              {{ historyOpen ? t("skills.hideHistory") : t("skills.showHistory") }}
              <span v-if="history.length > 0">({{ history.length }})</span>
            </button>

            <div v-if="historyOpen" class="mt-2 rounded-lg border border-app bg-secondary">
              <p v-if="historyLoading" class="px-3 py-2 text-xs text-secondary">
                {{ t("common.loading") }}
              </p>
              <p v-else-if="history.length === 0" class="px-3 py-2 text-xs text-secondary">
                {{ t("skills.noHistory") }}
              </p>
              <div v-else class="max-h-56 divide-y divide-app overflow-y-auto">
                <div
                  v-for="rev in [...history].reverse()"
                  :key="`${rev.version}-${rev.changedAt}`"
                  class="flex items-center justify-between gap-3 px-3 py-2"
                >
                  <div class="min-w-0">
                    <div class="flex items-center gap-2 text-xs text-app">
                      <span class="rounded bg-tertiary px-1.5 py-0.5">v{{ rev.version }}</span>
                      <span class="text-secondary">{{ rev.source }}</span>
                    </div>
                    <p class="truncate text-[11px] text-secondary">{{ rev.changedAt }}</p>
                  </div>
                  <button
                    class="shrink-0 rounded border border-app px-2 py-1 text-[11px] text-app hover:bg-tertiary disabled:opacity-50"
                    :disabled="restoring"
                    :aria-label="t('skills.diffFor', { version: rev.version })"
                    @click="toggleDiff(rev)"
                  >
                    {{ diffing === rev ? t("skills.hideDiff") : t("skills.diff") }}
                  </button>
                  <button
                    class="shrink-0 rounded border border-app px-2 py-1 text-[11px] text-app hover:bg-tertiary disabled:opacity-50"
                    :disabled="restoring"
                    :aria-label="t('skills.restoreFor', { version: rev.version })"
                    @click="restore(rev)"
                  >
                    {{ t("skills.restore") }}
                  </button>
                </div>
              </div>

              <!-- Diff of the selected revision against the prompt in the editor. -->
              <div
                v-if="diffing"
                class="border-t border-app bg-app px-3 py-2"
                data-testid="prompt-diff"
              >
                <div class="mb-1.5 flex items-center justify-between text-[11px]">
                  <span class="text-secondary">
                    {{ t("skills.diffTitle", { version: diffing.version }) }}
                  </span>
                  <span v-if="!diffTooLarge && !diffIsEmpty" class="flex items-center gap-2">
                    <span class="text-emerald-400">+{{ diffCounts.added }}</span>
                    <span class="text-red-400">−{{ diffCounts.removed }}</span>
                  </span>
                </div>

                <p v-if="diffTooLarge" class="text-[11px] text-amber-400">
                  {{ t("skills.diffTooLarge") }}
                </p>
                <p v-else-if="diffIsEmpty" class="text-[11px] text-secondary">
                  {{ t("skills.diffIdentical") }}
                </p>

                <div v-else class="max-h-64 overflow-y-auto font-mono text-[11px] leading-relaxed">
                  <div
                    v-for="(line, i) in diffLines"
                    :key="i"
                    class="flex gap-2 whitespace-pre-wrap px-1"
                    :class="{
                      'bg-emerald-500/10 text-emerald-300': line.kind === 'added',
                      'bg-red-500/10 text-red-300': line.kind === 'removed',
                      'text-secondary': line.kind === 'context',
                    }"
                  >
                    <span class="w-8 shrink-0 select-none text-right opacity-50">
                      {{ line.leftLine ?? "" }}
                    </span>
                    <span class="w-8 shrink-0 select-none text-right opacity-50">
                      {{ line.rightLine ?? "" }}
                    </span>
                    <span class="w-3 shrink-0 select-none">
                      {{ line.kind === "added" ? "+" : line.kind === "removed" ? "−" : " " }}
                    </span>
                    <span class="min-w-0 break-words">{{ line.text }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Sandbox run -->
        <div class="rounded-xl border border-app bg-secondary p-4">
          <div class="mb-2 flex items-center justify-between">
            <h2 class="text-sm font-medium text-app">{{ t("skills.sandbox") }}</h2>
            <div class="flex gap-2">
              <button
                v-if="runState === 'running' || runState === 'starting'"
                class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary"
                @click="cancelRun"
              >
                {{ t("common.cancel") }}
              </button>
              <button
                v-else
                class="rounded-lg bg-emerald-600 px-3 py-1 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
                :disabled="!client || runCode.trim().length === 0 || scanning"
                @click="run"
              >
                {{ scanning ? t("skills.scanning") : `▶ ${t("skills.run")}` }}
              </button>
            </div>
          </div>

          <!-- Stage 3.5 — scanner verdict before anything is executed. -->
          <div
            v-if="scanReasons.length > 0"
            class="mb-2 rounded-lg border border-red-500/50 bg-red-500/5 p-2"
            data-testid="scan-report"
          >
            <p class="mb-1 text-[11px] text-red-400">{{ t("skills.scanBlocked") }}</p>
            <ul class="space-y-0.5 text-[11px] text-secondary">
              <li v-for="(reason, i) in scanReasons" :key="i">
                <span v-if="scanLines[i]" class="font-mono text-amber-400">
                  {{ t("skills.scanLine", { n: scanLines[i] }) }}
                </span>
                {{ reason }}
              </li>
            </ul>
            <label class="mt-1.5 flex items-center gap-1.5 text-[11px] text-secondary">
              <input v-model="scanOverride" type="checkbox" />
              {{ t("skills.scanOverride") }}
            </label>
          </div>

          <p class="mb-2 text-xs text-secondary">{{ t("skills.sandboxHint") }}</p>

          <textarea
            v-model="runCode"
            rows="5"
            spellcheck="false"
            :aria-label="t('skills.runCode')"
            class="mb-2 w-full rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
          />

          <pre
            class="max-h-64 overflow-auto rounded-lg bg-tertiary p-3 font-mono text-xs text-app"
            :class="{ 'opacity-60': runState === 'running' }"
          >{{ runLines.length ? runLines.join("\n") : t("skills.noOutput") }}</pre>

          <p v-if="runMeta" class="mt-1 text-xs text-secondary">
            exit {{ runMeta.exitCode }} · {{ runMeta.durationMs }}ms
          </p>
        </div>
      </template>
    </div>
  </div>
</template>