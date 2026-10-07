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
import type {
  QuotaStatusResponseDto,
  ApiKeySummaryDto,
  ApiKeysListResponseDto,
} from "../sdk/types";

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

// ---- Quotas (Stage 6.5) ----
// `IQuotaService` is read-only: limits live in the `quotas` section of the agent
// config. So "editing" means patching config through the same merge path as the
// rest of this view — there is deliberately no separate quota write endpoint.
const quotas = ref<QuotaStatusResponseDto | null>(null);
const quotaScope = ref("Agent");
const quotaScopeId = ref("default");
const quotaLoading = ref(false);
const quotaSaving = ref(false);
const quotaDraft = ref<Record<string, number>>({});
const originalQuotaDraft = ref("");

/** Per-agent limit fields that QuotasConfig actually exposes. */
const QUOTA_FIELDS = [
  "maxConcurrentRequestsPerAgent",
  "maxCallsPerMinutePerAgent",
  "maxTokensPerDayPerAgent",
  "maxStorageMbPerAgent",
  "maxMessagesPerDayPerAgent",
] as const;

function quotaPct(row: { limit: number | string; current: number | string }): number {
  const limit = Number(row.limit);
  const current = Number(row.current);
  if (!Number.isFinite(limit) || limit <= 0) return 0;
  return Math.max(0, Math.min(100, (current / limit) * 100));
}

async function loadQuotas(): Promise<void> {
  if (!client.value) return;
  quotaLoading.value = true;
  try {
    quotas.value = await client.value.getQuotas(quotaScope.value, quotaScopeId.value || undefined);

    const cfg = (await client.value.getConfig()) as ConfigDto;
    const section = (cfg.config?.quotas ?? {}) as Record<string, unknown>;
    const next: Record<string, number> = {};
    for (const f of QUOTA_FIELDS) {
      const v = Number(section[f]);
      if (Number.isFinite(v)) next[f] = v;
    }
    quotaDraft.value = next;
    originalQuotaDraft.value = JSON.stringify(next);
  } catch (e) {
    quotas.value = null;
    toast.warn(`${t("config.quotaLoadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    quotaLoading.value = false;
  }
}

const quotaDirty = computed(
  () => Object.keys(quotaDraft.value).length > 0 && JSON.stringify(quotaDraft.value) !== originalQuotaDraft.value,
);

async function saveQuotas(): Promise<void> {
  if (!client.value || !quotaDirty.value) return;
  quotaSaving.value = true;
  try {
    await client.value.patchConfig({ quotas: quotaDraft.value });
    toast.success(t("config.quotaSaved"));
    await loadQuotas();
  } catch (e) {
    toast.error(`${t("config.quotaSaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    quotaSaving.value = false;
  }
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

// ---- API keys and roles (Stage 6.3) ----
// Keys live in WebApi:ApiKeys / keys.json, not in AppConfig, so they are NOT part
// of the merge-patch editor above — they have their own endpoints under /api/auth.
// The raw key is never readable: every row is a fingerprint, and a generated key
// is shown exactly once, at creation.
const keys = ref<ApiKeysListResponseDto | null>(null);
const keysLoading = ref(false);
const keysSaving = ref(false);
const keyPending = ref<Set<string>>(new Set());
const newKeyRole = ref<"contribute" | "system">("contribute");
const newKeyDescription = ref("");
const generatedKey = ref<string | null>(null);

async function loadKeys(): Promise<void> {
  if (!client.value || !isSystem.value) return;
  keysLoading.value = true;
  try {
    keys.value = await client.value.listApiKeys();
  } catch (e) {
    keys.value = null;
    toast.warn(`${t("config.keysLoadFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    keysLoading.value = false;
  }
}

async function createKey(): Promise<void> {
  if (!client.value || keysSaving.value) return;
  keysSaving.value = true;
  try {
    const created = await client.value.createApiKey({
      role: newKeyRole.value,
      description: newKeyDescription.value.trim() || undefined,
    });
    // Only an agent-generated key comes back in plaintext, and only now.
    generatedKey.value = created.generatedKey ?? null;
    newKeyDescription.value = "";
    toast.success(t("config.keyCreated"));
    await loadKeys();
  } catch (e) {
    toast.error(`${t("config.keySaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    keysSaving.value = false;
  }
}

async function setKeyRole(key: ApiKeySummaryDto, role: "contribute" | "system"): Promise<void> {
  if (!client.value || key.role === role || keyPending.value.has(key.fingerprint)) return;
  keyPending.value.add(key.fingerprint);
  try {
    const res = await client.value.updateApiKey(key.fingerprint, { role });
    // int32 fields are typed `number | string` by the generated schema; the wire value is a number.
    const revoked = Number(res.revokedSessions) || 0;
    toast.success(
      revoked > 0
        ? t("config.keyRoleChangedWithSessions", { n: revoked })
        : t("config.keyRoleChanged"),
    );
    await loadKeys();
  } catch (e) {
    toast.error(`${t("config.keySaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    keyPending.value.delete(key.fingerprint);
  }
}

async function removeKey(key: ApiKeySummaryDto): Promise<void> {
  if (!client.value || keyPending.value.has(key.fingerprint)) return;
  if (!window.confirm(t("config.keyRemoveConfirm", { fingerprint: key.fingerprint }))) return;

  keyPending.value.add(key.fingerprint);
  try {
    const res = await client.value.deleteApiKey(key.fingerprint);
    toast.success(t("config.keyRemoved", { n: Number(res.revokedSessions) || 0 }));
    await loadKeys();
  } catch (e) {
    toast.error(`${t("config.keySaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  } finally {
    keyPending.value.delete(key.fingerprint);
  }
}

async function copyGeneratedKey(): Promise<void> {
  if (!generatedKey.value) return;
  try {
    await navigator.clipboard.writeText(generatedKey.value);
    toast.success(t("config.keyCopied"));
  } catch {
    // Clipboard is unavailable over plain HTTP on some browsers; the key stays
    // on screen either way, so this is a warning rather than an error.
    toast.warn(t("config.keyCopyFailed"));
  }
}

function dismissGeneratedKey(): void {
  generatedKey.value = null;
}

// ---- Session store backend (Stage 6.12) ----
// Reads the live config, which the agent redacts: `connectionString` comes back as the
// redaction marker, so an untouched field is simply left out of the patch (the agent
// strips the marker before merging). That is what makes "leave unchanged" expressible
// without ever putting the real connection string in the browser.
//
// Deliberately NOT a live toggle. `ISessionStore` is chosen by conditional DI
// registration at startup (Program.cs), so a switch only takes effect on restart. The
// UI says so rather than letting an operator believe it applied.
const storageProvider = ref("");
const storageSchema = ref("");
const storageConnString = ref("");
const storageDirty = ref(false);

async function loadStorage(): Promise<void> {
  if (!client.value) return;
  try {
    const cfg = (await client.value.getConfig()) as ConfigDto;
    const store = (
      ((cfg.config?.storage as Record<string, unknown> | undefined)?.sessionStore ??
        {}) as Record<string, unknown>
    );
    storageProvider.value = String(store.provider ?? "sqlite");
    storageSchema.value = String(store.schema ?? "public");
    storageDirty.value = false;
  } catch {
    // Non-fatal: the rest of the page does not depend on the storage section.
  }
}

async function saveStorage(): Promise<void> {
  if (!client.value || !storageDirty.value) return;

  // Only send connectionString when the operator typed one — otherwise omit the
  // property so the existing secret is left untouched server-side.
  const sessionStore: Record<string, unknown> = {
    provider: storageProvider.value,
    schema: storageSchema.value.trim() || "public",
  };
  if (storageConnString.value.trim()) sessionStore.connectionString = storageConnString.value.trim();

  try {
    await client.value.patchConfig({ storage: { sessionStore } });
    storageConnString.value = "";
    toast.success(t("config.storageSaved"));
    await loadStorage();
  } catch (e) {
    toast.error(`${t("config.storageSaveFailed")}: ${e instanceof Error ? e.message : String(e)}`);
  }
}

onMounted(() => {
  void load();
  void loadKeys();
  void loadStorage();
});
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

      <!-- Quotas (Stage 6.5): live usage from /api/quotas, limits edited via config PATCH -->
      <div class="mt-6 rounded-xl border border-app bg-secondary p-4">
        <div class="mb-3 flex items-center justify-between">
          <h2 class="text-sm font-medium text-app">{{ t("config.quotas") }}</h2>
          <div class="flex items-center gap-2">
            <select
              v-model="quotaScope"
              class="rounded-lg border border-app bg-tertiary px-2 py-1 text-xs text-app outline-none"
              @change="loadQuotas"
            >
              <option value="Agent">Agent</option>
              <option value="Skill">Skill</option>
              <option value="User">User</option>
              <option value="Tenant">Tenant</option>
            </select>
            <input
              v-model="quotaScopeId"
              type="text"
              spellcheck="false"
              class="w-28 rounded-lg border border-app bg-tertiary px-2 py-1 font-mono text-xs text-app outline-none focus:border-emerald-500"
              @keyup.enter="loadQuotas"
            />
            <button
              class="rounded-lg border border-app px-2 py-1 text-xs text-app hover:bg-tertiary disabled:opacity-50"
              :disabled="quotaLoading"
              @click="loadQuotas"
            >
              {{ t("common.refresh") }}
            </button>
          </div>
        </div>

        <p v-if="quotaLoading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="!quotas" class="text-xs text-secondary">{{ t("config.quotaUnavailable") }}</p>

        <template v-else>
          <div class="mb-4 space-y-2">
            <div v-for="row in quotas.limits" :key="row.type" class="flex items-center gap-3">
              <span class="w-32 shrink-0 text-xs text-secondary">{{ row.type }}</span>
              <div class="h-1.5 flex-1 overflow-hidden rounded-full bg-tertiary">
                <div
                  class="h-full rounded-full transition-all"
                  :class="row.isExceeded ? 'bg-red-500' : quotaPct(row) > 70 ? 'bg-amber-500' : 'bg-emerald-500'"
                  :style="{ width: `${quotaPct(row)}%` }"
                />
              </div>
              <span class="w-24 shrink-0 text-right text-[11px] text-secondary">
                {{ row.current }} / {{ row.limit }}
              </span>
            </div>
          </div>

          <p class="mb-2 text-[11px] text-secondary">{{ t("config.quotaHint") }}</p>
          <div class="grid gap-2 sm:grid-cols-2">
            <label
              v-for="(_, field) in quotaDraft"
              :key="field"
              class="flex items-center justify-between gap-2 text-xs text-secondary"
            >
              <span class="truncate font-mono">{{ field }}</span>
              <input
                v-model.number="quotaDraft[field]"
                type="number"
                min="0"
                class="w-28 rounded border border-app bg-tertiary px-2 py-1 text-right text-xs text-app outline-none focus:border-emerald-500"
              />
            </label>
          </div>

          <div class="mt-3 flex gap-2">
            <button
              class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
              :disabled="!quotaDirty || quotaSaving"
              @click="saveQuotas"
            >
              {{ quotaSaving ? t("common.saving") : t("config.saveQuotas") }}
            </button>
            <button
              v-if="quotaDirty"
              class="rounded-lg border border-app px-3 py-1.5 text-xs text-app hover:bg-tertiary"
              @click="loadQuotas"
            >
              {{ t("config.revert") }}
            </button>
          </div>
        </template>
      </div>

      <!-- Session store backend (Stage 6.12). System-only: this chooses where every
           session, interaction and audit row is persisted. -->
      <div v-if="isSystem" class="mt-6 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("config.storage") }}</h2>
        <p class="mb-3 text-xs text-secondary">{{ t("config.storageHint") }}</p>

        <div class="grid gap-2 sm:grid-cols-2">
          <label class="flex flex-col gap-1">
            <span class="text-[11px] text-secondary">{{ t("config.storageProvider") }}</span>
            <select
              v-model="storageProvider"
              :aria-label="t('config.storageProvider')"
              class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
              @change="storageDirty = true"
            >
              <option value="sqlite">sqlite</option>
              <option value="postgres">postgres</option>
            </select>
          </label>

          <label class="flex flex-col gap-1">
            <span class="text-[11px] text-secondary">{{ t("config.storageSchema") }}</span>
            <input
              v-model="storageSchema"
              type="text"
              :aria-label="t('config.storageSchema')"
              :disabled="storageProvider !== 'postgres'"
              class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500 disabled:opacity-50"
              @input="storageDirty = true"
            />
          </label>

          <label class="flex flex-col gap-1 sm:col-span-2">
            <span class="text-[11px] text-secondary">{{ t("config.storageConnectionString") }}</span>
            <input
              v-model="storageConnString"
              type="password"
              autocomplete="off"
              :aria-label="t('config.storageConnectionString')"
              :disabled="storageProvider !== 'postgres'"
              :placeholder="t('config.storageConnectionPlaceholder')"
              class="rounded-lg border border-app bg-tertiary px-3 py-1.5 font-mono text-sm text-app outline-none focus:border-emerald-500 disabled:opacity-50"
              @input="storageDirty = true"
            />
            <span class="text-[11px] text-secondary">{{ t("config.storageConnectionHint") }}</span>
          </label>
        </div>

        <div class="mt-3 flex items-center gap-2">
          <button
            class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
            :disabled="!storageDirty || !client"
            @click="saveStorage"
          >
            {{ t("config.saveStorage") }}
          </button>
          <button
            v-if="storageDirty"
            class="rounded-lg border border-app px-3 py-1.5 text-xs text-app hover:bg-tertiary"
            @click="loadStorage"
          >
            {{ t("config.revert") }}
          </button>
        </div>

        <!-- Two reasons this is not a live toggle. Stated, not hidden. -->
        <div class="mt-3 space-y-1 rounded-lg border border-amber-500/40 bg-amber-500/5 p-2">
          <p class="text-[11px] text-amber-400">{{ t("config.storageRestartNotice") }}</p>
          <p class="text-[11px] text-secondary">{{ t("config.storagePartialNotice") }}</p>
        </div>
      </div>

      <!-- API keys and roles (Stage 6.3). System-role only. Keys are shown as
           fingerprints — the plaintext exists exactly once, when generated. -->
      <div v-if="isSystem" class="mt-6 rounded-xl border border-app bg-secondary p-4">
        <h2 class="mb-2 text-sm font-medium text-app">{{ t("config.keys") }}</h2>
        <p class="mb-3 text-xs text-secondary">{{ t("config.keysHint") }}</p>

        <!-- One-time reveal of a generated key. -->
        <div
          v-if="generatedKey"
          class="mb-3 rounded-lg border border-emerald-600/50 bg-emerald-500/10 p-3"
        >
          <p class="mb-2 text-xs text-app">{{ t("config.keyGeneratedOnce") }}</p>
          <code class="block break-all rounded bg-app px-2 py-1.5 font-mono text-[11px] text-app">
            {{ generatedKey }}
          </code>
          <div class="mt-2 flex gap-2">
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary"
              @click="copyGeneratedKey"
            >
              {{ t("config.keyCopy") }}
            </button>
            <button
              class="rounded-lg border border-app px-3 py-1 text-xs text-app hover:bg-tertiary"
              @click="dismissGeneratedKey"
            >
              {{ t("common.close") }}
            </button>
          </div>
        </div>

        <div class="mb-3 flex flex-wrap items-end gap-2">
          <label class="flex flex-col gap-1">
            <span class="text-[11px] text-secondary">{{ t("config.newKeyRole") }}</span>
            <select
              v-model="newKeyRole"
              class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
            >
              <option value="contribute">contribute</option>
              <option value="system">system</option>
            </select>
          </label>
          <label class="flex min-w-48 flex-1 flex-col gap-1">
            <span class="text-[11px] text-secondary">{{ t("config.newKeyDescription") }}</span>
            <input
              v-model="newKeyDescription"
              type="text"
              :placeholder="t('config.newKeyDescriptionPlaceholder')"
              class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app outline-none focus:border-emerald-500"
            />
          </label>
          <button
            class="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-emerald-500 disabled:opacity-50"
            :disabled="keysSaving || !client"
            @click="createKey"
          >
            {{ keysSaving ? t("common.saving") : t("config.newKey") }}
          </button>
        </div>

        <p v-if="keysLoading" class="text-xs text-secondary">{{ t("common.loading") }}</p>
        <p v-else-if="!keys" class="text-xs text-secondary">{{ t("config.keysUnavailable") }}</p>

        <div v-else class="space-y-1">
          <p class="text-[11px] text-secondary">
            {{ t("config.keysSummary", { total: keys.count, system: keys.systemCount }) }}
          </p>

          <div
            v-for="key in keys.keys"
            :key="key.fingerprint"
            class="flex items-center justify-between gap-3 rounded-lg border border-app bg-app px-3 py-2"
          >
            <div class="min-w-0">
              <div class="flex items-center gap-2">
                <code class="font-mono text-[11px] text-app">{{ key.fingerprint }}</code>
                <span class="text-[11px] text-secondary">{{ key.label }}</span>
                <span
                  v-if="key.fingerprint === keys.currentFingerprint"
                  class="rounded bg-emerald-600/20 px-1.5 py-0.5 text-[10px] text-emerald-400"
                >
                  {{ t("config.keyCurrent") }}
                </span>
              </div>
              <p v-if="key.description" class="truncate text-[11px] text-secondary">
                {{ key.description }}
              </p>
            </div>

            <div class="flex shrink-0 items-center gap-2">
              <select
                :value="key.role"
                class="rounded border border-app bg-tertiary px-2 py-1 text-[11px] text-app outline-none focus:border-emerald-500 disabled:opacity-50"
                :disabled="keyPending.has(key.fingerprint) || keysSaving"
                :aria-label="t('config.keyRoleFor', { id: key.fingerprint })"
                @change="setKeyRole(key, ($event.target as HTMLSelectElement).value as 'contribute' | 'system')"
              >
                <option value="contribute">contribute</option>
                <option value="system">system</option>
              </select>
              <button
                class="rounded border border-red-500/40 px-2 py-0.5 text-[11px] text-red-400 transition-colors hover:bg-red-500/10 disabled:opacity-50"
                :disabled="keyPending.has(key.fingerprint) || keysSaving"
                @click="removeKey(key)"
              >
                {{ t("common.delete") }}
              </button>
            </div>
          </div>
        </div>
      </div>

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