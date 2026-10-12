<script setup lang="ts">
/**
 * Workflow view — multi-agent workflow definitions from the workflow server.
 *
 * The workflow server is a separate service with its own credentials, so this
 * view does not depend on an agent connection. Credentials are held in memory
 * only (see `platform.workflows`) and must be re-entered after a reload.
 *
 * `run` and `executions` are backend stubs (task_105), so the UI says so rather
 * than presenting a Run button that cannot do anything yet.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useToastStore } from "../stores/toast";
import { platform } from "../platform";
import GraphEditor from "../components/workflow/GraphEditor.vue";
import { parseGraph, type WorkflowGraph } from "../workflow/graph";
import type { WorkflowDetailDto, WorkflowSummaryDto } from "../platform/capabilities";

const { t } = useI18n();
const toast = useToastStore();

const DEFAULT_BASE_URL = "http://localhost:8430";

const baseUrl = ref(DEFAULT_BASE_URL);
const clientId = ref("");
const clientSecret = ref("");
const configured = ref(false);

const workflows = ref<WorkflowSummaryDto[]>([]);
const detail = ref<WorkflowDetailDto | null>(null);
const loading = ref(false);
const selectedId = ref<string | null>(null);
const hasMore = ref(false);

// Stage 8 authoring. `editingId` is null while creating a new definition; a
// non-null id updates that definition in place (POST with an explicit id).
const editing = ref(false);
const editingId = ref<string | null>(null);
const editingName = ref("");
const editingDescription = ref("");
const editingVersion = ref(1);
const editingGraph = ref<WorkflowGraph>({ nodes: [], edges: [] });
const saving = ref(false);

const isConfigured = computed(() => configured.value);

function connect(): void {
  if (!clientId.value.trim() || !clientSecret.value.trim()) {
    toast.warn(t("workflow.credentialsRequired"));
    return;
  }
  platform.workflows.configure({
    baseUrl: baseUrl.value,
    clientId: clientId.value.trim(),
    clientSecret: clientSecret.value.trim(),
  });
  configured.value = true;
  // Do not keep the secret in component state once handed to the platform.
  clientSecret.value = "";
  toast.success(t("workflow.connected"));
  void load();
}

async function load(): Promise<void> {
  if (!configured.value) return;
  loading.value = true;
  try {
    const res = await platform.workflows.list({ limit: 50 });
    workflows.value = Array.isArray(res.items) ? res.items : [];
    hasMore.value = Boolean(res.hasMore);
  } catch (e) {
    configured.value = false;
    toast.error(`${t("workflow.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

async function select(id: string): Promise<void> {
  selectedId.value = id;
  detail.value = null;
  try {
    detail.value = await platform.workflows.get(id);
  } catch (e) {
    toast.error(`${t("workflow.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function remove(id: string, name: string): Promise<void> {
  try {
    await platform.workflows.remove(id);
    toast.success(t("workflow.deleted", { name }));
    if (selectedId.value === id) {
      selectedId.value = null;
      detail.value = null;
    }
    await load();
  } catch (e) {
    toast.error(`${t("workflow.deleteFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function run(id: string): Promise<void> {
  try {
    await platform.workflows.run(id);
    toast.info(t("workflow.runStub"));
  } catch (e) {
    toast.error(`${t("workflow.runFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

function prettyGraph(value: unknown): string {
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

function startCreate(): void {
  editingId.value = null;
  editingName.value = "";
  editingDescription.value = "";
  editingVersion.value = 1;
  editingGraph.value = { nodes: [], edges: [] };
  editing.value = true;
}

/** Loads an existing definition into the editor so saving updates it in place. */
function startEdit(): Promise<void> {
  if (!detail.value) return Promise.resolve();
  editingId.value = detail.value.id;
  editingName.value = detail.value.name;
  editingDescription.value = detail.value.description ?? "";
  editingVersion.value = detail.value.version ?? 1;
  editingGraph.value = parseGraph(detail.value.graphJson);
  editing.value = true;
  return Promise.resolve();
}

function cancelEdit(): void {
  editing.value = false;
  editingId.value = null;
}

async function saveWorkflow(payload: {
  id: string | null;
  name: string;
  description: string;
  version: number;
  graph: WorkflowGraph;
}): Promise<void> {
  if (saving.value) return;
  saving.value = true;
  try {
    const saved = await platform.workflows.save({
      id: payload.id ?? undefined,
      name: payload.name,
      description: payload.description,
      version: payload.version,
      graph: payload.graph,
    });

    editing.value = false;
    editingId.value = null;
    toast.success(payload.id ? t("workflowEditor.updated") : t("workflowEditor.created"));
    await load();
    // Re-read the definition rather than trusting the local draft.
    await select(saved.id ?? payload.id ?? "");
  } catch (e) {
    toast.error(`${t("workflowEditor.saveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    saving.value = false;
  }
}

onMounted(() => {
  configured.value = platform.workflows.configured();
});
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-5xl">
      <h1 class="mb-1 text-lg font-semibold text-app">{{ t("workflow.title") }}</h1>
      <p class="mb-4 text-sm text-secondary">{{ t("workflow.subtitle") }}</p>

      <!-- Connection form: workflow server is a separate service -->
      <div v-if="!isConfigured" class="mb-5 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("workflow.connect") }}</h2>
        <p class="mb-3 text-xs text-secondary">{{ t("workflow.credentialsHint") }}</p>

        <div class="grid gap-3 sm:grid-cols-2">
          <label class="flex flex-col gap-1 text-xs text-secondary">
            {{ t("workflow.baseUrl") }}
            <input
              v-model="baseUrl"
              type="text"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            />
          </label>
          <label class="flex flex-col gap-1 text-xs text-secondary">
            {{ t("workflow.clientId") }}
            <input
              v-model="clientId"
              type="text"
              autocomplete="off"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            />
          </label>
        </div>
        <label class="mt-3 flex flex-col gap-1 text-xs text-secondary">
          {{ t("workflow.clientSecret") }}
          <input
            v-model="clientSecret"
            type="password"
            autocomplete="off"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
          />
          <span class="text-[11px]">{{ t("workflow.secretHint") }}</span>
        </label>

        <button
          class="mt-4 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500"
          @click="connect"
        >
          {{ t("workflow.connect") }}
        </button>
      </div>

      <!-- Definitions -->
      <template v-else>
        <!-- Stage 8 authoring. Editing an existing definition updates it in place;
             creating omits the id so the server mints one. -->
        <GraphEditor
          v-if="editing"
          class="mb-4"
          :workflow-id="editingId"
          :initial-name="editingName"
          :initial-description="editingDescription"
          :initial-version="editingVersion"
          :initial-graph="editingGraph"
          @save="saveWorkflow"
          @cancel="cancelEdit"
        />

        <div class="mb-3 flex items-center justify-between">
          <span class="text-xs text-secondary">
            {{ t("workflow.count") }}: {{ workflows.length }}
            <span v-if="hasMore" class="ml-1 text-amber-400">({{ t("workflow.moreAvailable") }})</span>
          </span>
          <div class="flex gap-2">
            <button
              class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50"
              :disabled="editing"
              @click="startCreate"
            >
              {{ t("workflowEditor.newWorkflow") }}
            </button>
            <button
              v-if="detail"
              class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="editing"
              @click="startEdit"
            >
              {{ t("common.edit") }}
            </button>
            <button
              class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="loading"
              @click="load"
            >
              {{ t("common.refresh") }}
            </button>
          </div>
        </div>

        <p v-if="loading" class="text-sm text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="workflows.length === 0" class="text-sm text-secondary">{{ t("workflow.none") }}</p>

        <div v-else class="space-y-2">
          <div
            v-for="w in workflows"
            :key="w.id"
            class="rounded-xl border border-app bg-secondary p-3"
            :class="selectedId === w.id ? 'border-emerald-500/50' : ''"
          >
            <div class="flex items-start justify-between gap-3">
              <button class="min-w-0 flex-1 text-left" @click="select(w.id)">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-medium text-app">{{ w.name }}</span>
                  <span class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary">
                    v{{ w.version }}
                  </span>
                </div>
                <p v-if="w.description" class="mt-0.5 text-xs text-secondary">{{ w.description }}</p>
                <p class="mt-0.5 text-[11px] text-secondary">{{ w.createdAt }}</p>
              </button>

              <div class="flex shrink-0 gap-2">
                <button
                  class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary"
                  :title="t('workflow.runStub')"
                  @click="run(w.id)"
                >
                  ▶ {{ t("workflow.run") }}
                </button>
                <button
                  class="rounded-lg border border-app px-3 py-1 text-xs text-red-400 hover:bg-red-500/10"
                  @click="remove(w.id, w.name)"
                >
                  {{ t("common.delete") }}
                </button>
              </div>
            </div>

            <div v-if="selectedId === w.id" class="mt-3">
              <p v-if="loading && !detail" class="text-xs text-secondary">{{ t("common.loading") }}</p>
              <pre
                v-else-if="detail"
                class="max-h-72 overflow-auto rounded-lg bg-tertiary p-3 font-mono text-[11px] text-app"
                >{{ prettyGraph(detail.graphJson) }}</pre
              >
            </div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>