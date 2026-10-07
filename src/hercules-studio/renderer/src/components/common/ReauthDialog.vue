<script setup lang="ts">
import { ref, computed } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "@renderer/stores/connections";

const { t } = useI18n();
const connections = useConnectionsStore();

const apiKey = ref("");
const busy = ref(false);

const target = computed(() =>
  connections.sessionExpiredId
    ? connections.list.find((c) => c.id === connections.sessionExpiredId)
    : null,
);

async function submit(): Promise<void> {
  const id = connections.sessionExpiredId;
  if (!id || !apiKey.value.trim()) return;
  busy.value = true;
  try {
    await connections.reauthenticate(id, apiKey.value.trim());
    apiKey.value = "";
  } catch {
    // The store already surfaced the failure via toast.
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div v-if="target" class="fixed inset-0 z-50 flex items-center justify-center bg-black/70">
    <div class="w-full max-w-md rounded-xl border border-app bg-secondary p-6 shadow-2xl">
      <h2 class="mb-2 text-lg font-semibold text-app">{{ t("session.expiredTitle") }}</h2>
      <p class="mb-4 text-sm text-secondary">
        {{ t("session.expiredBody", { name: target.displayName }) }}
      </p>

      <p class="mb-3 text-xs text-secondary">{{ t("session.keyNeverStored") }}</p>

      <form class="mb-4" @submit.prevent="submit">
        <input
          v-model="apiKey"
          type="password"
          autocomplete="off"
          :placeholder="t('session.enterKey')"
          class="w-full rounded-lg border border-app bg-tertiary px-3 py-2 text-sm text-app outline-none focus:border-emerald-500"
        />
      </form>

      <div class="flex justify-end gap-2">
        <button
          class="rounded-lg px-4 py-2 text-sm text-secondary hover:bg-tertiary"
          @click="connections.acknowledgeExpiry()"
        >
          {{ t("common.later") }}
        </button>
        <button
          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
          :disabled="busy || !apiKey.trim()"
          @click="submit"
        >
          {{ busy ? t("common.saving") : t("session.reconnect") }}
        </button>
      </div>
    </div>
  </div>
</template>