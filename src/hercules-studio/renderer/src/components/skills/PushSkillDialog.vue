<script setup lang="ts">
/**
 * Stage 3 — push a skill to one or more agents, plus Stage 5's MCP pre-check.
 *
 * Cross-agent push is the point: the same skill is authored once and installed on
 * whichever agents the operator selects, each of which may be a different deployment at a
 * different version. Targets settle independently so one unreachable agent does not lose
 * the others' results — the same failure policy the consensus fan-out uses.
 *
 * The MCP pre-check reports each target's configured MCP servers and their health. It is a
 * *status* check, not a dependency analysis: `SkillDto` carries no tool list, so there is
 * no honest way to say "this skill needs server X". What it can say truthfully is whether
 * MCP-backed tools will work on the target once the skill lands.
 */
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../../stores/connections";
import { useToastStore } from "../../stores/toast";
import type { SkillDetailDto } from "../../sdk/types";

const props = defineProps<{
  /** Skill being pushed; null means "create new". */
  skill: SkillDetailDto | null;
}>();

const emit = defineEmits<{ (e: "close"): void; (e: "done"): void }>();

const { t } = useI18n();
const connections = useConnectionsStore();
const toast = useToastStore();

type PushMode = "update" | "new" | "import";
type Conflict = "replace" | "skip" | "rename";

const mode = ref<PushMode>("update");
const conflict = ref<Conflict>("rename");
const targets = ref<string[]>([]);
const file = ref<File | null>(null);
const running = ref(false);

/** Per-target MCP status, resolved before the push so the operator sees it up front. */
const mcpByTarget = ref<Record<string, { total: number; unhealthy: number } | "error">>({});
const prechecking = ref(false);

interface PushResult {
  connectionId: string;
  ok: boolean;
  error?: string;
}
const results = ref<PushResult[]>([]);

const canPrecheck = computed(() => targets.value.length > 0 && !running.value);

function toggleTarget(id: string): void {
  targets.value = targets.value.includes(id)
    ? targets.value.filter((x) => x !== id)
    : [...targets.value, id];
}

async function precheck(): Promise<void> {
  if (!canPrecheck.value) return;
  prechecking.value = true;
  mcpByTarget.value = {};
  try {
    await Promise.all(
      targets.value.map(async (id) => {
        const api = connections.clientFor(id);
        if (!api) {
          mcpByTarget.value = { ...mcpByTarget.value, [id]: "error" };
          return;
        }
        try {
          const servers = await api.listMcpServers();
          const list = Array.isArray(servers.servers) ? servers.servers : [];
          const unhealthy = list.filter((s) => {
            const status = s.config?.enabled === false ? "Disabled" : s.status;
            return /unhealthy|disconnected/i.test(String(status));
          }).length;
          mcpByTarget.value = {
            ...mcpByTarget.value,
            [id]: { total: list.length, unhealthy },
          };
        } catch {
          mcpByTarget.value = { ...mcpByTarget.value, [id]: "error" };
        }
      }),
    );
  } finally {
    prechecking.value = false;
  }
}

function pickFile(e: Event): void {
  const input = e.target as HTMLInputElement;
  file.value = input.files?.[0] ?? null;
}

async function push(): Promise<void> {
  if (running.value || targets.value.length === 0) return;
  if (mode.value === "import" && !file.value) {
    toast.error(t("skills.importNeedsFile"));
    return;
  }

  running.value = true;
  results.value = [];

  const settled = await Promise.all(
    targets.value.map(async (connectionId): Promise<PushResult> => {
      const api = connections.clientFor(connectionId);
      if (!api) return { connectionId, ok: false, error: t("skills.pushNoSession") };

      try {
        if (mode.value === "import") {
          // `push()` returns early when no file is chosen, so this is reachable only
          // with one set — captured as a local to keep the assertion local.
          const pkg = file.value;
          if (!pkg) return { connectionId, ok: false, error: t("skills.importNeedsFile") };
          await api.importSkill(pkg, conflict.value);
        } else if (mode.value === "update" && props.skill) {
          const meta = props.skill.meta;
          await api.updateSkill(meta.id, {
            triggers: meta.phraseReceivers,
            prompt: props.skill.prompt,
            description: props.skill.descriptionMarkdown,
          });
        } else {
          // "New" means a fresh skill record on the target, not a blank editor — the
          // agent requires a prompt to create one, so both modes push the skill being
          // viewed. Only import works without one.
          const meta = props.skill?.meta;
          await api.createSkill({
            name: meta?.name ?? t("skills.newSkillName"),
            trigger: meta?.phraseReceivers?.[0] ?? "",
            prompt: props.skill?.prompt ?? "",
            ...(props.skill?.descriptionMarkdown
              ? { description: props.skill.descriptionMarkdown }
              : {}),
          });
        }
        return { connectionId, ok: true };
      } catch (e) {
        return { connectionId, ok: false, error: e instanceof Error ? e.message : String(e) };
      }
    }),
  );

  results.value = settled;
  running.value = false;

  const okCount = settled.filter((r) => r.ok).length;
  if (okCount === targets.value.length) {
    toast.success(t("skills.pushAllOk", { n: okCount }));
    emit("done");
    emit("close");
  } else {
    const failed = settled.filter((r) => !r.ok);
    toast.warn(t("skills.pushPartial", { ok: okCount, total: targets.value.length }));
    // Surfaced inline rather than only in a toast: the operator needs to know *which*
    // agent failed before deciding whether to retry just that one.
    void failed;
  }
}

function resultFor(id: string): PushResult | undefined {
  return results.value.find((r) => r.connectionId === id);
}

const MODES: PushMode[] = ["update", "new", "import"];

interface McpRow {
  id: string;
  name: string;
  status: "error" | { total: number; unhealthy: number };
}

/**
 * Precomputed view model — the template cannot use `as` casts, and re-deriving this in the
 * template would recompute per render.
 */
const mcpRows = computed<McpRow[]>(() => {
  const rows: McpRow[] = [];
  for (const c of connections.list) {
    const status = mcpByTarget.value[c.id];
    if (status !== undefined) rows.push({ id: c.id, name: c.name, status });
  }
  return rows;
});

const resultRows = computed(() =>
  connections.list
    .map((c) => ({ id: c.id, name: c.name, result: resultFor(c.id) }))
    .filter((r): r is { id: string; name: string; result: PushResult } => r.result !== undefined),
);
</script>

<template>
  <div class="rounded-xl border border-emerald-600/40 bg-secondary p-4">
    <div class="mb-3 flex items-center justify-between">
      <h3 class="text-sm font-medium text-app">{{ t("skills.pushTitle") }}</h3>
      <button
        class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary"
        @click="emit('close')"
      >
        {{ t("common.close") }}
      </button>
    </div>

    <!-- Mode -->
    <div class="mb-3 flex rounded-lg border border-app p-0.5">
      <button
        v-for="m in MODES"
        :key="m"
        type="button"
        class="rounded-md px-2.5 py-1 text-xs transition-colors disabled:opacity-50"
        :class="mode === m ? 'bg-emerald-600 text-white' : 'text-secondary'"
        :disabled="running || (m !== 'import' && !skill)"
        @click="mode = m"
      >
        {{ t(`skills.pushMode.${m}`) }}
      </button>
    </div>

    <label v-if="mode === 'import'" class="mb-3 flex flex-col gap-1">
      <span class="text-[11px] text-secondary">{{ t("skills.importFile") }}</span>
      <input
        type="file"
        accept=".skillpkg,.zip"
        class="text-[11px] text-secondary"
        @change="pickFile"
      />
      <select
        v-model="conflict"
        class="rounded-lg border border-app bg-tertiary px-3 py-1.5 text-sm text-app"
      >
        <option value="rename">rename</option>
        <option value="replace">replace</option>
        <option value="skip">skip</option>
      </select>
    </label>

    <!-- Targets -->
    <p class="mb-1 text-[11px] text-secondary">{{ t("skills.pushTargets") }}</p>
    <div class="mb-2 flex flex-wrap gap-1.5">
      <button
        v-for="c in connections.list"
        :key="c.id"
        type="button"
        class="rounded-full px-2.5 py-1 text-xs transition-colors disabled:opacity-50"
        :class="targets.includes(c.id) ? 'bg-emerald-600 text-white' : 'bg-tertiary text-secondary hover:bg-app'"
        :disabled="running"
        @click="toggleTarget(c.id)"
      >
        {{ c.name }}
        <span v-if="c.status !== 'online'" class="text-[10px] text-secondary">offline</span>
      </button>
    </div>

    <!-- MCP pre-check (Stage 5) -->
    <div class="mb-3 rounded-lg border border-app bg-app px-3 py-2">
      <div class="flex items-center justify-between">
        <span class="text-[11px] text-secondary">{{ t("skills.mcpPrecheck") }}</span>
        <button
          class="rounded border border-app px-2 py-0.5 text-[11px] text-app hover:bg-tertiary disabled:opacity-50"
          :disabled="!canPrecheck || prechecking"
          @click="precheck"
        >
          {{ prechecking ? t("common.loading") : t("skills.mcpPrecheckRun") }}
        </button>
      </div>

      <div v-if="mcpRows.length > 0" class="mt-1.5 space-y-0.5">
        <div
          v-for="row in mcpRows"
          :key="row.id"
          class="flex items-center justify-between text-[11px]"
        >
          <span class="truncate text-secondary">{{ row.name }}</span>
          <span v-if="row.status === 'error'" class="text-red-400">
            {{ t("skills.mcpPrecheckFailed") }}
          </span>
          <span v-else class="text-app">
            {{ t("skills.mcpServerCount", { n: row.status.total }) }}
            <span v-if="row.status.unhealthy > 0" class="ml-1 text-amber-400">
              {{ t("skills.mcpUnhealthy", { n: row.status.unhealthy }) }}
            </span>
          </span>
        </div>
      </div>
      <p v-else class="mt-1 text-[10px] text-secondary">{{ t("skills.mcpPrecheckHint") }}</p>
    </div>

    <!-- Results -->
    <div v-if="resultRows.length > 0" class="mb-3 space-y-0.5">
      <div
        v-for="row in resultRows"
        :key="row.id"
        class="flex items-center justify-between text-[11px]"
      >
        <span class="truncate text-secondary">{{ row.name }}</span>
        <span :class="row.result.ok ? 'text-emerald-400' : 'text-red-400'">
          {{ row.result.ok ? t("skills.pushOk") : row.result.error }}
        </span>
      </div>
    </div>

    <button
      type="button"
      class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white transition-opacity hover:opacity-90 disabled:opacity-50"
      :disabled="running || targets.length === 0 || (mode === 'import' && !file)"
      @click="push"
    >
      {{ running ? t("common.saving") : t("skills.pushRun", { n: targets.length }) }}
    </button>
  </div>
</template>