import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { useConnectionsStore } from "./connections";
import type { Connection } from "../platform/capabilities";
import type { ChatResponseDto } from "../sdk/types";
import { buildJudgePrompt, parseJudgeVerdict } from "../consensus/judge";

/**
 * Stage 7 consensus — fan one prompt out to several agents and aggregate the answers.
 *
 * Deliberately backend-free (the Stage 7 task file's own dependency line: "нет —
 * Studio orchestrates parallel /api/chat"). Nothing here is persisted: a consensus
 * round is a transient investigation, and writing agent replies into browser storage
 * would leak answer content for no benefit.
 *
 * Failure policy: one agent failing must not lose the others' work. Each fan-out is
 * settled independently, so a partial round still renders and can still be aggregated
 * from whatever arrived.
 */

export type AggregationMode = "manual" | "llm-judge";
export type ConsensusStatus = "idle" | "querying" | "aggregating" | "done";

/** Per-agent outcome of one fan-out round. */
export interface ConsensusResponse {
  connectionId: string;
  agentName: string;
  status: "querying" | "done" | "error";
  response: ChatResponseDto | null;
  error: string | null;
}

/** Ceiling on fan-out width — the roadmap's default is 10. */
export const MAX_CONSENSUS_AGENTS = 10;
/** A consensus needs disagreement to be meaningful; one agent is just a chat. */
export const MIN_CONSENSUS_AGENTS = 2;

export const useConsensusStore = defineStore("consensus", () => {
  const connections = useConnectionsStore();

  const selectedIds = ref<string[]>([]);
  const prompt = ref("");
  const responses = ref<ConsensusResponse[]>([]);
  const aggregationMode = ref<AggregationMode>("manual");
  const judgeConnectionId = ref<string | null>(null);
  const aggregatedResult = ref<string | null>(null);
  /** Connection id the user marked as best in manual mode, or the judge's winner. */
  const pickedConnectionId = ref<string | null>(null);
  /** Judge's explanation for its pick (Stage 7.5). Empty when unjudged or hand-picked. */
  const judgeRationale = ref<string | null>(null);
  const status = ref<ConsensusStatus>("idle");

  const selected = computed<Connection[]>(() =>
    selectedIds.value
      .map((id) => connections.list.find((c) => c.id === id))
      .filter((c): c is Connection => c !== undefined),
  );

  const doneCount = computed(() => responses.value.filter((r) => r.status === "done").length);
  const errorCount = computed(() => responses.value.filter((r) => r.status === "error").length);

  /** True only when a round is actually runnable — drives the Send button. */
  const canSend = computed(
    () =>
      status.value === "idle" &&
      prompt.value.trim().length > 0 &&
      selected.value.length >= MIN_CONSENSUS_AGENTS,
  );

  /** Aggregation needs a finished round with at least one surviving answer. */
  const canAggregate = computed(() => status.value === "done" && doneCount.value > 0);

  function toggleAgent(id: string): void {
    if (status.value === "querying" || status.value === "aggregating") return;

    const at = selectedIds.value.indexOf(id);
    if (at >= 0) {
      selectedIds.value = selectedIds.value.filter((x) => x !== id);
      return;
    }

    if (selectedIds.value.length >= MAX_CONSENSUS_AGENTS) return;
    selectedIds.value = [...selectedIds.value, id];
  }

  function reset(): void {
    judgeRationale.value = null;
    selectedIds.value = [];
    prompt.value = "";
    responses.value = [];
    aggregationMode.value = "manual";
    judgeConnectionId.value = null;
    aggregatedResult.value = null;
    pickedConnectionId.value = null;
    status.value = "idle";
  }

  /** Clears a finished round but keeps the agent selection and prompt. */
  function clearRound(): void {
    judgeRationale.value = null;
    responses.value = [];
    aggregatedResult.value = null;
    pickedConnectionId.value = null;
    status.value = "idle";
  }

  /**
   * Sends the prompt to every selected agent in parallel.
   *
   * Each agent settles on its own: one rejection becomes an error entry and the rest
   * continue, which is why this is not a bare `Promise.all` — that would discard every
   * successful answer when a single agent is unreachable.
   */
  async function send(): Promise<void> {
    if (!canSend.value) return;

    const targets = selected.value;
    responses.value = targets.map((c) => ({
      connectionId: c.id,
      agentName: c.name,
      status: "querying" as const,
      response: null,
      error: null,
    }));
    aggregatedResult.value = null;
    pickedConnectionId.value = null;
    status.value = "querying";

    const text = prompt.value.trim();

    await Promise.all(
      targets.map(async (conn) => {
        const api = connections.clientFor(conn.id);
        if (!api) {
          patch(conn.id, { status: "error", error: "no active session for this agent" });
          return;
        }

        try {
          const res = await api.chat(text);
          patch(conn.id, { status: "done", response: res });
        } catch (e) {
          patch(conn.id, {
            status: "error",
            error: e instanceof Error ? e.message : String(e),
          });
        }
      }),
    );

    status.value = "done";
  }

  function patch(id: string, changes: Partial<ConsensusResponse>): void {
    const at = responses.value.findIndex((r) => r.connectionId === id);
    if (at < 0) return;
    responses.value = responses.value.map((r, i) => (i === at ? { ...r, ...changes } : r));
  }

  /** Manual mode: the operator picks the column they trust. */
  function aggregateManual(connectionId: string): void {
    const picked = responses.value.find(
      (r) => r.connectionId === connectionId && r.status === "done",
    );
    if (!picked?.response) return;

    pickedConnectionId.value = connectionId;
    // A hand-pick supersedes any earlier judge verdict; the two must not disagree.
    judgeRationale.value = null;
    aggregatedResult.value = picked.response.answer ?? "";
    status.value = "done";
  }

  /**
   * LLM-judge mode (Stage 7.5): one agent is asked to pick among the collected answers.
   *
   * Uses the task file's structured contract — the judge returns `{best_index, rationale}`
   * rather than the answer verbatim. That is what makes the winning column highlightable
   * and the rationale displayable; asking for prose loses both. If the judge fails or
   * returns something unusable, nothing is set and the view falls back to Manual pick —
   * presenting a guessed winner as a verdict would be worse than no verdict.
   */
  async function aggregateLlmJudge(judgeId?: string): Promise<void> {
    const id = judgeId ?? judgeConnectionId.value;
    if (!id) return;

    // flatMap narrows the response to a present value inside the callback, so the
    // material can be built without a non-null assertion on every entry.
    // connectionId rides along because the winner's column has to be highlighted.
    const answers = responses.value.flatMap((r) =>
      r.status === "done" && r.response
        ? [{ connectionId: r.connectionId, agentName: r.agentName, text: r.response.answer ?? "" }]
        : [],
    );
    if (answers.length === 0) return;

    const api = connections.clientFor(id);
    if (!api) return;

    status.value = "aggregating";
    judgeRationale.value = null;
    pickedConnectionId.value = null;

    try {
      const res = await api.chat(buildJudgePrompt(prompt.value.trim(), answers));

      const verdict = parseJudgeVerdict(res.answer ?? "", answers.length);
      if (!verdict) {
        // Unparseable judge output: leave the round in manual mode.
        status.value = "done";
        return;
      }

      // 1-based from the judge, 0-based here — the bounds check is in the parser.
      const winner = answers[verdict.bestIndex - 1];
      pickedConnectionId.value = winner.connectionId;
      judgeRationale.value = verdict.rationale;
      aggregatedResult.value = winner.text;
      status.value = "done";
    } catch (e) {
      // Keep the individual answers on screen — a failed judge is not a failed round.
      status.value = "done";
      throw e;
    }
  }

  function aggregatedByJudge(): boolean {
    return aggregationMode.value === "llm-judge" && aggregatedResult.value !== null;
  }

  return {
    selectedIds,
    prompt,
    responses,
    aggregationMode,
    judgeConnectionId,
    aggregatedResult,
    pickedConnectionId,
    judgeRationale,
    status,
    selected,
    doneCount,
    errorCount,
    canSend,
    canAggregate,
    toggleAgent,
    send,
    aggregateManual,
    aggregateLlmJudge,
    aggregatedByJudge,
    clearRound,
    reset,
  };
});