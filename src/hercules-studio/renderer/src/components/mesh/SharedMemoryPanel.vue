<script setup lang="ts">
/**
 * Stage 4.6 — shared memory browser.
 *
 * Publish is opt-in and scoped: a fact carries a category, content and an optional
 * allow-list of agents. Deleting confirms first, because a published fact is visible to
 * every peer it is allowed to reach and cannot be un-seen by them.
 */
import { computed, onMounted, ref } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useToastStore } from "../../stores/toast";
import type { SharedFact } from "../../sdk/types";

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

const facts = ref<SharedFact[]>([]);
const loading = ref(false);
const busy = ref(false);
const category = ref("");
const content = ref("");
const filter = ref("");

const visible = computed(() => {
  const q = filter.value.trim().toLowerCase();
  if (!q) return facts.value;
  return facts.value.filter(
    (f) =>
      (f.category ?? "").toLowerCase().includes(q) || (f.content ?? "").toLowerCase().includes(q),
  );
});

async function load(): Promise<void> {
  if (!connections.client) return;
  loading.value = true;
  try {
    facts.value = await connections.client.getSharedFacts();
  } catch {
    facts.value = [];
  } finally {
    loading.value = false;
  }
}

async function publish(): Promise<void> {
  if (!connections.client || !category.value.trim() || !content.value.trim()) return;
  busy.value = true;
  try {
    await connections.client.publishSharedFact({
      category: category.value.trim(),
      content: content.value.trim(),
    });
    category.value = "";
    content.value = "";
    toast.success(t("mesh.factPublished"));
    await load();
  } catch (e) {
    toast.error(`${t("mesh.factPublishFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    busy.value = false;
  }
}

async function sync(): Promise<void> {
  if (!connections.client || busy.value) return;
  busy.value = true;
  try {
    const res = await connections.client.syncSharedMemory();
    toast.success(t("mesh.factSynced", { n: Number(res.receivedCount) || 0 }));
    await load();
  } catch (e) {
    toast.error(`${t("mesh.factSyncFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    busy.value = false;
  }
}

async function remove(fact: SharedFact): Promise<void> {
  if (!connections.client || busy.value) return;
  if (!window.confirm(t("mesh.factDeleteConfirm", { id: fact.id }))) return;

  busy.value = true;
  try {
    await connections.client.deleteSharedFact(fact.id);
    toast.success(t("mesh.factDeleted"));
    await load();
  } catch (e) {
    toast.error(`${t("mesh.factDeleteFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    busy.value = false;
  }
}

/** Facts with no allow-list are broadcast to every reachable peer. */
function audience(fact: SharedFact): string {
  const allowed = fact.allowedAgents;
  if (!allowed || allowed.length === 0) return t("mesh.factAudienceAll");
  return allowed.join(", ");
}

onMounted(load);
</script>

<template>
  <div class="rounded-xl border border-app bg-secondary p-3">
    <div class="mb-2 flex items-center justify-between gap-2">
      <h3 class="text-sm font-medium text-app">{{ t("mesh.sharedTitle") }}</h3>
      <div class="flex items-center gap-2">
        <input
          v-model="filter"
          type="search"
          :placeholder="t('mesh.factFilter')"
          class="w-32 rounded border border-app bg-tertiary px-2 py-0.5 text-[11px] text-app outline-none focus:border-emerald-500"
        />
        <button
          class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary disabled:opacity-50"
          :disabled="busy || !connections.client"
          @click="sync"
        >
          {{ t("mesh.factSync") }}
        </button>
      </div>
    </div>

    <div class="mb-3 grid gap-1.5 sm:grid-cols-[10rem_1fr_auto]">
      <input
        v-model="category"
        type="text"
        :placeholder="t('mesh.factCategory')"
        class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
      />
      <input
        v-model="content"
        type="text"
        :placeholder="t('mesh.factContent')"
        class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500"
        @keyup.enter="publish"
      />
      <button
        class="rounded-lg bg-emerald-600 px-3 py-1 text-[11px] font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
        :disabled="busy || !connections.client || !category.trim() || !content.trim()"
        @click="publish"
      >
        {{ t("mesh.factPublish") }}
      </button>
    </div>

    <p v-if="loading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
    <p v-else-if="visible.length === 0" class="text-xs text-secondary">
      {{ facts.length === 0 ? t("mesh.factsEmpty") : t("common.notFound") }}
    </p>

    <div v-else class="space-y-1">
      <div
        v-for="fact in visible"
        :key="fact.id"
        class="flex items-start justify-between gap-2 rounded border border-app bg-app px-2 py-1.5"
      >
        <div class="min-w-0">
          <div class="flex items-center gap-2">
            <span class="rounded bg-tertiary px-1.5 py-0.5 text-[10px] text-secondary">
              {{ fact.category || t("mesh.factUncategorised") }}
            </span>
            <span class="text-[10px] text-secondary">{{ audience(fact) }}</span>
          </div>
          <p class="mt-0.5 text-[11px] text-app">{{ fact.content }}</p>
        </div>
        <button
          class="shrink-0 rounded border border-red-500/40 px-2 py-0.5 text-[10px] text-red-400 hover:bg-red-500/10 disabled:opacity-50"
          :disabled="busy || !connections.client"
          @click="remove(fact)"
        >
          {{ t("common.delete") }}
        </button>
      </div>
    </div>
  </div>
</template>