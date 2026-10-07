// View registry — extensible pattern for adding new views.
// Each view registers its component, sidebar content, and activity bar entry.
// New views only need to register here, no changes to ActivityBar/Sidebar/MainWorkbench.

import type { Component } from "vue";
import { markRaw } from "vue";

export interface ViewDefinition {
  id: string;
  labelKey: string;
  icon: string;
  component: () => Promise<Component>;
  order: number;
  requiresConnection?: boolean;
}

export const viewRegistry: ViewDefinition[] = [
  {
    id: "agents",
    labelKey: "activity.agents",
    icon: "M16 17l5-5v.01M21 12l-5-5M21 12H9M9 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2",
    component: () => import("../views/AgentList.vue").then((m) => markRaw(m.default)),
    order: 1,
    requiresConnection: false,
  },
  {
    id: "chat",
    labelKey: "activity.chat",
    icon: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z",
    component: () => import("../views/ChatView.vue").then((m) => markRaw(m.default)),
    order: 2,
    requiresConnection: true,
  },
  {
    id: "skills",
    labelKey: "activity.skills",
    icon: "M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5",
    component: () => import("../views/SkillsView.vue").then((m) => markRaw(m.default)),
    order: 3,
    requiresConnection: true,
  },
  {
    id: "mesh",
    labelKey: "activity.mesh",
    icon: "M12 2v20M2 12h20M5 5l14 14M19 5L5 19",
    component: () => import("../views/MeshView.vue").then((m) => markRaw(m.default)),
    order: 4,
    requiresConnection: true,
  },
  {
    id: "tools",
    labelKey: "activity.tools",
    icon: "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z",
    component: () => import("../views/ToolsView.vue").then((m) => markRaw(m.default)),
    order: 5,
    requiresConnection: true,
  },
  {
    id: "config",
    labelKey: "activity.config",
    icon: "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z",
    component: () => import("../views/ConfigView.vue").then((m) => markRaw(m.default)),
    order: 6,
    requiresConnection: true,
  },
  {
    id: "workflow",
    labelKey: "activity.workflow",
    icon: "M3 3h7v7H3zM14 3h7v7h-7zM14 14h7v7h-7zM3 14h7v7H3z",
    component: () => import("../views/WorkflowView.vue").then((m) => markRaw(m.default)),
    order: 7,
    // The workflow server is a separate service with its own credentials,
    // so this view is reachable without connecting an agent.
    requiresConnection: false,
  },
  {
    // Human-in-the-loop queue (approvals + escalations). Was registered as
    // "consensus" before Stage 7 was built, which collided with the multi-agent
    // consensus feature that now owns that name.
    id: "decisions",
    labelKey: "activity.decisions",
    icon: "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75",
    component: () => import("../views/DecisionsView.vue").then((m) => markRaw(m.default)),
    order: 8,
    requiresConnection: true,
  },
  {
    // Stage 7 consensus — fans a prompt out to several connected agents at once,
    // so it needs at least one live connection.
    id: "consensus",
    labelKey: "activity.consensus",
    icon: "M4 5h6v6H4zM14 5h6v6h-6zM4 13h6v6H4zM14 13h6v6h-6z",
    component: () => import("../views/ConsensusView.vue").then((m) => markRaw(m.default)),
    order: 9,
    requiresConnection: true,
  },
  {
    // Context talks to the agent (unlike Workflow, which has its own server),
    // so it keeps the connection requirement.
    id: "context",
    labelKey: "activity.context",
    icon: "M4 6h16M4 12h10M4 18h7",
    component: () => import("../views/ContextView.vue").then((m) => markRaw(m.default)),
    order: 10,
    requiresConnection: true,
  },
{
    id: "llm",
    labelKey: "activity.llm",
    icon: "M12 2a10 10 0 100 10 10 10 0 100-10zm0 4a6 6 0 100 6 6 6 0 100-6z",
    component: () => import("../views/LlmView.vue").then((m) => markRaw(m.default)),
    order: 11,
    requiresConnection: true,
  },
];

export function getView(viewId: string): ViewDefinition | undefined {
  return viewRegistry.find((v) => v.id === viewId);
}

export function getOrderedViews(): ViewDefinition[] {
  return [...viewRegistry].sort((a, b) => a.order - b.order);
}

export function registerView(view: ViewDefinition): void {
  const existing = viewRegistry.findIndex((v) => v.id === view.id);
  if (existing !== -1) {
    viewRegistry[existing] = view;
  } else {
    viewRegistry.push(view);
  }
}