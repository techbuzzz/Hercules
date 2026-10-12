<script setup lang="ts">
/**
 * Add-connection dialog.
 *
 * Lives at app level rather than inside a view: the empty state offers "Add
 * connection manually", and the agents view that also offers it is not mounted
 * while there are zero connections. Driving the form from the store is what
 * makes the action reachable from both places.
 */
import { ref, computed, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useToastStore } from "../../stores/toast";
import type { NewConnection } from "@renderer/platform/capabilities";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const DEFAULT_BASE_URL = "http://localhost:8421";

const form = ref<NewConnection>({
  name: "",
  baseUrl: DEFAULT_BASE_URL,
  apiKey: "",
});

const testStatus = ref<{ state: "idle" | "testing" | "success" | "error"; message: string }>({
  state: "idle",
  message: "",
});

function isValidUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

const formErrors = computed(() => ({
  name: form.value.name.trim().length === 0,
  baseUrl: !isValidUrl(form.value.baseUrl),
  apiKey: form.value.apiKey.trim().length === 0,
}));

const canSave = computed(() => !formErrors.value.name && !formErrors.value.baseUrl && !formErrors.value.apiKey);
const canTest = computed(() => !formErrors.value.baseUrl && !formErrors.value.apiKey);

// Re-seed from the store each time the dialog opens so a scan result can
// prefill it, and so a previous entry never leaks into the next one.
watch(
  () => connections.showAddForm,
  (open) => {
    if (!open) return;
    const prefill = connections.addFormPrefill;
    form.value = {
      name: prefill?.name ?? "",
      baseUrl: prefill?.baseUrl || DEFAULT_BASE_URL,
      apiKey: "",
    };
    testStatus.value = { state: "idle", message: "" };
  },
);

async function testConnection(): Promise<void> {
  if (!canTest.value) return;
  testStatus.value = { state: "testing", message: t("connection.testing") };
  try {
    const res = await fetch(`${form.value.baseUrl}/agent.manifest.json`, {
      headers: { "X-Api-Key": form.value.apiKey },
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const manifest = (await res.json()) as { agentId?: string };
    testStatus.value = {
      state: "success",
      message: t("connection.testSuccess", { agentId: manifest.agentId ?? "unknown" }),
    };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    testStatus.value = { state: "error", message: t("connection.testFailed", { error: msg }) };
  }
}

async function save(): Promise<void> {
  if (!canSave.value) return;
  const name = form.value.name.trim();
  try {
    await connections.add({ ...form.value, name });
    toast.success(`Connected: ${name}`);
    // Wipe the key from component state the moment it is no longer needed.
    form.value.apiKey = "";
    connections.closeAddForm();
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    toast.error(t("connection.testFailed", { error: msg }));
  }
}
</script>

<template>
  <div
    v-if="connections.showAddForm"
    class="fixed inset-0 z-40 flex items-start justify-center bg-black/60 pt-24"
    @click.self="connections.closeAddForm()"
  >
    <div class="w-full max-w-lg rounded-xl border border-app bg-secondary p-6 shadow-2xl fade-in">
      <h2 class="mb-4 text-lg font-semibold text-app">{{ t("connection.add") }}</h2>

      <div class="grid gap-3 sm:grid-cols-2">
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.name") }} <span v-if="formErrors.name" class="text-red-400">*</span>
          <input
            v-model="form.name"
            type="text"
            :placeholder="t('connection.namePlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            :class="formErrors.name ? 'border-red-500' : ''"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.baseUrl") }} <span v-if="formErrors.baseUrl" class="text-red-400">*</span>
          <input
            v-model="form.baseUrl"
            type="text"
            :placeholder="t('connection.baseUrlPlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            :class="formErrors.baseUrl ? 'border-red-500' : ''"
          />
        </label>
      </div>

      <label class="mt-3 flex flex-col gap-1 text-xs text-secondary">
        {{ t("connection.apiKey") }} <span v-if="formErrors.apiKey" class="text-red-400">*</span>
        <input
          v-model="form.apiKey"
          type="password"
          autocomplete="off"
          :placeholder="t('connection.apiKeyPlaceholder')"
          class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
          :class="formErrors.apiKey ? 'border-red-500' : ''"
        />
        <span class="text-[11px] text-secondary">{{ t("connection.apiKeyHint") }}</span>
      </label>

      <div v-if="testStatus.state !== 'idle'" class="mt-3 text-sm">
        <span
          :class="{
            'text-secondary': testStatus.state === 'testing',
            'text-emerald-400': testStatus.state === 'success',
            'text-red-400': testStatus.state === 'error',
          }"
        >
          {{ testStatus.message }}
        </span>
      </div>

      <div class="mt-5 flex gap-2">
        <button
          class="rounded-lg border border-app px-4 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50 disabled:hover:bg-transparent"
          :disabled="!canTest || testStatus.state === 'testing'"
          @click="testConnection"
        >
          <span v-if="testStatus.state === 'testing'">{{ t("connection.testing") }}</span>
          <span v-else>{{ t("connection.test") }}</span>
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-50 disabled:hover:bg-emerald-600"
          :disabled="!canSave"
          @click="save"
        >
          {{ t("connection.save") }}
        </button>
        <button
          class="rounded-lg px-4 py-2 text-sm text-secondary transition-colors hover:bg-tertiary"
          @click="connections.closeAddForm()"
        >
          {{ t("connection.cancel") }}
        </button>
      </div>
    </div>
  </div>
</template>