<script setup lang="ts">
/**
 * Stage 8 — workflow graph editor.
 *
 * Authoring surface for the `graphJson` the workflow-server stores. It is a structured
 * node/edge editor rather than the BPMN canvas Stage 4 asks for: the canvas depends on
 * a typed `WorkflowGraph` that task_105 still owns, whereas this operates on the same
 * plain data the server already accepts, so nothing here has to be thrown away when the
 * canvas lands — it is a renderer over the same model.
 *
 * Validation is structural only (see `validateGraph`). Execution is not implemented
 * server-side (task_105), and the Run action stays a labelled 501 rather than pretending.
 */
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  NODE_TYPES,
  emptyGraph,
  newNode,
  validateGraph,
  type GraphIssue,
  type WorkflowEdge,
  type WorkflowGraph,
  type WorkflowNode,
  type WorkflowNodeType,
} from "../../workflow/graph";

const props = defineProps<{
  /** Existing definition id when editing; null creates a new one. */
  workflowId: string | null;
  initialName: string;
  initialDescription: string;
  initialVersion: number;
  initialGraph: WorkflowGraph;
}>();

const emit = defineEmits<{
  (e: "save", payload: { id: string | null; name: string; description: string; version: number; graph: WorkflowGraph }): void;
  (e: "cancel"): void;
}>();

const { t } = useI18n();

const name = ref(props.initialName);
const description = ref(props.initialDescription);
const version = ref(props.initialVersion);
const graph = ref<WorkflowGraph>(
  props.initialGraph.nodes.length > 0
    ? { nodes: [...props.initialGraph.nodes], edges: [...props.initialGraph.edges] }
    : emptyGraph(),
);

const seq = ref(graph.value.nodes.length + 1);
const showIssues = ref(false);

const issues = computed<GraphIssue[]>(() => validateGraph(graph.value));
const canSave = computed(() => name.value.trim().length > 0 && issues.value.length === 0);
const nodeTypes = NODE_TYPES;

const nodeOptions = computed(() => graph.value.nodes.map((n) => ({ id: n.id, label: n.label })));

function addNode(type: WorkflowNodeType): void {
  graph.value.nodes.push(newNode(type, seq.value));
  seq.value += 1;
}

function removeNode(id: string): void {
  graph.value.nodes = graph.value.nodes.filter((n) => n.id !== id);
  // Drop edges that referenced it — leaving them would fail validation for a reason
  // the operator did not cause.
  graph.value.edges = graph.value.edges.filter((e) => e.from !== id && e.to !== id);
}

function addEdge(): void {
  const first = graph.value.nodes[0]?.id ?? "";
  const second = graph.value.nodes[1]?.id ?? first;
  graph.value.edges.push({ from: first, to: second, label: "" });
}

function removeEdge(index: number): void {
  graph.value.edges.splice(index, 1);
}

function edgeTargets(index: number): { from: string; to: string } {
  const edge = graph.value.edges[index];
  if (!edge) return { from: "", to: "" };
  return { from: edge.from, to: edge.to };
}

function setEdge(index: number, key: keyof WorkflowEdge, value: string): void {
  const edge = graph.value.edges[index];
  if (edge) edge[key] = value;
}

function isService(node: WorkflowNode): node is Extract<WorkflowNode, { type: "ServiceTaskNode" }> {
  return node.type === "ServiceTaskNode";
}

function isConditional(
  node: WorkflowNode,
): node is Extract<WorkflowNode, { type: "ConditionalNode" }> {
  return node.type === "ConditionalNode";
}

function isUserTask(node: WorkflowNode): node is Extract<WorkflowNode, { type: "UserTaskNode" }> {
  return node.type === "UserTaskNode";
}

function submit(): void {
  showIssues.value = true;
  if (!canSave.value) return;

  emit("save", {
    id: props.workflowId,
    name: name.value.trim(),
    description: description.value.trim(),
    version: Number(version.value) || 1,
    graph: {
      nodes: graph.value.nodes,
      edges: graph.value.edges,
    },
  });
}
</script>

<template>
  <form
    class="space-y-4 rounded-xl border border-emerald-600/40 bg-secondary p-4"
    @submit.prevent="submit"
  >
    <div class="grid gap-2 sm:grid-cols-3">
      <label class="flex flex-col gap-1">
        <span class="text-[11px] text-secondary">{{ t("workflowEditor.name") }}</span>
        <input
          v-model="name"
          type="text"
          :placeholder="t('workflowEditor.namePlaceholder')"
          class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
        />
      </label>
      <label class="flex flex-col gap-1">
        <span class="text-[11px] text-secondary">{{ t("workflowEditor.version") }}</span>
        <input
          v-model.number="version"
          type="number"
          min="1"
          class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
        />
      </label>
      <label class="flex flex-col gap-1">
        <span class="text-[11px] text-secondary">{{ t("workflowEditor.description") }}</span>
        <input
          v-model="description"
          type="text"
          class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
        />
      </label>
    </div>

    <!-- Nodes -->
    <div>
      <div class="mb-1.5 flex items-center justify-between">
        <span class="text-[11px] text-secondary">{{ t("workflowEditor.nodes") }}</span>
        <div class="flex flex-wrap gap-1">
          <button
            v-for="type in nodeTypes"
            :key="type"
            type="button"
            class="rounded border border-app px-2 py-0.5 text-[11px] text-app transition-colors hover:bg-tertiary"
            @click="addNode(type)"
          >
            + {{ type }}
          </button>
        </div>
      </div>

      <div class="space-y-1.5">
        <div
          v-for="node in graph.nodes"
          :key="node.id"
          class="rounded-lg border border-app bg-app px-3 py-2"
        >
          <div class="flex items-center gap-2">
            <input
              v-model="node.id"
              type="text"
              class="w-40 rounded border border-app bg-tertiary px-2 py-1 font-mono text-[11px] text-app outline-none focus:border-emerald-500"
              :aria-label="t('workflowEditor.nodeId')"
            />
            <input
              v-model="node.label"
              type="text"
              class="flex-1 rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
              :aria-label="t('workflowEditor.nodeLabel')"
            />
            <span class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary">
              {{ node.type }}
            </span>
            <button
              type="button"
              class="rounded border border-red-500/40 px-2 py-0.5 text-[11px] text-red-400 hover:bg-red-500/10"
              @click="removeNode(node.id)"
            >
              {{ t("common.delete") }}
            </button>
          </div>

          <div v-if="isService(node)" class="mt-1.5 grid gap-1.5 sm:grid-cols-2">
            <input
              v-model="node.agentId"
              type="text"
              :placeholder="t('workflowEditor.agentId')"
              class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            />
            <input
              v-model="node.intent"
              type="text"
              :placeholder="t('workflowEditor.intent')"
              class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            />
            <input
              v-model="node.payload"
              type="text"
              placeholder='{"env":"prod"}'
              class="rounded border border-app bg-tertiary px-2 py-1 font-mono text-[11px] text-app outline-none focus:border-emerald-500"
            />
            <input
              v-model.number="node.timeoutMs"
              type="number"
              min="0"
              class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            />
          </div>

          <div v-if="isUserTask(node)" class="mt-1.5">
            <input
              v-model="node.question"
              type="text"
              :placeholder="t('workflowEditor.question')"
              class="w-full rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            />
          </div>

          <div v-if="isConditional(node)" class="mt-1.5 grid gap-1.5 sm:grid-cols-3">
            <input
              v-model="node.expression"
              type="text"
              :placeholder="t('workflowEditor.expression')"
              class="rounded border border-app bg-tertiary px-2 py-1 font-mono text-[11px] text-app outline-none focus:border-emerald-500"
            />
            <select
              v-model="node.trueNext"
              class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
              :aria-label="t('workflowEditor.trueBranch')"
            >
              <option value="">{{ t("workflowEditor.noBranch") }}</option>
              <option v-for="opt in nodeOptions" :key="opt.id" :value="opt.id">
                {{ opt.label }}
              </option>
            </select>
            <select
              v-model="node.falseNext"
              class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
              :aria-label="t('workflowEditor.falseBranch')"
            >
              <option value="">{{ t("workflowEditor.noBranch") }}</option>
              <option v-for="opt in nodeOptions" :key="opt.id" :value="opt.id">
                {{ opt.label }}
              </option>
            </select>
          </div>
        </div>
      </div>
    </div>

    <!-- Edges -->
    <div>
      <div class="mb-1.5 flex items-center justify-between">
        <span class="text-[11px] text-secondary">{{ t("workflowEditor.edges") }}</span>
        <button
          type="button"
          class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary"
          @click="addEdge"
        >
          + {{ t("workflowEditor.edge") }}
        </button>
      </div>

      <p v-if="graph.edges.length === 0" class="text-[11px] text-secondary">
        {{ t("workflowEditor.noEdges") }}
      </p>

      <div v-else class="space-y-1">
        <div
          v-for="(edge, index) in graph.edges"
          :key="index"
          class="flex items-center gap-2"
        >
          <select
            :value="edgeTargets(index).from"
            class="flex-1 rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            :aria-label="t('workflowEditor.edgeFrom', { n: index + 1 })"
            @change="setEdge(index, 'from', ($event.target as HTMLSelectElement).value)"
          >
            <option v-for="opt in nodeOptions" :key="opt.id" :value="opt.id">
              {{ opt.label }}
            </option>
          </select>
          <span class="text-[11px] text-secondary">→</span>
          <select
            :value="edgeTargets(index).to"
            class="flex-1 rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            :aria-label="t('workflowEditor.edgeTo', { n: index + 1 })"
            @change="setEdge(index, 'to', ($event.target as HTMLSelectElement).value)"
          >
            <option v-for="opt in nodeOptions" :key="opt.id" :value="opt.id">
              {{ opt.label }}
            </option>
          </select>
          <input
            :value="graph.edges[index]?.label ?? ''"
            type="text"
            :placeholder="t('workflowEditor.edgeLabel')"
            class="w-24 rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
            @input="setEdge(index, 'label', ($event.target as HTMLInputElement).value)"
          />
          <button
            type="button"
            class="rounded border border-red-500/40 px-2 py-0.5 text-[11px] text-red-400 hover:bg-red-500/10"
            @click="removeEdge(index)"
          >
            {{ t("common.delete") }}
          </button>
        </div>
      </div>
    </div>

    <!-- Validation -->
    <div v-if="issues.length > 0" class="rounded-lg border border-amber-500/40 bg-amber-500/5 p-2">
      <p class="mb-1 text-[11px] text-amber-400">
        {{ t("workflowEditor.issues", { n: issues.length }) }}
      </p>
      <ul class="space-y-0.5 text-[11px] text-secondary">
        <li v-for="(issue, i) in showIssues ? issues : issues.slice(0, 3)" :key="i">
          {{ issue.message }}
        </li>
      </ul>
    </div>

    <div class="flex items-center gap-2">
      <button
        type="submit"
        class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        :disabled="!canSave"
      >
        {{ t("workflowEditor.save") }}
      </button>
      <button
        type="button"
        class="rounded-lg border border-app px-3 py-1.5 text-xs text-app hover:bg-tertiary"
        @click="emit('cancel')"
      >
        {{ t("common.cancel") }}
      </button>
      <span class="text-[11px] text-secondary">{{ t("workflowEditor.executionNote") }}</span>
    </div>
  </form>
</template>