<script setup lang="ts">
/**
 * Chat view.
 *
 * Talks to the agent over REST with a session token (ADR-0009) — no local
 * process, no key in storage. `/api/chat` is request/response today, so the
 * assistant reply arrives in one piece; the typing effect is a local
 * presentation aid and is labelled as such in the UI.
 */
import { ref, computed, nextTick, onMounted } from "vue";
import { useI18n } from "vue-i18n";
import { useConnectionsStore } from "../stores/connections";
import { useSettingsStore } from "../stores/settings";
import { useToastStore } from "../stores/toast";
import { useUiStore } from "../stores/ui";
import type { ChatResponseDto } from "../sdk/types";

interface Turn {
  id: string;
  role: "user" | "agent";
  text: string;
  meta?: {
    mode: string;
    confidence: string;
    provider: string;
    skill: string | null;
  };
  proposal?: {
    kind: "create-skill" | "improve-skill";
    input?: string;
    skillId?: string;
    skillName?: string;
  };
}

const { t } = useI18n();
const connections = useConnectionsStore();
const settings = useSettingsStore();
const toast = useToastStore();
const ui = useUiStore();

const turns = ref<Turn[]>([]);
const input = ref("");
const busy = ref(false);
const scroller = ref<HTMLElement | null>(null);
const sessionId = ref(`studio-${Math.random().toString(36).slice(2, 10)}`);

const client = computed(() => connections.client);
const canSend = computed(() => client.value !== null && input.value.trim().length > 0 && !busy.value);

async function scrollToEnd(): Promise<void> {
  await nextTick();
  const el = scroller.value;
  if (el) el.scrollTop = el.scrollHeight;
}

/**
 * Reveals text progressively when the typing effect is on. Purely cosmetic —
 * the full reply is already in hand.
 */
function reveal(text: string): Promise<void> {
  if (!settings.data.typingEffect) return Promise.resolve();
  return new Promise((resolve) => {
    const turn = turns.value[turns.value.length - 1];
    if (!turn) return resolve();
    const step = Math.max(1, Math.ceil(text.length / 120));
    let i = 0;
    const tick = () => {
      i = Math.min(text.length, i + step);
      turn.text = text.slice(0, i);
      if (i < text.length) {
        setTimeout(tick, 16);
      } else {
        resolve();
      }
    };
    tick();
  });
}

async function send(): Promise<void> {
  if (!canSend.value || !client.value) return;
  const message = input.value.trim();
  input.value = "";
  busy.value = true;

  turns.value.push({ id: `u-${Date.now()}`, role: "user", text: message });
  const agentTurn: Turn = { id: `a-${Date.now()}`, role: "agent", text: "" };
  turns.value.push(agentTurn);
  await scrollToEnd();

  try {
    const res: ChatResponseDto = await client.value.chat(message, sessionId.value);

    await reveal(res.answer ?? "");

    agentTurn.meta = {
      mode: res.mode,
      confidence: res.confidence,
      provider: res.provider,
      skill: res.skill?.name ?? null,
    };

    if (res.proposeSkillForInput) {
      agentTurn.proposal = { kind: "create-skill", input: res.proposeSkillForInput };
    } else if (res.proposeImproveSkillId) {
      agentTurn.proposal = {
        kind: "improve-skill",
        skillId: res.proposeImproveSkillId,
        skillName: res.proposeImproveSkillName ?? res.proposeImproveSkillId,
      };
    }
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e);
    agentTurn.text = `${t("chat.error")}: ${msg}`;
    toast.error(`${t("chat.failed")}: ${msg}`);
  } finally {
    busy.value = false;
    await scrollToEnd();
  }
}

function onKeydown(e: KeyboardEvent): void {
  // Enter sends; Shift+Enter inserts a newline.
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    void send();
  }
}

function clear(): void {
  turns.value = [];
}

onMounted(scrollToEnd);
</script>

<template>
  <div class="flex flex-1 flex-col overflow-hidden bg-app">
    <!-- Transcript -->
    <div ref="scroller" class="flex-1 overflow-y-auto p-6">
      <div v-if="turns.length === 0" class="flex h-full items-center justify-center">
        <div class="max-w-md text-center">
          <h2 class="mb-2 text-xl font-semibold text-app">{{ t("chat.emptyTitle") }}</h2>
          <p class="text-sm text-secondary">{{ t("chat.emptyHint") }}</p>
        </div>
      </div>

      <div class="mx-auto max-w-3xl space-y-4">
        <div v-for="turn in turns" :key="turn.id">
          <!-- user -->
          <div v-if="turn.role === 'user'" class="flex justify-end">
            <div
              class="max-w-[80%] whitespace-pre-wrap rounded-2xl rounded-br-sm bg-tertiary px-4 py-2 text-sm text-app"
            >
              {{ turn.text }}
            </div>
          </div>

          <!-- agent -->
          <div v-else class="flex flex-col gap-1">
            <div class="max-w-[90%] whitespace-pre-wrap text-sm leading-relaxed text-app">
              <span v-if="turn.text">{{ turn.text }}</span>
              <span
                v-else-if="busy"
                class="inline-block h-4 w-1.5 animate-pulse bg-emerald-500 align-middle"
              />
            </div>

            <div v-if="turn.meta" class="flex flex-wrap items-center gap-2 text-[11px] text-secondary">
              <span class="rounded bg-tertiary px-1.5 py-0.5">{{ turn.meta.mode }}</span>
              <span class="rounded bg-tertiary px-1.5 py-0.5">
                {{ t("chat.confidence") }}: {{ turn.meta.confidence }}
              </span>
              <span class="rounded bg-tertiary px-1.5 py-0.5">{{ turn.meta.provider }}</span>
              <span v-if="turn.meta.skill" class="rounded bg-emerald-500/20 px-1.5 py-0.5 text-emerald-400">
                {{ turn.meta.skill }}
              </span>
            </div>

            <!-- agent proposals -->
            <div
              v-if="turn.proposal"
              class="mt-1 flex items-center gap-2 rounded-lg border border-app bg-secondary px-3 py-2 text-xs"
            >
              <span class="text-secondary">
                {{
                  turn.proposal.kind === "create-skill"
                    ? t("chat.proposeCreate")
                    : t("chat.proposeImprove", { name: turn.proposal.skillName ?? "" })
                }}
              </span>
              <button
                class="rounded bg-emerald-600 px-2 py-0.5 font-medium text-white hover:bg-emerald-500"
                @click="
                  turn.proposal.kind === 'improve-skill' && turn.proposal.skillId
                    ? ui.openSkill(turn.proposal.skillId)
                    : ui.select('skills')
                "
              >
                {{ t("chat.openSkills") }}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Composer -->
    <div class="border-t border-app p-4">
      <div class="mx-auto max-w-3xl">
        <div class="flex items-end gap-2">
          <textarea
            v-model="input"
            rows="2"
            :placeholder="t('chat.placeholder')"
            :disabled="!client"
            class="flex-1 resize-none rounded-xl border border-app bg-secondary px-4 py-3 text-sm text-app outline-none focus:border-emerald-500 disabled:opacity-50"
            @keydown="onKeydown"
          />
          <div class="flex flex-col gap-1">
            <button
              class="rounded-xl bg-emerald-600 px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-emerald-500 disabled:opacity-40"
              :disabled="!canSend"
              @click="send"
            >
              {{ busy ? t("common.saving") : t("chat.send") }}
            </button>
            <button
              class="rounded-lg px-3 py-1 text-xs text-secondary hover:bg-tertiary"
              :disabled="turns.length === 0"
              @click="clear"
            >
              {{ t("chat.clear") }}
            </button>
          </div>
        </div>
        <p class="mt-2 text-[11px] text-secondary">
          <template v-if="!client">{{ t("chat.noAgent") }}</template>
          <template v-else>{{ t("chat.hint") }}</template>
        </p>
      </div>
    </div>
  </div>
</template>