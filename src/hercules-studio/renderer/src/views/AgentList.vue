<script setup lang="ts">
import { ref, computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import type { DiscoveredAgent, NewConnection } from "@shared/protocol";

const { t } = useI18n();
const connections = useConnectionsStore();

const showAddForm = ref(false);
const scanning = ref(false);
const discovered = ref<DiscoveredAgent[]>([]);

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

async function scan() {
  scanning.value = true;
  try {
    discovered.value = await window.studioAPI.scanner.scan();
  } finally {
    scanning.value = false;
  }
}

function addDiscovered(agent: DiscoveredAgent) {
  form.value.baseUrl = agent.endpoint;
  form.value.name = agent.displayName ?? `Agent ${agent.port}`;
  showAddForm.value = true;
}

async function testConnection() {
  testStatus.value = { state: "testing", message: t("connection.testing") };
  try {
    // Quick health probe via fetch
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
    testStatus.value = {
      state: "error",
      message: t("connection.testFailed", { error: e instanceof Error ? e.message : String(e) }),
    };
  }
}

async function save() {
  try {
    await connections.add(form.value);
    showAddForm.value = false;
    form.value = { name: "", baseUrl: "http://localhost:8421", apiKey: "", systemKey: "" };
    testStatus.value = { state: "idle", message: "" };
  } catch (e) {
    testStatus.value = {
      state: "error",
      message: e instanceof Error ? e.message : String(e),
    };
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
          class="flex items-center gap-2 rounded-lg border border-app px-3 py-2 text-sm text-app hover:bg-tertiary"
          :disabled="scanning"
          @click="scan"
        >
          {{ scanning ? t("connection.scanning") : t("empty.scanAgents") }}
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-500"
          @click="showAddForm = !showAddForm"
        >
          {{ t("connection.add") }}
        </button>
      </div>
    </div>

    <!-- Discovered agents -->
    <div v-if="discovered.length > 0" class="mb-6">
      <h2 class="mb-2 text-sm font-medium text-secondary">{{ t("connection.scanResults") }}</h2>
      <div class="space-y-1">
        <div
          v-for="agent in discovered"
          :key="agent.port"
          class="flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2"
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
          </div>
          <button class="text-sm text-emerald-400 hover:text-emerald-300" @click="addDiscovered(agent)">
            {{ t("connection.addDiscovered") }}
          </button>
        </div>
      </div>
    </div>

    <!-- Add connection form -->
    <div v-if="showAddForm" class="mb-6 rounded-xl border border-app bg-secondary p-4">
      <h2 class="mb-3 font-medium text-app">{{ t("connection.add") }}</h2>
      <div class="grid gap-3 sm:grid-cols-2">
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.name") }}
          <input
            v-model="form.name"
            type="text"
            :placeholder="t('connection.namePlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.baseUrl") }}
          <input
            v-model="form.baseUrl"
            type="text"
            :placeholder="t('connection.baseUrlPlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
          />
        </label>
      </div>
      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <label class="flex flex-col gap-1 text-xs text-secondary">
          {{ t("connection.apiKey") }}
          <input
            v-model="form.apiKey"
            type="password"
            :placeholder="t('connection.apiKeyPlaceholder')"
            class="rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
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
          class="rounded-lg border border-app px-4 py-2 text-sm text-app hover:bg-tertiary"
          :disabled="!form.baseUrl || !form.apiKey"
          @click="testConnection"
        >
          {{ t("connection.test") }}
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500"
          :disabled="!form.name || !form.baseUrl || !form.apiKey"
          @click="save"
        >
          {{ t("connection.save") }}
        </button>
        <button class="rounded-lg px-4 py-2 text-sm text-secondary hover:bg-tertiary" @click="showAddForm = false">
          {{ t("connection.cancel") }}
        </button>
      </div>
    </div>

    <!-- Connections list -->
    <div v-if="onlineAgents.length > 0" class="mb-4">
      <h2 class="mb-2 text-sm font-medium text-secondary">{{ t("status.online") }}</h2>
      <div class="space-y-1">
        <div
          v-for="conn in onlineAgents"
          :key="conn.id"
          class="flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full bg-green-500" />
            <span class="text-sm text-app">{{ conn.displayName }}</span>
            <span class="text-xs text-secondary">{{ conn.baseUrl }}</span>
          </div>
          <button class="text-sm text-red-400 hover:text-red-300" @click="connections.remove(conn.id)">
            Remove
          </button>
        </div>
      </div>
    </div>

    <div v-if="offlineAgents.length > 0">
      <h2 class="mb-2 text-sm font-medium text-secondary">{{ t("status.offline") }}</h2>
      <div class="space-y-1">
        <div
          v-for="conn in offlineAgents"
          :key="conn.id"
          class="flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full bg-zinc-500" />
            <span class="text-sm text-secondary">{{ conn.displayName }}</span>
            <span class="text-xs text-secondary">{{ conn.baseUrl }}</span>
          </div>
          <button class="text-sm text-red-400 hover:text-red-300" @click="connections.remove(conn.id)">
            Remove
          </button>
        </div>
      </div>
    </div>

    <!-- Empty -->
    <div
      v-if="connections.list.length === 0 && discovered.length === 0 && !showAddForm"
      class="flex flex-1 items-center justify-center text-secondary"
    >
      <p>{{ t("status.noAgent") }}</p>
    </div>
  </div>
</template>