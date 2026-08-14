// SDK types — DTOs ported from hercules-web/src/lib/api.ts
// All types are backend API contracts.

export interface SkillDto {
  id: string;
  name: string;
  description: string;
  triggers: string[];
  version: number;
  successRate: number;
  totalUses: number;
  createdAt: string;
}

export interface SkillDetailDto {
  meta: SkillDto;
  descriptionMarkdown: string;
  prompt: string;
}

export interface ChatResponseDto {
  answer: string;
  mode: string;
  confidence: string;
  provider: string;
  skill: SkillDto | null;
  proposeSkillForInput: string | null;
  proposeImproveSkillId: string | null;
  proposeImproveSkillName: string | null;
}

export interface StatsDto {
  totalInteractions: number;
  skillBased: number;
  direct: number;
  successRate: number;
  totalSkills: number;
  byDay: { date: string; total: number; skill: number; direct: number }[];
}

export interface ConfigDto {
  config: Record<string, unknown>;
  source: string;
}

export interface AgentManifest {
  agentId: string;
  version: string;
  displayName: string;
  description?: string;
  endpoint: string;
  transport: string;
  auth: string;
  capabilities: { name: string; description: string; phrase_receivers?: string[]; tools?: string[] }[];
  skills: {
    id: string;
    name: string;
    version: number;
    risk_level?: string;
    tools?: string[];
    phrase_receivers?: string[];
  }[];
  models?: { primary?: string; fallback?: string };
  health?: string;
  supported_protocol_versions?: string[];
}

// Mesh types
export interface MeshAgentDto {
  agentId: string;
  displayName: string;
  endpoint: string;
  healthScore: number;
  latencyMs: number;
  qualityScore: number;
  trustLevel: string;
  lastSeen: string | null;
  capabilities: string[];
}

export interface MeshHealthEntryDto {
  agentId: string;
  displayName: string;
  healthScore: number;
  healthStatus: string;
  circuitState: string;
  consecutiveFailures: number;
  lastSeen: string | null;
  avgLatencyMs: number;
}

export interface MeshHealthDto {
  agents: MeshHealthEntryDto[];
  healthyCount: number;
  degradedCount: number;
  unhealthyCount: number;
}

export interface MeshDashboardDto {
  topology: unknown;
  traffic: unknown;
  health: MeshHealthDto;
  policyDenials: unknown;
  skillHeatmap: unknown;
  evalSummary: unknown;
  generatedAt: string;
}