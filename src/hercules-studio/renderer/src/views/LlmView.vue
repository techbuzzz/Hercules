<script setup lang="ts">
/**
 * LLM providers view (Stage 6.2).
 *
 * `GET /api/llm/config` is a deliberately secret-free DTO, so this view shows and
 * edits provider selection, fallback chain and models — **never API keys**. Keys
 * stay in the agent's config and are not readable through any endpoint Studio uses.
 *
 * Health comes from `/api/llm/health`, whose per-provider result shape is not
 * uniform, so it is rendered defensively rather than assumed.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { LlmConfigDto } from "../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const config = ref<LlmConfigDto | null>(null);
const health = ref<Record<string, unknown>>({});
const loading = ref(false);
const saving = ref(false);
const probing = ref(false);

/** Editable subset — mirrors the secret-free DTO. */
const draft = ref<Record<string, string>>({});
const original = ref("");

const PROVIDERS = [
  { id: "yandexgpt", modelKey: "yandexGptModel" },
  { id: "ollama-cloud", modelKey: "ollamaCloudModel" },
  { id: "ollama-local", modelKey: "ollamaLocalModel" },
  { id: "openai-compatible", modelKey: "openAICompatibleModel" },
] as const;

const client = computed(() => connections.client);

const dirty = computed(
  () => Object.keys(draft.value).length > 0 && JSON.stringify(draft.value) !== original.value,
);

function isHealthy(value: unknown): boolean | null {
  if (value === null || value === undefined) return null;
  if (typeof value === "boolean") return value;
  if (typeof value === "object") {
    const rec = value as Record<string, unknown>;
    for (const key of ["healthy", "isHealthy", "ok", "available"]) {
      if (typeof rec[key] === "boolean") return rec[key] as boolean;
    }
    if (typeof rec.status === "string") {
      const s = rec.status.toLowerCase();
      if (s.includes("healthy") || s === "ok") return true;
      if (s.includes("unhealthy") || s.includes("fail")) return false;
    }
  }
  return null;
}

function healthLabel(value: unknown): string {
  const h = isHealthy(value);
  if (h === true) return t("llm.healthy");
  if (h === false) return t("llm.unhealthy");
  return t("llm.unknown");
}

function healthClass(value: unknown): string {
  const h = isHealthy(value);
  if (h === true) return "text-emerald-400";
  if (h === false) return "text-red-400";
  return "text-secondary";
}

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    const cfg = await client.value.getLlmConfig();
    config.value = cfg;
    const next: Record<string, string> = {
      provider: String(cfg.provider ?? ""),
      fallback: Array.isArray(cfg.fallback) ? cfg.fallback.join(",") : String(cfg.fallback ?? ""),
      openAICompatibleEndpoint: String(cfg.openAICompatibleEndpoint ?? ""),
    };
    for (const p of PROVIDERS) {
      next[p.modelKey] = String((cfg as unknown as Record<string, unknown>)[p.modelKey] ?? "");
    }
    draft.value = next;
    original.value = JSON.stringify(next);
  } catch (e) {
    config.value = null;
    toast.warn(`${t("llm.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

async function probe(): Promise<void> {
  if (!client.value || probing.value) return;
  probing.value = true;
  try {
    health.value = await client.value.getLlmHealth();
  } catch (e) {
    health.value = {};
    toast.warn(`${t("llm.healthFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    probing.value = false;
  }
}

/**
 * Builds the config patch. Only fields this view exposes are sent, so a merge
 * patch cannot clobber unrelated config or wipe API keys stored server-side.
 */
function buildPatch(): Record<string, unknown> {
  // `draft` is a Partial, so these fields are absent until loaded from the agent. The
  // fallbacks match the agent's own defaults rather than sending empty strings.
  const provider = (draft.value.provider ?? "yandexgpt").trim();
  const fallback = (draft.value.fallback ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);

  const yandexGpt = { model: draft.value.yandexGptModel };
  const ollamaCloud = { model: draft.value.ollamaCloudModel };
  const ollamaLocal = { model: draft.value.ollamaLocalModel };
  const openAICompatible = {
    endpoint: (draft.value.openAICompatibleEndpoint ?? "").trim(),
    model: draft.value.openAICompatibleModel,
  };

  return {
    llm: { provider, fallback, yandexGpt, ollamaCloud, ollamaLocal, openAICompatible },
  };
}

async function save(): Promise<void> {
  if (!client.value || !dirty.value) return;
  saving.value = true;
  try {
    await client.value.patchConfig(buildPatch());
    toast.success(t("llm.saved"));
    await load();
  } catch (e) {
    toast.error(`${t("llm.saveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    saving.value = false;
  }
}

onMounted(async () => {
  await load();
  await probe();
});
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <div class="mx-auto w-full max-w-3xl">
      <div class="mb-4">
        <h1 class="text-lg font-semibold text-app">{{ t("llm.title") }}</h1>
        <p class="text-sm text-secondary">{{ t("llm.subtitle") }}</p>
      </div>

      <!-- Health -->
      <div class="mb-4 rounded-xl border border-app bg-secondary p-4">
        <div class="mb-2 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">{{ t("llm.health") }}</h2>
          <button
            class="rounded-lg border border-app px-2 py-1 text-xs text-app hover:bg-tertiary disabled:opacity-50"
            :disabled="probing"
            @click="probe"
          >
            {{ probing ? t("common.saving") : t("llm.probe") }}
          </button>
        </div>

        <p v-if="loading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
        <div v-else class="space-y-1">
          <div
            v-for="p in PROVIDERS"
            :key="p.id"
            class="flex items-center justify-between rounded px-2 py-1 text-xs"
            :class="p.id === config?.provider ? 'bg-tertiary' : ''"
          >
            <span class="text-app">{{ p.id }}</span>
            <span :class="healthClass(health[p.id] ?? health[p.id.replace('-', '_')])">
              {{ healthLabel(health[p.id] ?? health[p.id.replace('-', '_')]) }}
            </span>
          </div>
        </div>
      </div>

      <!-- Editor -->
      <div class="rounded-xl border border-app bg-secondary p-4">
        <p class="mb-3 text-xs text-secondary">{{ t("llm.keysHidden") }}</p>

        <div class="grid gap-3 sm:grid-cols-2">
          <label class="flex flex-col gap-1 text-xs text-secondary">
            {{ t("llm.activeProvider") }}
            <select
              v-model="draft.provider"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            >
              <option v-for="p in PROVIDERS" :key="p.id" :value="p.id">{{ p.id }}</option>
            </select>
          </label>

          <label class="flex flex-col gap-1 text-xs text-secondary">
            {{ t("llm.fallbackChain") }}
            <input
              v-model="draft.fallback"
              type="text"
              spellcheck="false"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
            />
          </label>

          <label
            v-for="p in PROVIDERS"
            :key="p.id"
            class="flex flex-col gap-1 text-xs text-secondary"
          >
            {{ p.id }} — {{ t("llm.model") }}
            <input
              v-model="draft[p.modelKey]"
              type="text"
              spellcheck="false"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
            />
          </label>

          <label class="flex flex-col gap-1 text-xs text-secondary sm:col-span-2">
            openai-compatible — {{ t("llm.endpoint") }}
            <input
              v-model="draft.openAICompatibleEndpoint"
              type="text"
              spellcheck="false"
              class="rounded-lg border border-app bg-tertiary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
            />
          </label>
        </div>

        <div class="mt-4 flex gap-2">
          <button
            class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50"
            :disabled="!dirty || saving || !config"
            @click="save"
          >
            {{ saving ? t("common.saving") : t("common.save") }}
          </button>
          <button
            v-if="dirty"
            class="rounded-lg border border-app px-3 py-2 text-sm text-app hover:bg-tertiary"
            @click="load"
          >
            {{ t("config.revert") }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>