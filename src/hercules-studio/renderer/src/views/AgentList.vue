<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useToastStore } from "../stores/toast";
import type { DiscoveredAgent } from "@renderer/platform/capabilities";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

async function scan() {
  await connections.scan();
  if (connections.discovered.length > 0) {
    toast.success(t("connection.scanResults", { n: connections.discovered.length }));
  } else {
    toast.info(t("connection.noAgentsFound"));
  }
}

function addDiscovered(agent: DiscoveredAgent): void {
  connections.openAddForm({
    baseUrl: agent.baseUrl,
    name: agent.displayName || defaultNameFromUrl(agent.baseUrl),
  });
}

/** Fallback label for an agent that did not advertise a display name. */
function defaultNameFromUrl(baseUrl: string): string {
  try {
    const { hostname, port } = new URL(baseUrl);
    return port ? `${hostname}:${port}` : hostname;
  } catch {
    return "agent";
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
          @click="connections.openAddForm()"
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
          :key="agent.baseUrl"
          class="flex items-center justify-between rounded-lg border border-app bg-secondary px-3 py-2 transition-colors hover:border-emerald-500/30"
        >
          <div class="flex items-center gap-2">
            <span class="h-2 w-2 rounded-full" :class="agent.authRequired ? 'bg-amber-500' : 'bg-green-500'" />
            <span class="text-sm text-app">{{ agent.displayName ?? agent.baseUrl }}</span>
            <span class="text-xs text-secondary">{{ agent.baseUrl }}</span>
            <span
              v-if="agent.authRequired"
              class="rounded bg-amber-500/20 px-1.5 py-0.5 text-xs text-amber-400"
            >
              {{ t("connection.authRequired") }}
            </span>
          </div>
          <button class="text-sm text-emerald-400 hover:text-emerald-300" @click="addDiscovered(agent)">
            {{ t("connection.addDiscovered") }}
          </button>
        </div>
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
            <span
              v-if="!conn.hasSession"
              class="rounded bg-amber-500/20 px-1.5 py-0.5 text-xs text-amber-400"
              :title="t('session.keyNeverStored')"
            >
              {{ t("connection.noSession") }}
            </span>
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
      v-if="connections.list.length === 0 && connections.discovered.length === 0"
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