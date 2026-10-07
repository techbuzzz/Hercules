/**
 * Line diff for skill prompt revisions (Stage 2).
 *
 * Implemented here rather than pulled in as a dependency: the Studio bundle already
 * carries Monaco, and this needs to be a pure function so it can be unit-tested without
 * a DOM. `restore` and `improve` already write revisions, so comparing any two of them is
 * the missing piece — not the data.
 *
 * LCS-based so an unchanged run of lines is reported as context rather than as a
 * delete+insert pair; a naive line-by-line comparison on a prompt edit produces a diff
 * that reads as if the whole prompt changed.
 */

export type DiffKind = "context" | "added" | "removed";

export interface DiffLine {
  kind: DiffKind;
  /** 1-based line number in the left (older) text; null for added lines. */
  leftLine: number | null;
  /** 1-based line number in the right (newer) text; null for removed lines. */
  rightLine: number | null;
  text: string;
}

const MAX_LINES = 2000;

function split(text: string): string[] {
  return text.replace(/\r\n/g, "\n").split("\n");
}

/**
 * Longest common subsequence over lines, returned as pairs of matched indices.
 * Guarded by `MAX_LINES`: past that the quadratic table is not worth the memory on a
 * prompt of this size, and the caller is comparing human-authored text.
 */
function lcsPairs(a: string[], b: string[]): Array<[number, number]> {
  const n = a.length;
  const m = b.length;

  // Rows and cells are read through `?? 0` because a statically-sized 2-D array is
  // still `T | undefined` per index under `noUncheckedIndexedAccess`. Every slot is
  // filled below, so the fallback is unreachable — it just satisfies the checker
  // without scattering non-null assertions.
  const table: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    const row = table[i];
    const nextRow = table[i + 1];
    if (!row || !nextRow) continue;

    for (let j = m - 1; j >= 0; j--) {
      row[j] =
        a[i] === b[j]
          ? (nextRow[j + 1] ?? 0) + 1
          : Math.max(nextRow[j] ?? 0, row[j + 1] ?? 0);
    }
  }

  const pairs: Array<[number, number]> = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      pairs.push([i, j]);
      i++;
      j++;
    } else if ((table[i + 1]?.[j] ?? 0) >= (table[i]?.[j + 1] ?? 0)) {
      i++;
    } else {
      j++;
    }
  }
  return pairs;
}

/** True when the diff is too large to compute; callers render a "too different" notice. */
export function exceedsDiffLimit(left: string, right: string): boolean {
  return split(left).length > MAX_LINES || split(right).length > MAX_LINES;
}

export function diffLines(left: string, right: string): DiffLine[] {
  if (exceedsDiffLimit(left, right)) {
    return [
      { kind: "removed", leftLine: 1, rightLine: null, text: split(left).join("\n") },
      { kind: "added", leftLine: null, rightLine: 1, text: split(right).join("\n") },
    ];
  }

  const a = split(left);
  const b = split(right);
  const out: DiffLine[] = [];

  let i = 0;
  let j = 0;
  // `a[i]` / `b[j]` are guarded by the loop bounds above, but the checker cannot see
  // that, so the values are read once into locals the compiler can narrow.
  for (const [ai, bi] of lcsPairs(a, b)) {
    while (i < ai) {
      out.push({ kind: "removed", leftLine: i + 1, rightLine: null, text: a[i] ?? "" });
      i++;
    }
    while (j < bi) {
      out.push({ kind: "added", leftLine: null, rightLine: j + 1, text: b[j] ?? "" });
      j++;
    }
    out.push({ kind: "context", leftLine: i + 1, rightLine: j + 1, text: a[i] ?? "" });
    i++;
    j++;
  }
  while (i < a.length) {
    out.push({ kind: "removed", leftLine: i + 1, rightLine: null, text: a[i] ?? "" });
    i++;
  }
  while (j < b.length) {
    out.push({ kind: "added", leftLine: null, rightLine: j + 1, text: b[j] ?? "" });
    j++;
  }

  return out;
}

export interface DiffSummary {
  added: number;
  removed: number;
}

/** Counts real changes, so "identical prompts" is distinguishable from "no changes shown". */
export function summarize(lines: DiffLine[]): DiffSummary {
  return {
    added: lines.filter((l) => l.kind === "added").length,
    removed: lines.filter((l) => l.kind === "removed").length,
  };
}

/**
 * Collapses long runs of unchanged lines, keeping `context` lines around each hunk so a
 * one-line edit in a 300-line prompt does not render as 300 unchanged lines.
 */
export function collapseContext(lines: DiffLine[], context = 3): DiffLine[] {
  const keep = new Set<number>();
  lines.forEach((line, idx) => {
    if (line.kind === "context") return;
    for (let k = Math.max(0, idx - context); k <= Math.min(lines.length - 1, idx + context); k++) {
      keep.add(k);
    }
  });

  // Always keep the first and last line so the reader has framing.
  if (lines.length > 0) {
    keep.add(0);
    keep.add(lines.length - 1);
  }

  const out: DiffLine[] = [];
  let skipped = 0;
  lines.forEach((line, idx) => {
    if (keep.has(idx)) {
      if (skipped > 0) {
        out.push({ kind: "context", leftLine: null, rightLine: null, text: `⋯ ${skipped} unchanged line(s)` });
        skipped = 0;
      }
      out.push(line);
    } else {
      skipped++;
    }
  });
  return out;
}