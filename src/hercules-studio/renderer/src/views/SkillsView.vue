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
import type { SkillDto, SkillDetailDto } from "../sdk/types";
import type { CodeRunStatus } from "../sdk/client";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();
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
      s.triggers.some((t) => t.toLowerCase().includes(q)),
  );
});

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    skills.value = await client.value.listSkills();
    if (!selectedId.value && skills.value.length > 0) {
      await select(skills.value[0].id);
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

async function run(): Promise<void> {
  if (!client.value || runState.value === "starting" || runState.value === "running") return;

  runLines.value = [];
  runMeta.value = null;
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
          <div class="truncate text-xs opacity-70">
            {{ skill.triggers.join(", ") || t("skills.noTriggers") }}
          </div>
        </button>
      </div>
    </div>

    <!-- Detail + run -->
    <div class="flex flex-1 flex-col overflow-y-auto p-4">
      <div v-if="!detail" class="flex flex-1 items-center justify-center text-secondary">
        <p class="text-sm">{{ t("skills.selectOne") }}</p>
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
              {{ t("skills.success") }}: {{ Math.round(detail.meta.successRate * 100) }}%
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
          </div>
          <textarea
            v-model="promptDraft"
            rows="6"
            spellcheck="false"
            class="w-full rounded-lg border border-app bg-secondary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
          />
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
                :disabled="!client || runCode.trim().length === 0"
                @click="run"
              >
                ▶ {{ t("skills.run") }}
              </button>
            </div>
          </div>

          <p class="mb-2 text-xs text-secondary">{{ t("skills.sandboxHint") }}</p>

          <textarea
            v-model="runCode"
            rows="5"
            spellcheck="false"
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