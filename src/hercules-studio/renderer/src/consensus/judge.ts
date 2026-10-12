/**
 * LLM-judge protocol (Stage 7.5).
 *
 * The task file specifies a *structured* contract: the judge is asked to return
 * `{best_index, rationale}` as JSON, the winning column is highlighted, and a judge
 * failure falls back to manual pick. Asking an LLM to "reply with the best answer
 * verbatim" instead loses the rationale and makes the winning column unknowable.
 *
 * Parsing is deliberately forgiving: models wrap JSON in prose and fences often enough
 * that a strict `JSON.parse` of the whole reply fails most of the time in practice.
 */

export interface JudgeVerdict {
  /** 1-based index into the candidate list, as the judge reported it. */
  bestIndex: number;
  rationale: string;
}

/**
 * Pulls `{best_index, rationale}` out of a judge reply.
 *
 * Returns null when nothing usable can be found — the caller then falls back to manual
 * pick rather than guessing, because a wrong "winner" presented as a verdict is worse
 * than no verdict.
 */
export function parseJudgeVerdict(
  raw: string,
  candidateCount: number,
): JudgeVerdict | null {
  if (!raw || candidateCount <= 0) return null;

  const candidates = extractJsonObjects(raw);
  for (const obj of candidates) {
    const index = coerceIndex(obj.best_index ?? obj.bestIndex ?? obj.index);
    if (index === null) continue;
    if (index < 1 || index > candidateCount) continue;

    const rationale = obj.rationale ?? obj.reason ?? obj.explanation;
    return {
      bestIndex: index,
      rationale: typeof rationale === "string" ? rationale.trim() : "",
    };
  }
  return null;
}

/**
 * Yields every balanced `{...}` span in the text, innermost-last-closed wins by position.
 * Handles fenced ```json blocks and bare objects embedded in prose.
 */
function extractJsonObjects(text: string): Record<string, unknown>[] {
  const found: Record<string, unknown>[] = [];

  for (let i = 0; i < text.length; i++) {
    if (text[i] !== "{") continue;

    let depth = 0;
    let inString = false;
    let escaped = false;

    for (let j = i; j < text.length; j++) {
      const ch = text[j];

      if (escaped) {
        escaped = false;
        continue;
      }
      if (ch === "\\") {
        escaped = true;
        continue;
      }
      if (ch === '"') {
        inString = !inString;
        continue;
      }
      if (inString) continue;

      if (ch === "{") depth++;
      else if (ch === "}") {
        depth--;
        if (depth === 0) {
          try {
            const parsed: unknown = JSON.parse(text.slice(i, j + 1));
            if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
              found.push(parsed as Record<string, unknown>);
            }
          } catch {
            // Malformed span — keep scanning for the next candidate.
          }
          break;
        }
      }
    }
  }
  return found;
}

function coerceIndex(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) return Math.round(value);
  if (typeof value === "string") {
    const m = value.match(/-?\d+/);
    if (m) return Number(m[0]);
  }
  return null;
}

/**
 * The judge prompt, per the Stage 7.5 contract. Kept here rather than inline so the
 * exact wire text is testable and cannot drift from the parser's expectations.
 */
export function buildJudgePrompt(
  question: string,
  answers: Array<{ agentName: string; text: string }>,
): string {
  const rendered = answers
    .map((a, i) => `[${i + 1}: ${a.text}]`)
    .join("\n");

  return (
    `You are a judge. Select the best answer to: '${question}'. ` +
    `Here are ${answers.length} responses:\n${rendered}\n` +
    `Return JSON: {"best_index": <1-${answers.length}>, "rationale": "<why>"}`
  );
}