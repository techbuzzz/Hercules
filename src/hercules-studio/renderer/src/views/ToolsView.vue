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
import type { ToolDto } from "../sdk/types";

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

onMounted(load);
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
              <span v-if="tool.consecutiveFailures > 0" class="text-red-400">
                {{ t("tools.failures") }}: {{ tool.consecutiveFailures }}
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
    </div>
  </div>
</template>