/**
 * Workflow graph model for the Studio editor (Stage 8).
 *
 * The node set follows `docs/EPIC_Hercules_Studio/tasks/stage_08_workflow.md` and the
 * type names the workflow-server already anticipates in
 * `src/workflow-server/Models/WorkflowDefinition.cs` (`StartNode`, `ServiceTaskNode`,
 * `ConditionalNode`, …). This is the *authoring* shape written into the free-form
 * `graphJson`; task_105 still owns the formal executor-side schema, which is why
 * nothing here pretends the graph is executable — `run` is a 501 and the UI says so.
 *
 * A canvas (Stage 4, Vue Flow) is deliberately not assumed: this model is plain data,
 * so it can be rendered as a canvas later without reshaping what is stored.
 */

export type WorkflowNodeType =
  | "StartNode"
  | "ServiceTaskNode"
  | "ConditionalNode"
  | "UserTaskNode"
  | "EndNode";

export interface WorkflowNodeBase {
  id: string;
  type: WorkflowNodeType;
  label: string;
}

/** Calls an agent intent. `agentId` is advisory — the executor resolves it. */
export interface ServiceTaskNode extends WorkflowNodeBase {
  type: "ServiceTaskNode";
  agentId: string;
  intent: string;
  payload: string;
  timeoutMs: number;
}

/** `expression` is a simple `result.field == "value"` form; branches name the two successors. */
export interface ConditionalNode extends WorkflowNodeBase {
  type: "ConditionalNode";
  expression: string;
  trueNext: string;
  falseNext: string;
}

export interface UserTaskNode extends WorkflowNodeBase {
  type: "UserTaskNode";
  question: string;
}

/** Plain start/end — the payload differs only by the discriminator. */
export interface SimpleNode extends WorkflowNodeBase {
  type: "StartNode" | "EndNode";
}

export type WorkflowNode = ServiceTaskNode | ConditionalNode | UserTaskNode | SimpleNode;

export interface WorkflowEdge {
  from: string;
  to: string;
  /** "true" / "false" on conditional branches; empty otherwise. */
  label: string;
}

export interface WorkflowGraph {
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
}

export const NODE_TYPES: WorkflowNodeType[] = [
  "StartNode",
  "ServiceTaskNode",
  "ConditionalNode",
  "UserTaskNode",
  "EndNode",
];

export function emptyGraph(): WorkflowGraph {
  return {
    nodes: [{ id: "start", type: "StartNode", label: "Start" }],
    edges: [],
  };
}

export function newNode(type: WorkflowNodeType, seq: number): WorkflowNode {
  const id = `${type.toLowerCase()}-${seq}`;
  switch (type) {
    case "ServiceTaskNode":
      return {
        id,
        type,
        label: "Service task",
        agentId: "",
        intent: "",
        payload: "{}",
        timeoutMs: 30_000,
      };
    case "ConditionalNode":
      return {
        id,
        type,
        label: "Condition",
        expression: "",
        trueNext: "",
        falseNext: "",
      };
    case "UserTaskNode":
      return { id, type, label: "Ask a human", question: "" };
    default:
      return { id, type, label: type === "StartNode" ? "Start" : "End" };
  }
}

export interface GraphIssue {
  /** Node/edge id the issue belongs to, or null for graph-wide problems. */
  nodeId: string | null;
  message: string;
}

/**
 * Structural validation run before saving.
 *
 * Deliberately checks only what the editor itself can guarantee — the shape is
 * persisted as free-form JSON and task_105 defines the real execution semantics, so
 * inventing semantic rules here would reject graphs the executor may legitimately
 * accept.
 */
export function validateGraph(graph: WorkflowGraph): GraphIssue[] {
  const issues: GraphIssue[] = [];

  if (graph.nodes.length === 0) {
    issues.push({ nodeId: null, message: "Graph needs at least one node" });
    return issues;
  }

  const ids = new Set<string>();
  for (const node of graph.nodes) {
    if (!node.id.trim()) {
      issues.push({ nodeId: null, message: "Every node needs an id" });
      continue;
    }
    if (ids.has(node.id)) {
      issues.push({ nodeId: node.id, message: `Duplicate node id "${node.id}"` });
    }
    ids.add(node.id);
  }

  const starts = graph.nodes.filter((n) => n.type === "StartNode");
  if (starts.length === 0) {
    issues.push({ nodeId: null, message: "Graph needs a StartNode" });
  } else if (starts.length > 1) {
    const extra = starts[1];
    if (extra) {
      issues.push({ nodeId: extra.id, message: "A graph can only have one StartNode" });
    }
  }

  for (const edge of graph.edges) {
    if (!ids.has(edge.from)) {
      issues.push({ nodeId: edge.from, message: `Edge points from unknown node "${edge.from}"` });
    }
    if (!ids.has(edge.to)) {
      issues.push({ nodeId: edge.to, message: `Edge points to unknown node "${edge.to}"` });
    }
    if (edge.from === edge.to) {
      issues.push({ nodeId: edge.from, message: `Node "${edge.from}" points at itself` });
    }
  }

  for (const node of graph.nodes) {
    if (node.type !== "ServiceTaskNode") continue;
    if (!node.intent.trim()) {
      issues.push({ nodeId: node.id, message: `"${node.label}" needs an intent` });
    }
    if (node.payload.trim()) {
      try {
        JSON.parse(node.payload);
      } catch {
        issues.push({ nodeId: node.id, message: `"${node.label}" payload is not valid JSON` });
      }
    }
  }

  for (const node of graph.nodes) {
    if (node.type !== "ConditionalNode") continue;
    if (!node.expression.trim()) {
      issues.push({ nodeId: node.id, message: `"${node.label}" needs an expression` });
    }
    for (const [label, target] of [
      ["true", node.trueNext],
      ["false", node.falseNext],
    ] as const) {
      if (target && !ids.has(target)) {
        issues.push({ nodeId: node.id, message: `"${node.label}" ${label} branch → unknown node "${target}"` });
      }
    }
  }

  return issues;
}

/**
 * Structural read of a stored `graphJson`; anything unrecognised degrades to empty.
 *
 * Deliberately faithful rather than repairing: a stored graph with no nodes is
 * reported as such so `validateGraph` complains, instead of being quietly
 * backfilled with a StartNode that would make a broken definition look editable.
 */
export function parseGraph(value: unknown): WorkflowGraph {
  if (!value || typeof value !== "object") return { nodes: [], edges: [] };

  const raw = value as { nodes?: unknown; edges?: unknown };
  const nodes = Array.isArray(raw.nodes) ? (raw.nodes as WorkflowNode[]) : [];
  const edges = Array.isArray(raw.edges)
    ? (raw.edges as Array<Partial<WorkflowEdge>>).map((e) => ({
        from: String(e.from ?? ""),
        to: String(e.to ?? ""),
        label: String(e.label ?? ""),
      }))
    : [];

  return { nodes, edges };
}