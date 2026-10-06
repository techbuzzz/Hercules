<script setup lang="ts">
/**
 * Config view.
 *
 * Edits the agent's live runtime config. Saves go through PATCH (merge), which
 * both API-key roles may use; the destructive full replace (PUT) is deliberately
 * not exposed here — it is a system-role operation and a merge patch is the
 * safer default for an operator tweaking a running agent.
 */
import { ref, computed, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import { platform } from "../platform";
import type { ConfigDto } from "../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const draft = ref("");
const original = ref("");
const source = ref<string>("");
const loading = ref(false);
const saving = ref(false);
const parseError = ref<string | null>(null);

const client = computed(() => connections.client);

/** A session with the system role may request a restart. */
const isSystem = computed(() => {
  const id = connections.activeId;
  if (!id) return false;
  return platform.session.info(id)?.role === "system";
});

const dirty = computed(() => draft.value !== original.value);

/** Validated patch payload; null when the draft is not a JSON object. */
const parsedPatch = computed<Record<string, unknown> | null>(() => {
  try {
    const value = JSON.parse(draft.value) as unknown;
    parseError.value =
      value !== null && typeof value === "object" && !Array.isArray(value)
        ? null
        : t("config.notAnObject");
    return value as Record<string, unknown>;
  } catch (e) {
    parseError.value = e instanceof Error ? e.message : String(e);
    return null;
  }
});

async function load(): Promise<void> {
  if (!client.value) return;
  loading.value = true;
  try {
    const res: ConfigDto = await client.value.getConfig();
    original.value = JSON.stringify(res.config ?? {}, null, 2);
    draft.value = original.value;
    source.value = res.source ?? "";
  } catch (e) {
    toast.error(`${t("config.loadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    loading.value = false;
  }
}

async function save(): Promise<void> {
  if (!client.value || !parsedPatch.value) return;
  saving.value = true;
  try {
    await client.value.patchConfig(parsedPatch.value);
    toast.success(t("config.saved"));
    await load();
  } catch (e) {
    toast.error(`${t("config.saveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    saving.value = false;
  }
}

function revert(): void {
  draft.value = original.value;
  parseError.value = null;
}

const restarting = ref(false);

async function requestRestart(): Promise<void> {
  if (!client.value) return;
  restarting.value = true;
  try {
    await client.value.requestRestart(t("config.restartReason"));
    toast.success(t("config.restartRequested"));
  } catch (e) {
    toast.error(`${t("config.restartFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    restarting.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto p-4">
    <div class="mx-auto w-full max-w-4xl">
      <div class="mb-4 flex items-start justify-between">
        <div>
          <h1 class="text-lg font-semibold text-app">{{ t("config.title") }}</h1>
          <p class="text-sm text-secondary">{{ t("config.subtitle") }}</p>
        </div>
        <div class="flex gap-2">
          <button
            class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
            :disabled="loading"
            @click="load"
          >
            {{ t("common.refresh") }}
          </button>
          <button
            class="rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50 disabled:hover:bg-transparent"
            :disabled="!dirty"
            @click="revert"
          >
            {{ t("config.revert") }}
          </button>
          <button
            class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50 disabled:hover:bg-emerald-600"
            :disabled="!dirty || saving || parsedPatch === null"
            @click="save"
          >
            {{ saving ? t("common.saving") : t("config.savePatch") }}
          </button>
        </div>
      </div>

      <p v-if="source" class="mb-2 text-xs text-secondary">
        {{ t("config.source") }}: <code class="text-app">{{ source }}</code>
      </p>

      <p class="mb-2 text-xs text-secondary">{{ t("config.patchHint") }}</p>

      <textarea
        v-model="draft"
        rows="26"
        spellcheck="false"
        class="w-full rounded-lg border border-app bg-secondary px-3 py-2 font-mono text-xs text-app outline-none focus:border-emerald-500"
        :class="{ 'border-red-500': parseError }"
      />

      <p v-if="parseError" class="mt-2 text-xs text-red-400">
        {{ t("config.jsonInvalid") }}: {{ parseError }}
      </p>

      <!-- Restart requires a system-role session; the operator cannot kill the
           process from the UI, the supervisor does it (ADR-0009). -->
      <div class="mt-6 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("config.restart") }}</h2>
        <p class="mb-3 text-xs text-secondary">{{ t("config.restartHint") }}</p>
        <button
          v-if="isSystem"
          class="rounded-lg border border-red-500/50 px-3 py-2 text-sm text-red-400 transition-colors hover:bg-red-500/10 disabled:opacity-50"
          :disabled="restarting || !client"
          @click="requestRestart"
        >
          {{ restarting ? t("common.saving") : t("config.requestRestart") }}
        </button>
        <p v-else class="text-xs text-secondary">{{ t("config.restartNeedsSystem") }}</p>
      </div>
    </div>
  </div>
</template>