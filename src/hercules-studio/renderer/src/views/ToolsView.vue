<script setup lang="ts">
/**
 * Tools view — the agent's tool registry with enable/disable and health.
 *
 * Toggling a tool is a real agent-side operation, so each switch is optimistic
 * but reverts if the agent rejects it — a silent failure here would leave the
 * UI claiming a tool is enabled when it is not.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { ToolDto, McpServerSummaryDto, McpServerDefinitionDto } from "../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const tools = ref<ToolDto[]>([]);
const loading = ref(false);
const search = ref("");
const category = ref<string | null>(null);
const pending = ref<Set<string>>(new Set());

const client = computed(() => connections.client);

const categories = computed(() => {
  const set = new Set<string>();
  for (const tool of tools.value) set.add(tool.category);
  return [...set].sort();
});

const filtered = computed(() => {
  const q = search.value.trim().toLowerCase();
  return tools.value.filter((tool) => {
    if (category.value && tool.category !== category.value) return false;
    if (!q) return true;
    return `${tool.name} ${tool.description} ${tool.category}`.toLowerCase().includes(q);
  });
});

const enabledCount = computed(() => tools.value.filter((x) => x.enabled).length);

// ---- MCP servers (Stage 5 / 5b) ----
// MCP servers are tool-adjacent, so they live here rather than in a separate view.
//
// There is no dedicated add/remove endpoint: the agent's `McpClientService` is an
// `IConfigReload`, so writing `mcp.servers` through `PATCH /api/config` makes it diff
// the list and connect/disconnect on its own. The list endpoint therefore returns each
// server's live config so this editor can round-trip it through that single patch.
const mcpServers = ref<McpServerSummaryDto[]>([]);
const mcpLoading = ref(false);
const mcpReloading = ref(false);
const mcpSaving = ref(false);
const mcpPending = ref<Set<string>>(new Set());

const TRANSPORTS = ["stdio", "http", "sse"] as const;

/** Form model — args are one-per-line so quoted arguments survive a round trip. */
interface McpDraft {
  name: string;
  transport: string;
  command: string;
  args: string;
  endpoint: string;
  enabled: boolean;
  healthCheckEnabled: boolean;
  timeoutSeconds: string;
}

const draft = ref<McpDraft | null>(null);
/** Name of the server being edited, or null when adding a new one. */
const editingName = ref<string | null>(null);

function emptyDraft(): McpDraft {
  return {
    name: "",
    transport: "stdio",
    command: "",
    args: "",
    endpoint: "",
    enabled: true,
    healthCheckEnabled: true,
    timeoutSeconds: "30",
  };
}

function toDraft(def: McpServerDefinitionDto): McpDraft {
  return {
    name: def.name,
    transport: def.transport,
    command: def.command ?? "",
    args: (def.args ?? []).join("\n"),
    endpoint: def.endpoint ?? "",
    enabled: def.enabled,
    healthCheckEnabled: def.healthCheckEnabled,
    // int32 arrives as `number | string` in the generated schema; normalise for the input.
    timeoutSeconds: String(def.timeoutSeconds ?? 30),
  };
}

function toDefinition(d: McpDraft): McpServerDefinitionDto {
  return {
    name: d.name.trim(),
    transport: d.transport,
    command: d.command.trim() || null,
    args: d.args
      .split("\n")
      .map((a) => a.trim())
      .filter(Boolean),
    endpoint: d.endpoint.trim() || null,
    enabled: d.enabled,
    healthCheckEnabled: d.healthCheckEnabled,
    timeoutSeconds: Number(d.timeoutSeconds) || 30,
  };
}

async function loadMcp(): Promise<void> {
  if (!client.value) return;
  mcpLoading.value = true;
  try {
    const res = await client.value.listMcpServers();
    mcpServers.value = Array.isArray(res.servers) ? res.servers : [];
  } catch (e) {
    // A missing MCP surface must not blank the tool registry above it.
    toast.warn(`${t("tools.mcpLoadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    mcpLoading.value = false;
  }
}

async function reloadMcp(): Promise<void> {
  if (!client.value || mcpReloading.value) return;
  mcpReloading.value = true;
  try {
    await client.value.reloadMcpServers();
    toast.success(t("tools.mcpReloaded"));
    await loadMcp();
  } catch (e) {
    toast.error(`${t("tools.mcpReloadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    mcpReloading.value = false;
  }
}

function startAdd(): void {
  editingName.value = null;
  draft.value = emptyDraft();
}

function startEdit(server: McpServerSummaryDto): void {
  editingName.value = server.name;
  draft.value = toDraft(server.config);
}

function cancelEdit(): void {
  draft.value = null;
  editingName.value = null;
}

/**
 * Writes the whole `mcp.servers` array. A merge patch replaces arrays wholesale
 * rather than merging by index, so the list must always be sent in full.
 */
async function persist(
  next: McpServerDefinitionDto[],
  successKey: string,
): Promise<void> {
  if (!client.value || mcpSaving.value) return;
  mcpSaving.value = true;
  try {
    await client.value.patchConfig({ mcp: { servers: next } });
    // The config change already schedules a hot-reload, but that is fire-and-forget.
    // Awaiting the explicit reload makes the status shown below deterministic.
    await client.value.reloadMcpServers();
    toast.success(t(successKey));
    await loadMcp();
  } catch (e) {
    toast.error(`${t("tools.mcpSaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    mcpSaving.value = false;
  }
}

async function saveDraft(): Promise<void> {
  const d = draft.value;
  if (!d) return;

  const name = d.name.trim();
  if (!name) {
    toast.error(t("tools.mcpNameRequired"));
    return;
  }

  const isStdio = d.transport === "stdio";
  if (isStdio && !d.command.trim()) {
    toast.error(t("tools.mcpCommandRequired"));
    return;
  }
  if (!isStdio) {
    const endpoint = d.endpoint.trim();
    if (!endpoint) {
      toast.error(t("tools.mcpEndpointRequired"));
      return;
    }
    try {
      new URL(endpoint);
    } catch {
      toast.error(t("tools.mcpEndpointInvalid"));
      return;
    }
  }

  // Drop both the row being edited (it may have been renamed) and any row that
  // already holds the target name, so the result can never contain a duplicate.
  const existingName = editingName.value?.toLowerCase() ?? null;
  const nextName = name.toLowerCase();
  const next = mcpServers.value
    .filter((s) => s.name.toLowerCase() !== existingName && s.name.toLowerCase() !== nextName)
    .map((s) => s.config);
  next.push(toDefinition(d));

  await persist(next, "tools.mcpSaved");
  cancelEdit();
}

async function toggleEnabled(server: McpServerSummaryDto): Promise<void> {
  if (!client.value || mcpPending.value.has(server.name)) return;
  mcpPending.value.add(server.name);
  try {
    const next = mcpServers.value.map((s) =>
      s.name === server.name
        ? { ...s.config, enabled: !s.config.enabled }
        : s.config,
    );
    await persist(next, "tools.mcpSaved");
  } finally {
    mcpPending.value.delete(server.name);
  }
}

async function removeMcp(server: McpServerSummaryDto): Promise<void> {
  if (!client.value || mcpPending.value.has(server.name)) return;
  if (!window.confirm(t("tools.mcpRemoveConfirm", { name: server.name }))) return;

  mcpPending.value.add(server.name);
  try {
    const next = mcpServers.value
      .filter((s) => s.name !== server.name)
      .map((s) => s.config);
    await persist(next, "tools.mcpRemoved");
  } finally {
    mcpPending.value.delete(server.name);
  }
}

function mcpStatusClass(server: McpServerSummaryDto): string {
  switch (server.status.toLowerCase()) {
    case "healthy":
      return "text-emerald-400";
    case "unhealthy":
      return "text-red-400";
    case "disconnected":
      return "text-amber-400";
    case "disabled":
      return "text-secondary";
    default:
      return "text-secondary";
  }
}

/** int32 fields are typed `number | string` by the generated schema. */
function failureCount(tool: ToolDto): number {
  return Number(tool.consecutiveFailures) || 0;
}

function healthClass(tool: ToolDto): string {
  const s = (tool.healthStatus ?? "").toLowerCase();
  if (s.includes("healthy") || s === "ok") return "text-emerald-400";
  if (s.includes("degraded") || s.includes("unknown")) return "text-amber-400";
  if (s.includes("unhealthy") || s.includes("failed")) return "text-red-400";
  return "text-secondary";
}

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    const res = await client.value.listTools();
    tools.value = Array.isArray(res?.tools) ? res.tools : [];
  } catch (e) {
    toast.error(`${t("tools.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

async function toggle(tool: ToolDto): Promise<void> {
  if (!client.value || pending.value.has(tool.name)) return;

  const previous = tool.enabled;
  const next = !previous;
  tool.enabled = next; // optimistic
  pending.value.add(tool.name);

  try {
    const updated = next
      ? await client.value.enableTool(tool.name)
      : await client.value.disableTool(tool.name);
    // Trust the agent's own view over our guess.
    tool.enabled = updated?.enabled ?? next;
    tool.healthStatus = updated?.healthStatus ?? tool.healthStatus;
  } catch (e) {
    tool.enabled = previous; // revert
    toast.error(`${t("tools.toggleFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    pending.value.delete(tool.name);
  }
}

onMounted(() => {
  void load();
  void loadMcp();
});
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-5xl">
      <!-- Header -->
      <div class="mb-4 flex items-center justify-between">
        <div>
          <h1 class="text-lg font-semibold text-app">{{ t("tools.title") }}</h1>
          <p class="text-sm text-secondary">{{ t("tools.subtitle") }}</p>
        </div>
        <div class="flex items-center gap-2">
          <input
            v-model="search"
            type="search"
            :placeholder="t('tools.search')"
            class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
          />
          <button
            class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
            :disabled="loading"
            @click="load"
          >
            {{ t("common.refresh") }}
          </button>
        </div>
      </div>

      <!-- Summary -->
      <div class="mb-3 flex items-center gap-3 text-xs text-secondary">
        <span>{{ t("tools.total") }}: {{ tools.length }}</span>
        <span class="text-emerald-400">{{ t("tools.enabled") }}: {{ enabledCount }}</span>
      </div>

      <!-- Category filter -->
      <div class="mb-3 flex flex-wrap gap-1.5">
        <button
          class="rounded-full px-2.5 py-1 text-xs transition-colors"
          :class="category === null ? 'bg-emerald-600 text-white' : 'bg-tertiary text-secondary hover:bg-app'"
          @click="category = null"
        >
          {{ t("tools.allCategories") }}
        </button>
        <button
          v-for="c in categories"
          :key="c"
          class="rounded-full px-2.5 py-1 text-xs transition-colors"
          :class="category === c ? 'bg-emerald-600 text-white' : 'bg-tertiary text-secondary hover:bg-app'"
          @click="category = c"
        >
          {{ c }}
        </button>
      </div>

      <!-- List -->
      <div class="space-y-2">
        <p v-if="loading" class="text-sm text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="filtered.length === 0" class="text-sm text-secondary">{{ t("tools.none") }}</p>

        <div
          v-for="tool in filtered"
          :key="tool.name"
          class="flex items-start justify-between gap-4 rounded-xl border border-app bg-secondary px-4 py-3"
        >
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2">
              <span class="truncate text-sm font-medium text-app">{{ tool.name }}</span>
              <span class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary">
                {{ tool.category }}
              </span>
              <span
                v-if="tool.sideEffectLevel"
                class="rounded px-1.5 py-0.5 text-[10px]"
                :class="
                  tool.sideEffectLevel.toLowerCase().includes('high')
                    ? 'bg-red-500/20 text-red-400'
                    : 'bg-tertiary text-secondary'
                "
              >
                {{ t("tools.sideEffect") }}: {{ tool.sideEffectLevel }}
              </span>
            </div>

            <p v-if="tool.description" class="mt-0.5 text-xs text-secondary">
              {{ tool.description }}
            </p>

            <div class="mt-1 flex flex-wrap gap-3 text-[11px] text-secondary">
              <span :class="healthClass(tool)">
                {{ t("tools.health") }}: {{ tool.healthStatus || "—" }}
              </span>
              <!-- int32 is typed `number | string` by the generated schema (OpenAPI
                   permits string-encoded integers); the wire value is a number. -->
              <span v-if="failureCount(tool) > 0" class="text-red-400">
                {{ t("tools.failures") }}: {{ failureCount(tool) }}
              </span>
              <span v-if="tool.lastError" class="truncate text-red-400" :title="tool.lastError">
                {{ tool.lastError }}
              </span>
              <span v-if="tool.limits?.maxCallsPerMinute">
                {{ t("tools.rateLimit") }}: {{ tool.limits.maxCallsPerMinute }}/min
              </span>
            </div>
          </div>

          <!-- Enable/disable -->
          <button
            class="relative mt-0.5 h-5 w-9 shrink-0 rounded-full transition-colors"
            :class="tool.enabled ? 'bg-emerald-600' : 'bg-tertiary'"
            :disabled="pending.has(tool.name)"
            :aria-label="`${tool.enabled ? t('tools.disable') : t('tools.enable')} ${tool.name}`"
            @click="toggle(tool)"
          >
            <span
              class="absolute top-0.5 h-4 w-4 rounded-full bg-white transition-transform"
              :class="tool.enabled ? 'translate-x-4' : 'translate-x-0.5'"
            />
          </button>
        </div>
      </div>

      <!-- MCP servers. Writes go through PATCH /api/config (mcp.servers), which the
           agent's McpClientService picks up via IConfigReload. -->
      <section class="mt-8">
        <div class="mb-2 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">{{ t("tools.mcpServers") }}</h2>
          <div class="flex items-center gap-2">
            <span class="text-xs text-secondary">
              {{ mcpServers.length }} {{ t("tools.mcpConfigured") }}
            </span>
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="mcpSaving || !client"
              @click="startAdd"
            >
              {{ t("tools.mcpAdd") }}
            </button>
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app transition-colors hover:bg-tertiary disabled:opacity-50"
              :disabled="mcpReloading || mcpSaving || !client"
              @click="reloadMcp"
            >
              {{ mcpReloading ? t("common.saving") : t("tools.mcpReload") }}
            </button>
          </div>
        </div>

        <!-- Add / edit form -->
        <form
          v-if="draft"
          class="mb-3 space-y-2 rounded-xl border border-emerald-600/40 bg-secondary px-4 py-3"
          @submit.prevent="saveDraft"
        >
          <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <label class="flex flex-col gap-1">
              <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldName") }}</span>
              <input
                v-model="draft.name"
                type="text"
                :placeholder="t('tools.mcpFieldNamePlaceholder')"
                class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
              />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldTransport") }}</span>
              <select
                v-model="draft.transport"
                class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
              >
                <option v-for="tr in TRANSPORTS" :key="tr" :value="tr">{{ tr }}</option>
              </select>
            </label>

            <template v-if="draft.transport === 'stdio'">
              <label class="flex flex-col gap-1">
                <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldCommand") }}</span>
                <input
                  v-model="draft.command"
                  type="text"
                  placeholder="npx"
                  class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
                />
              </label>

              <label class="flex flex-col gap-1">
                <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldArgs") }}</span>
                <textarea
                  v-model="draft.args"
                  rows="2"
                  :placeholder="t('tools.mcpFieldArgsPlaceholder')"
                  class="rounded-lg border border-app bg-tertiary px-3 py-1.5 font-mono text-xs text-app outline-none focus:border-emerald-500"
                />
              </label>
            </template>

            <label v-else class="flex flex-col gap-1 sm:col-span-2">
              <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldEndpoint") }}</span>
              <input
                v-model="draft.endpoint"
                type="url"
                placeholder="https://example.test/mcp"
                class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
              />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[11px] text-secondary">{{ t("tools.mcpFieldTimeout") }}</span>
              <input
                v-model="draft.timeoutSeconds"
                type="number"
                min="1"
                class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
              />
            </label>
          </div>

          <div class="flex items-center gap-4 text-xs text-secondary">
            <label class="flex items-center gap-1.5">
              <input v-model="draft.enabled" type="checkbox" />
              {{ t("tools.mcpFieldEnabled") }}
            </label>
            <label class="flex items-center gap-1.5">
              <input v-model="draft.healthCheckEnabled" type="checkbox" />
              {{ t("tools.mcpFieldHealthCheck") }}
            </label>
          </div>

          <div class="flex items-center gap-2 pt-1">
            <button
              type="submit"
              class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs text-white transition-opacity hover:opacity-90 disabled:opacity-50"
              :disabled="mcpSaving"
            >
              {{ mcpSaving ? t("common.saving") : t("common.save") }}
            </button>
            <button
              type="button"
              class="rounded-lg border border-app px-3 py-1.5 text-xs text-app transition-colors hover:bg-tertiary"
              @click="cancelEdit"
            >
              {{ t("common.cancel") }}
            </button>
          </div>
        </form>

        <p v-if="mcpLoading" class="text-sm text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="mcpServers.length === 0" class="text-sm text-secondary">
          {{ t("tools.mcpNone") }}
        </p>

        <div v-else class="space-y-1">
          <div
            v-for="server in mcpServers"
            :key="server.name"
            class="flex items-center justify-between gap-3 rounded-lg border border-app bg-secondary px-3 py-2"
          >
            <div class="min-w-0">
              <div class="flex items-center gap-2">
                <span class="text-sm text-app">{{ server.name }}</span>
                <span class="text-[11px] text-secondary">{{ server.transport }}</span>
                <span
                  v-if="!server.config.enabled"
                  class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary"
                >
                  {{ t("tools.mcpOff") }}
                </span>
              </div>
              <p class="truncate text-[11px] text-secondary">
                {{ server.config.command ?? server.config.endpoint }}
                <span v-if="server.config.command && server.config.args?.length">
                  {{ server.config.args.join(" ") }}
                </span>
              </p>
              <p v-if="server.error" class="truncate text-[11px] text-red-400" :title="server.error">
                {{ server.error }}
              </p>
            </div>

            <div class="flex shrink-0 items-center gap-3 text-xs">
              <span class="text-secondary">{{ server.toolCount }} {{ t("tools.mcpTools") }}</span>
              <span :class="mcpStatusClass(server)">{{ server.status }}</span>

              <button
                class="relative h-5 w-9 shrink-0 rounded-full transition-colors disabled:opacity-50"
                :class="server.config.enabled ? 'bg-emerald-600' : 'bg-tertiary'"
                :disabled="mcpPending.has(server.name) || mcpSaving"
                :aria-label="
                  `${server.config.enabled ? t('tools.mcpDisable') : t('tools.mcpEnable')} ${server.name}`
                "
                @click="toggleEnabled(server)"
              >
                <span
                  class="absolute top-0.5 h-4 w-4 rounded-full bg-white transition-transform"
                  :class="server.config.enabled ? 'translate-x-4' : 'translate-x-0.5'"
                />
              </button>

              <button
                class="rounded border border-app px-2 py-0.5 text-[11px] text-app transition-colors hover:bg-tertiary disabled:opacity-50"
                :disabled="mcpSaving"
                @click="startEdit(server)"
              >
                {{ t("common.edit") }}
              </button>

              <button
                class="rounded border border-red-500/40 px-2 py-0.5 text-[11px] text-red-400 transition-colors hover:bg-red-500/10 disabled:opacity-50"
                :disabled="mcpPending.has(server.name) || mcpSaving"
                @click="removeMcp(server)"
              >
                {{ t("common.delete") }}
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>
  </div>
</template>