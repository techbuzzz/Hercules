<script setup lang="ts">
import { ref, computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { DiscoveredAgent, NewConnection } from "@shared/protocol";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const showAddForm = ref(false);
const form = ref<NewConnection>({
  name: "",
  baseUrl: "http://localhost:8421",
  apiKey: "",
  systemKey: "",
});

const testStatus = ref<{ state: "idle" | "testing" | "success" | "error"; message: string }>({
  state: "idle",
  message: "",
});

// Validation
const formErrors = computed(() => ({
  name: form.value.name.trim().length === 0,
  baseUrl: !isValidUrl(form.value.baseUrl),
  apiKey: form.value.apiKey.trim().length === 0,
}));

const canSave = computed(() => !formErrors.value.name && !formErrors.value.baseUrl && !formErrors.value.apiKey);
const canTest = computed(() => !formErrors.value.baseUrl && !formErrors.value.apiKey);

function isValidUrl(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

async function scan() {
  await connections.scan();
  if (connections.discovered.length > 0) {
    toast.success(t("connection.scanResults") + `: ${connections.discovered.length} found`);
  } else {
    toast.info("No agents found on ports 8421-8521 + 5000");
  }
}

function addDiscovered(agent: DiscoveredAgent): void {
  form.value.baseUrl = agent.endpoint;
  form.value.name = agent.displayName ?? `Agent ${agent.port}`;
  showAddForm.value = true;
  testStatus.value = { state: "idle", message: "" };
}

async function testConnection(): Promise<void> {
  if (!canTest.value) return;
  testStatus.value = { state: "testing", message: t("connection.testing") };
  try {
    const res = await fetch(`${form.value.baseUrl}/agent.manifest.json`, {
      headers: { "X-Api-Key": form.value.apiKey },
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`);
    }
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
  try {
    await connections.add(form.value);
    toast.success(`Connected: ${form.value.name}`);
    showAddForm.value = false;
    form.value = { name: "", baseUrl: "http://localhost:8421", apiKey: "", systemKey: "" };
    testStatus.value = { state: "idle", message: "" };
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    testStatus.value = { state: "error", message: msg };
    toast.error(`Failed to add: ${msg}`);
  }
}

async function removeConnection(id: string, name: string): Promise<void> {
  try {
    await connections.remove(id);
    toast.info(`Removed: ${name}`);
  } catch (e) {
    toast.error(`Failed to remove: ${e instanceof Error ? e.message : String(e)}`);
  }
}

async function checkHealth(id: string, name: string): Promise<void> {
  try {
    const status = await connections.healthCheck(id);
    if (status.online) {
      toast.success(`${name} is online (${status.latencyMs}ms)`);
    } else {
      toast.warning(`${name} is offline: ${status.error ?? "unknown"}`);
    }
  } catch (e) {
    toast.error(`Health check failed: ${e instanceof Error ? e.message : String(e)}`);
  }
}

const onlineAgents = computed(() => connections.list.filter((c) => c.status === "online"));
const offlineAgents = computed(() => connections.list.filter((c) => c.status !== "online"));
</script>

<template>
  <div class="flex flex-1 flex-col overflow-y-auto bg-app p-4">
    <!-- Header -->
    <div class="mb-4 flex items-center justify-between">
      <div>
        <h1 class="text-lg font-semibold text-app">{{ t("activity.agents") }}</h1>
        <p class="text-sm text-secondary">Manage connected Hercules agents</p>
      </div>
      <div class="flex gap-2">
        <button
          class="flex items-center gap-2 rounded-lg border border-app px-3 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50"
          :disabled="connections.scanning"
          @click="scan"
        >
          <svg
            v-if="connections.scanning"
            class="h-4 w-4 animate-spin"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <path d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          {{ connections.scanning ? t("connection.scanning") : t("empty.scanAgents") }}
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-emerald-500"
          @click="showAddForm = !showAddForm"
        >
          {{ t("connection.add") }}
        </button>
      </div>
    </div>

    <!-- Discovered agents -->
    <div v-if="connections.discovered.length > 0" class="mb-6">
      <h2 class="mb-2 text-sm font-medium text-secondary">
        {{ t("connection.scanResults") }} ({{ connections.discovered.length }})
      </h2>
      <div class="space-y-1">
        <div
          v-for="agent in connections.discovered"
          :key="`${agent.foundVia}-${agent.port}`"
          class="flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2 transition-colors hover:border-emerald-500/30"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full bg-green-500" />
            <span class="text-sm text-app">{{ agent.displayName ?? `Port ${agent.port}` }}</span>
            <span class="text-xs text-secondary">{{ agent.endpoint }}</span>
            <span
              v-if="agent.authRequired"
              class="rounded bg-amber-500/20 px-1.5 py-0.5 text-xs text-amber-400"
            >
              Auth required
            </span>
            <span
              v-if="agent.foundVia === 'process'"
              class="rounded bg-sky-500/20 px-1.5 py-0.5 text-xs text-sky-400"
            >
              PID {{ agent.pid }}
            </span>
          </div>
          <button class="text-sm text-emerald-400 hover:text-emerald-300" @click="addDiscovered(agent)">
            {{ t("connection.addDiscovered") }}
          </button>
        </div>
      </div>
    </div>

    <!-- Add connection form -->
    <div v-if="showAddForm" class="mb-6 rounded-xl border border-app bg-secondary p-4 fade-in">
      <h2 class="mb-3 font-medium text-app">{{ t("connection.add") }}</h2>
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
      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.apiKey") }} <span v-if="formErrors.apiKey" class="text-red-400">*</span>
          <input
            v-model="form.apiKey"
            type="password"
            :placeholder="t('connection.apiKeyPlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
            :class="formErrors.apiKey ? 'border-red-500' : ''"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.systemKey") }}
          <input
            v-model="form.systemKey"
            type="password"
            :placeholder="t('connection.systemKeyPlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
          />
        </label>
      </div>

      <!-- Test status -->
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

      <!-- Actions -->
      <div class="mt-4 flex gap-2">
        <button
          class="rounded-lg border border-app px-4 py-2 text-sm text-app transition-colors hover:bg-tertiary disabled:opacity-50 disabled:hover:bg-transparent"
          :disabled="!canTest || testStatus.state === 'testing'"
          @click="testConnection"
        >
          <span v-if="testStatus.state === 'testing'">Testing...</span>
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
          @click="showAddForm = false"
        >
          {{ t("connection.cancel") }}
        </button>
      </div>
    </div>

    <!-- Connections list: online -->
    <div v-if="onlineAgents.length > 0" class="mb-4">
      <h2 class="mb-2 text-sm font-medium text-secondary">{{ t("status.online") }}</h2>
      <div class="space-y-1">
        <div
          v-for="conn in onlineAgents"
          :key="conn.id"
          class="group flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2 transition-colors hover:border-emerald-500/30"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full bg-green-500" />
            <span class="text-sm text-app">{{ conn.displayName }}</span>
            <span class="text-xs text-secondary">{{ conn.baseUrl }}</span>
          </div>
          <div class="flex gap-2 opacity-0 transition-opacity group-hover:opacity-100">
            <button
              class="text-xs text-secondary hover:text-app"
              :title="t('common.refresh')"
              @click="checkHealth(conn.id, conn.displayName)"
            >
              {{ t("common.refresh") }}
            </button>
            <button class="text-xs text-red-400 hover:text-red-300" @click="removeConnection(conn.id, conn.displayName)">
              {{ t("common.delete") }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Connections list: offline -->
    <div v-if="offlineAgents.length > 0">
      <h2 class="mb-2 text-sm font-medium text-secondary">{{ t("status.offline") }}</h2>
      <div class="space-y-1">
        <div
          v-for="conn in offlineAgents"
          :key="conn.id"
          class="group flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2 transition-colors hover:border-amber-500/30"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full bg-zinc-500" />
            <span class="text-sm text-secondary">{{ conn.displayName }}</span>
            <span class="text-xs text-secondary">{{ conn.baseUrl }}</span>
          </div>
          <div class="flex gap-2 opacity-0 transition-opacity group-hover:opacity-100">
            <button
              class="text-xs text-secondary hover:text-app"
              :title="t('common.refresh')"
              @click="checkHealth(conn.id, conn.displayName)"
            >
              {{ t("common.refresh") }}
            </button>
            <button
              class="text-xs text-red-400 hover:text-red-300"
              @click="removeConnection(conn.id, conn.displayName)"
            >
              {{ t("common.delete") }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty -->
    <div
      v-if="connections.list.length === 0 && connections.discovered.length === 0 && !showAddForm"
      class="flex flex-1 items-center justify-center text-secondary"
    >
      <div class="text-center">
        <p class="mb-2">{{ t("status.noAgent") }}</p>
        <button class="text-sm text-emerald-400 hover:text-emerald-300" @click="scan">
          {{ t("empty.scanAgents") }}
        </button>
      </div>
    </div>
  </div>
</template>