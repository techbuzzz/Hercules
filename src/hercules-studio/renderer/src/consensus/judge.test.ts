import { describe, expect, it } from "vitest";
import { buildJudgePrompt, parseJudgeVerdict } from "./judge";

describe("parseJudgeVerdict", () => {
  it("parses a clean JSON reply", () => {
    const v = parseJudgeVerdict('{"best_index": 2, "rationale": "more specific"}', 3);
    expect(v).toEqual({ bestIndex: 2, rationale: "more specific" });
  });

  it("parses JSON wrapped in a fence", () => {
    const v = parseJudgeVerdict('```json\n{"best_index": 1, "rationale": "clear"}\n```', 2);
    expect(v?.bestIndex).toBe(1);
  });

  it("parses JSON embedded in prose", () => {
    const v = parseJudgeVerdict(
      'After weighing both, my verdict is {"best_index": 3, "rationale": "cites sources"}. Thanks!',
      3,
    );
    expect(v).toEqual({ bestIndex: 3, rationale: "cites sources" });
  });

  it("accepts camelCase and string indices", () => {
    expect(parseJudgeVerdict('{"bestIndex": 2, "reason": "ok"}', 3)?.bestIndex).toBe(2);
    expect(parseJudgeVerdict('{"best_index": "1", "rationale": "x"}', 3)?.bestIndex).toBe(1);
  });

  it("tolerates a missing rationale", () => {
    const v = parseJudgeVerdict('{"best_index": 2}', 3);
    expect(v).toEqual({ bestIndex: 2, rationale: "" });
  });

  it("rejects an out-of-range index rather than picking something arbitrary", () => {
    expect(parseJudgeVerdict('{"best_index": 9}', 3)).toBeNull();
    expect(parseJudgeVerdict('{"best_index": 0}', 3)).toBeNull();
    expect(parseJudgeVerdict('{"best_index": -1}', 3)).toBeNull();
  });

  it("returns null for unusable replies so the caller can fall back", () => {
    expect(parseJudgeVerdict("the second one is best, obviously", 3)).toBeNull();
    expect(parseJudgeVerdict("", 3)).toBeNull();
    expect(parseJudgeVerdict('{"best_index": 1}', 0)).toBeNull();
  });

  it("does not choke on braces inside strings", () => {
    const v = parseJudgeVerdict('{"best_index": 1, "rationale": "use {\\"k\\": 1} here"}', 2);
    expect(v?.bestIndex).toBe(1);
  });
});

describe("buildJudgePrompt", () => {
  it("numbers answers and states the valid index range", () => {
    const prompt = buildJudgePrompt("ship on Friday?", [
      { agentName: "a", text: "hold" },
      { agentName: "b", text: "ship" },
    ]);
    expect(prompt).toContain("ship on Friday?");
    expect(prompt).toContain("[1: hold]");
    expect(prompt).toContain("[2: ship]");
    expect(prompt).toContain('"best_index"');
    expect(prompt).toContain("1-2");
  });
});