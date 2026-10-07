import { describe, expect, it } from "vitest";
import {
  emptyGraph,
  newNode,
  parseGraph,
  validateGraph,
  type WorkflowGraph,
} from "./graph";

/**
 * Stage 8 graph authoring. These rules are the last thing standing between an
 * operator and a definition the executor cannot walk, so they are worth pinning
 * independently of the UI.
 */

function graphOf(nodes: WorkflowGraph["nodes"], edges: WorkflowGraph["edges"] = []) {
  return { nodes, edges };
}

describe("workflow graph validation", () => {
  it("accepts the empty graph's start-only shape as incomplete but not broken", () => {
    // A lone StartNode is a legal (if useless) graph; only structural faults are errors.
    expect(validateGraph(emptyGraph())).toEqual([]);
  });

  it("rejects an empty graph", () => {
    expect(validateGraph({ nodes: [], edges: [] })).toHaveLength(1);
  });

  it("requires exactly one StartNode", () => {
    const none = graphOf([newNode("ServiceTaskNode", 1)]);
    expect(validateGraph(none).some((i) => /StartNode/.test(i.message))).toBe(true);

    const two = graphOf([newNode("StartNode", 1), newNode("StartNode", 2)]);
    const issues = validateGraph(two);
    expect(issues.some((i) => /only have one StartNode/.test(i.message))).toBe(true);
  });

  it("rejects duplicate node ids", () => {
    const a = newNode("ServiceTaskNode", 1);
    const b = { ...newNode("EndNode", 2), id: a.id };
    expect(validateGraph(graphOf([a, b])).some((i) => /Duplicate node id/.test(i.message))).toBe(
      true,
    );
  });

  it("rejects edges that point at unknown nodes", () => {
    const g = graphOf([newNode("StartNode", 1)], [{ from: "start", to: "ghost", label: "" }]);
    const issues = validateGraph(g);
    expect(issues.some((i) => /unknown node "ghost"/.test(i.message))).toBe(true);
  });

  it("rejects a self-loop", () => {
    const g = graphOf([newNode("StartNode", 1)], [{ from: "start", to: "start", label: "" }]);
    expect(validateGraph(g).some((i) => /points at itself/.test(i.message))).toBe(true);
  });

  it("requires an intent on service tasks and accepts valid JSON payloads", () => {
    const bare = newNode("ServiceTaskNode", 1) as WorkflowGraph["nodes"][number] & {
      intent: string;
      payload: string;
    };
    expect(validateGraph(graphOf([bare])).some((i) => /needs an intent/.test(i.message))).toBe(true);

    const good = { ...bare, intent: "deploy", payload: '{"env":"prod"}' };
    expect(validateGraph(graphOf([good])).filter((i) => /intent|payload/.test(i.message))).toEqual([]);
  });

  it("rejects a service task whose payload is not valid JSON", () => {
    const n = { ...newNode("ServiceTaskNode", 1), intent: "deploy", payload: "{oops" } as never;
    expect(validateGraph(graphOf([n])).some((i) => /not valid JSON/.test(i.message))).toBe(true);
  });

  it("requires an expression on conditionals and validates their branches", () => {
    const n = { ...newNode("ConditionalNode", 1), trueNext: "nowhere" } as never;
    const issues = validateGraph(graphOf([n]));
    expect(issues.some((i) => /needs an expression/.test(i.message))).toBe(true);
    expect(issues.some((i) => /true branch → unknown node "nowhere"/.test(i.message))).toBe(true);
  });

  it("accepts a realistic linear graph", () => {
    const g = graphOf(
      [
        { id: "start", type: "StartNode", label: "Start" } as const,
        {
          id: "task",
          type: "ServiceTaskNode",
          label: "Deploy",
          agentId: "hercules-main",
          intent: "deploy",
          payload: "{}",
          timeoutMs: 30000,
        } as const,
        { id: "done", type: "EndNode", label: "End" } as const,
      ],
      [
        { from: "start", to: "task", label: "" },
        { from: "task", to: "done", label: "" },
      ],
    );
    expect(validateGraph(g)).toEqual([]);
  });
});

describe("parseGraph", () => {
  it("reads the stored graph shape", () => {
    const parsed = parseGraph({
      nodes: [{ id: "start", type: "StartNode", label: "Start" }],
      edges: [{ from: "start", to: "x", label: "true" }],
    });
    expect(parsed.nodes).toHaveLength(1);
    expect(parsed.edges[0]).toEqual({ from: "start", to: "x", label: "true" });
  });

  it("degrades to an empty node list rather than throwing on junk", () => {
    expect(parseGraph(null).nodes).toEqual([]);
    expect(parseGraph("nonsense").edges).toEqual([]);
    expect(parseGraph({ nodes: 42 }).nodes).toEqual([]);
  });

  it("fills missing edge fields rather than producing undefined", () => {
    const parsed = parseGraph({ nodes: [{ id: "a", type: "StartNode" }], edges: [{ from: "a" }] });
    expect(parsed.edges[0]).toEqual({ from: "a", to: "", label: "" });
  });
});