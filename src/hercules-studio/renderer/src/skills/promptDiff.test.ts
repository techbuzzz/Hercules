import { describe, expect, it } from "vitest";
import {
  collapseContext,
  diffLines,
  exceedsDiffLimit,
  summarize,
} from "./promptDiff";

describe("prompt diff", () => {
  it("reports identical prompts as pure context", () => {
    const lines = diffLines("one\ntwo\nthree", "one\ntwo\nthree");
    expect(summarize(lines)).toEqual({ added: 0, removed: 0 });
    expect(lines.every((l) => l.kind === "context")).toBe(true);
  });

  it("detects a single changed line without flagging the rest", () => {
    const lines = diffLines("one\ntwo\nthree", "one\nTWO\nthree");
    const changes = summarize(lines);

    expect(changes).toEqual({ added: 1, removed: 1 });
    expect(lines.filter((l) => l.kind === "context")).toHaveLength(2);
  });

  it("detects added and removed lines", () => {
    const lines = diffLines("a\nb\nc", "a\nc\nd");
    const s = summarize(lines);

    expect(s.added).toBe(1);
    expect(s.removed).toBe(1);
  });

  it("uses the LCS so an unchanged run stays context", () => {
    // Naive line-by-line would report every line as changed once one line moves.
    const lines = diffLines("a\nb\nc\nd", "a\nb\nc\nd\ne");
    expect(summarize(lines)).toEqual({ added: 1, removed: 0 });
  });

  it("normalises CRLF so a Windows-authored prompt does not diff as fully replaced", () => {
    expect(summarize(diffLines("a\r\nb", "a\nb"))).toEqual({ added: 0, removed: 0 });
  });

  it("numbers lines on the side they belong to", () => {
    const lines = diffLines("a\nb", "a\nc");
    const removed = lines.find((l) => l.kind === "removed");
    const added = lines.find((l) => l.kind === "added");

    expect(removed?.leftLine).toBe(2);
    expect(removed?.rightLine).toBeNull();
    expect(added?.leftLine).toBeNull();
    expect(added?.rightLine).toBe(2);
  });

  it("handles an empty side", () => {
    expect(summarize(diffLines("", "a\nb")).added).toBe(2);
    expect(summarize(diffLines("a\nb", "")).removed).toBe(2);
  });

  it("collapses long unchanged runs but keeps a hunk with context", () => {
    const before = Array.from({ length: 40 }, (_, i) => `line ${i}`).join("\n");
    const after = before.replace("line 20", "line twenty");

    const collapsed = collapseContext(diffLines(before, after), 2);
    expect(collapsed.length).toBeLessThan(20);
    expect(collapsed.some((l) => l.text.includes("unchanged line"))).toBe(true);
    expect(collapsed.some((l) => l.kind === "added" && l.text === "line twenty")).toBe(true);
  });

  it("degrades instead of exploding on a very large prompt", () => {
    const huge = Array.from({ length: 2500 }, (_, i) => `l${i}`).join("\n");
    expect(exceedsDiffLimit(huge, "x")).toBe(true);

    const lines = diffLines(huge, "short");
    expect(summarize(lines).added).toBe(1);
    expect(summarize(lines).removed).toBe(1);
  });
});