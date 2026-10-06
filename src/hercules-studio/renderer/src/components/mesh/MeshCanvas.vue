<script setup lang="ts">
/**
 * Stage 4.2 — mesh topology canvas.
 *
 * Renders the registry's agents as nodes: colour by health, radius by capability count,
 * label by displayName / agentId / endpoint. Nodes are laid out on a circle rather than
 * with a physics simulation — the mesh has no edge list to attract, and a deterministic
 * ring reads more clearly than a force layout that reshuffles on every refresh.
 *
 * Data comes from `GET /api/mesh/dashboard` (`MeshDashboardDto.topology`), typed from the
 * OpenAPI document — see the note in `sdk/types.ts`. The registry's trust/delegation
 * relations are not exposed as edges, so no edges are drawn; that is a backend gap, not an
 * omission here.
 */
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { VueFlow, useVueFlow, type Node } from "@vue-flow/core";
import "@vue-flow/core/dist/style.css";
import "@vue-flow/core/dist/theme-default.css";
import type { MeshAgentDto } from "../../sdk/types";

const props = defineProps<{ agents: MeshAgentDto[]; selectedId?: string | null }>();

const emit = defineEmits<{
  select: [agentId: string];
  touch: [agentId: string];
  remove: [agentId: string];
  circuit: [agentId: string];
  register: [];
  cleanup: [];
}>();

const { t } = useI18n();
const { fitView } = useVueFlow();

/** Health colour bands, matching the legend and the health table's semantics. */
const healthColor = (score: number) => {
  if (score >= 0.7) return "#10b981"; // emerald
  if (score >= 0.4) return "#f59e0b"; // amber
  return "#ef4444"; // red
}

const healthBand = (score: number) => {
  if (score >= 0.7) return "healthy";
  if (score >= 0.4) return "degraded";
  return "unhealthy";
}

/**
 * Ring layout, radius scaled by capability count so a node's size reads as breadth of
 * what the agent can do. Clamped so one 40-capability agent cannot dwarf the rest.
 */
/**
 * `double` fields are emitted as `number | string` by the generator, so every read of a
 * score or latency is normalised here rather than being coerced at each call site.
 */
const score = (agent: MeshAgentDto): number => Number(agent.healthScore) || 0;

const layout = computed<Node[]>(() => {
  const agents = props.agents;
  const count = agents.length;

  return agents.map((agent, index) => {
    const angle = count === 1 ? 0 : (index / count) * Math.PI * 2;
    const radius = count === 1 ? 0 : 150;

    const capabilities = agent.capabilities?.length ?? 0;
    const size = Math.min(34, 14 + capabilities * 1.6);
    const health = score(agent);

    return {
      id: agent.agentId,
      position: {
        x: radius * Math.cos(angle) - size / 2,
        y: radius * Math.sin(angle) - size / 2,
      },
      // Sized by capability breadth; colour by health.
      data: {
        label: agent.displayName || agent.agentId,
        agentId: agent.agentId,
        endpoint: agent.endpoint,
        capabilities,
        size,
        band: healthBand(health),
      },
      type: "default",
      style: {
        width: `${size}px`,
        height: `${size}px`,
        background: healthColor(health),
        border: props.selectedId === agent.agentId ? "2px solid #e5e7eb" : "1px solid rgba(255,255,255,.25)",
        color: "#fff",
        fontSize: "10px",
        borderRadius: "9999px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        overflow: "visible",
      },
    } satisfies Node;
  });
});

const counts = computed(() => {
  const out = { healthy: 0, degraded: 0, unhealthy: 0 };
  for (const a of props.agents) out[healthBand(score(a))] += 1;
  return out;
});

/** Typed here rather than inline: Vue template expressions cannot carry annotations. */
function onNodeClick(event: { node: { id: string } }): void {
  emit("select", event.node.id);
}

// ---- Context menu (Stage 4.4) ----
// Right-clicking a node opens a small menu anchored to the viewport. It closes on Escape
// and on any outside click, so a stray menu can never stay latched over the canvas.
const menuFor = ref<string | null>(null);
const menuAt = ref({ x: 0, y: 0 });
const selfId = ref<string | null>(null);

function onNodeContextMenu(event: MouseEvent | TouchEvent, nodeId: string): void {
  // Touch devices synthesise a contextmenu event too; guard the mouse-only coordinates.
  event.preventDefault();
  menuFor.value = nodeId;
  const mouse = event as MouseEvent;
  menuAt.value = {
    x: typeof mouse.clientX === "number" ? mouse.clientX : 0,
    y: typeof mouse.clientY === "number" ? mouse.clientY : 0,
  };
}

/** Vue Flow hands `node-context-menu` a single event object, not (event, node). */
function onContextMenu(payload: { event: MouseEvent | TouchEvent; node: { id: string } }): void {
  onNodeContextMenu(payload.event, payload.node.id);
}

function closeMenu(): void {
  menuFor.value = null;
}

function act(kind: "details" | "touch" | "remove" | "circuit" | "register" | "cleanup"): void {
  const agentId = menuFor.value;
  closeMenu();
  // Registry-wide actions have no target node, so they are emitted without an id.
  if (kind === "cleanup") {
    emit("cleanup");
    return;
  }
  if (kind === "register") {
    emit("register");
    return;
  }
  if (!agentId) return;
  // Dispatched per-kind rather than `emit(kind, id)`: a union of event names does not
  // narrow to the specific overload.
  switch (kind) {
    case "details":
      emit("select", agentId);
      break;
    case "touch":
      emit("touch", agentId);
      break;
    case "remove":
      emit("remove", agentId);
      break;
    case "circuit":
      emit("circuit", agentId);
      break;
  }
}

function onKeydown(e: KeyboardEvent): void {
  if (e.key === "Escape") closeMenu();
}

onMounted(() => {
  window.addEventListener("keydown", onKeydown);
  window.addEventListener("click", closeMenu);
});

onBeforeUnmount(() => {
  window.removeEventListener("keydown", onKeydown);
  window.removeEventListener("click", closeMenu);
});

// Re-fit when the population changes, otherwise a peer that joins stays off-screen.
watch(
  () => props.agents.length,
  async () => {
    await nextTick();
    fitView({ padding: 0.25, duration: 300 });
  },
);
</script>

<template>
  <div class="rounded-xl border border-app bg-secondary p-3">
    <div class="mb-2 flex items-center justify-between">
      <h3 class="text-sm font-medium text-app">{{ t("mesh.canvasTitle") }}</h3>
      <div class="flex items-center gap-3 text-[11px] text-secondary">
        <span class="flex items-center gap-1">
          <span class="h-2 w-2 rounded-full bg-emerald-500" /> {{ t("mesh.bandHealthy") }}
          ({{ counts.healthy }})
        </span>
        <span class="flex items-center gap-1">
          <span class="h-2 w-2 rounded-full bg-amber-500" /> {{ t("mesh.bandDegraded") }}
          ({{ counts.degraded }})
        </span>
        <span class="flex items-center gap-1">
          <span class="h-2 w-2 rounded-full bg-red-500" /> {{ t("mesh.bandUnhealthy") }}
          ({{ counts.unhealthy }})
        </span>
        <span>{{ t("mesh.sizeLegend") }}</span>
      </div>
    </div>

    <p v-if="agents.length === 0" class="py-8 text-center text-xs text-secondary">
      {{ t("mesh.canvasEmpty") }}
    </p>

    <VueFlow
      v-else
      :nodes="layout"
      :edges="[]"
      :nodes-draggable="false"
      :nodes-connectable="false"
      :zoom-on-scroll="true"
      :pan-on-drag="true"
      :min-zoom="0.3"
      :max-zoom="2.5"
      class="h-80 w-full"
      @node-click="onNodeClick"
      @node-context-menu="onContextMenu"
    >
      <template #node-default="{ data }">
        <div class="text-center leading-tight">
          <div class="max-w-24 truncate font-semibold">{{ data.label }}</div>
          <div class="max-w-24 truncate text-[9px] opacity-80">{{ data.agentId }}</div>
        </div>
      </template>
    </VueFlow>

    <p class="mt-1.5 text-[10px] text-secondary">{{ t("mesh.canvasEdgesNote") }}</p>

    <!-- Stage 4.4 context menu. Anchored to the viewport and dismissable. -->
    <div
      v-if="menuFor"
      class="fixed z-50 w-44 rounded-lg border border-app bg-app py-1 shadow-lg"
      :style="{ left: `${menuAt.x}px`, top: `${menuAt.y}px` }"
      role="menu"
    >
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-app hover:bg-tertiary"
        role="menuitem"
        @click="act('details')"
      >
        {{ t("mesh.menuDetails") }}
      </button>
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-app hover:bg-tertiary"
        role="menuitem"
        @click="act('touch')"
      >
        {{ t("mesh.menuTouch") }}
      </button>
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-app hover:bg-tertiary"
        role="menuitem"
        @click="act('circuit')"
      >
        {{ t("mesh.menuCircuit") }}
      </button>
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-app hover:bg-tertiary"
        role="menuitem"
        @click="act('register')"
      >
        {{ t("mesh.menuRegister") }}
      </button>
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-app hover:bg-tertiary"
        role="menuitem"
        @click="act('cleanup')"
      >
        {{ t("mesh.menuCleanup") }}
      </button>
      <button
        type="button"
        class="block w-full px-3 py-1.5 text-left text-xs text-red-400 hover:bg-red-500/10"
        role="menuitem"
        @click="act('remove')"
      >
        {{ t("mesh.menuRemove") }}
      </button>
    </div>
  </div>
</template>