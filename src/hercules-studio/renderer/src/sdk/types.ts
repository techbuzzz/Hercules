// SDK types — backend API contracts.
//
// Types the agent's OpenAPI document describes are DERIVED from `openapi.d.ts`
// rather than hand-written, so Studio cannot drift from the agent: a renamed or
// removed field becomes a typecheck error rather than a runtime surprise.
//
// `npm run generate:api` writes `openapi.d.ts` from
// `src/agent/Hercules.WebApi/openapi.json`, which the agent regenerates from the
// same provider that backs its runtime `/openapi/v1.json`:
//   dotnet build src/agent/Hercules.WebApi -p:OpenApiGenerateDocumentOnBuild=true
//
// `client.contract.test.ts` enforces the endpoint-level half of the same contract.
// Types below that are still interfaces are the documented exceptions, each with
// the reason it cannot yet be derived.

import type { components } from "./openapi";

// ---- Derived from the agent document ----

export type SkillDto = components["schemas"]["SkillDto"];
export type SkillDetailDto = components["schemas"]["SkillDetailDto"];
export type ChatResponseDto = components["schemas"]["ChatResponseDto"];
export type AgentConfigDto = components["schemas"]["AgentConfigDto"];
export type CodeRunStatusDto = components["schemas"]["CodeRunStatusDto"];

export type ToolDto = components["schemas"]["ToolSummaryDto"];
export type ToolLimitsDto = components["schemas"]["ToolLimitsDto"];
export type ToolsResponse = components["schemas"]["ToolsListResponseDto"];

export type ApprovalDto = components["schemas"]["ApprovalDto"];
export type ApprovalsResponse = components["schemas"]["PendingApprovalsResponseDto"];
export type EscalationDto = components["schemas"]["EscalationDto"];
export type EscalationsResponse = components["schemas"]["PendingEscalationsResponseDto"];

export type MeshHealthEntryDto = components["schemas"]["MeshHealthEntryDto"];
export type MeshHealthDto = components["schemas"]["MeshHealthDto"];
export type MeshPolicyDenialsDto = components["schemas"]["MeshPolicyDenialsDto"];

export type McpServerDefinitionDto = components["schemas"]["McpServerDefinitionDto"];
export type McpServerSummaryDto = components["schemas"]["McpServerSummaryDto"];
export type McpServersListResponseDto = components["schemas"]["McpServersListResponseDto"];
export type McpServerDetailDto = components["schemas"]["McpServerDetailDto"];
export type McpReloadResponseDto = components["schemas"]["McpReloadResponseDto"];

export type ApiKeySummaryDto = components["schemas"]["ApiKeySummaryDto"];
export type ApiKeysListResponseDto = components["schemas"]["ApiKeysListResponseDto"];
export type CreateApiKeyRequestDto = components["schemas"]["CreateApiKeyRequestDto"];
export type CreatedApiKeyResponseDto = components["schemas"]["CreatedApiKeyResponseDto"];
export type UpdateApiKeyRequestDto = components["schemas"]["UpdateApiKeyRequestDto"];
export type ApiKeyMutationResponseDto = components["schemas"]["ApiKeyMutationResponseDto"];
export type ApiKeyDeleteResponseDto = components["schemas"]["ApiKeyDeleteResponseDto"];

export type SkillPromptRevisionDto = components["schemas"]["SkillPromptRevisionDto"];
export type SkillPromptHistoryResponseDto = components["schemas"]["SkillPromptHistoryResponseDto"];

export type ContextBudgetDto = components["schemas"]["ContextBudgetDto"];
export type ContextSummaryDto = components["schemas"]["ContextSummaryDto"];
export type ContextDistillResultDto = components["schemas"]["ContextDistillResultDto"];

export type QuotaStatusDto = components["schemas"]["QuotaStatusDto"];
export type QuotaCountersDto = components["schemas"]["QuotaCountersDto"];
export type QuotaStatusResponseDto = components["schemas"]["QuotaStatusResponseDto"];

export type LlmConfigDto = components["schemas"]["LlmConfigDto"];

export type MeshRouteCandidateDto = components["schemas"]["MeshRouteCandidateDto"];
export type MeshRoutesResponseDto = components["schemas"]["MeshRoutesResponseDto"];

// ---- Not yet derivable ----

/**
 * GET /api/config. `AgentConfigDto.Config` is an open-ended dictionary (the live
 * configuration has no fixed member set), so this wrapper stays loose rather than
 * pretending the keys are known.
 */
export interface ConfigDto {
  config: Record<string, unknown>;
  source: string;
}

/**
 /**
 * A shared-memory fact as stored by `SharedMemorySync.GetLocalFacts()`.
 * Hand-written: the endpoint returns an untyped payload.
 */
export interface SharedFact {
  id: string;
  category?: string;
  content?: string;
  allowedAgents?: string[] | null;
  publishedAt?: string;
  ttlSeconds?: number | null;
  [key: string]: unknown;
}

export type ConsensusSessionDto = components["schemas"]["ConsensusSessionDto"];
export type ConsensusSessionListDto = components["schemas"]["ConsensusSessionListDto"];

export type CodeScanResponseDto = components["schemas"]["CodeScanResponseDto"];

/**
 * Mesh topology, derived from the OpenAPI document.
 *
 * `MeshDashboardDto` / `MeshTopologyDto` / `MeshAgentDto` used to be hand-written here
 * with `topology: unknown`, because the dashboard endpoint carried no response schema.
 * The endpoint is annotated now (Stage 4), so these come from `components["schemas"]`
 * and the Stage 4 canvas is typed end to end.
 *
 * Note `MeshAgentDto` describes the *dashboard topology* entry. The separate
 * `GET /api/mesh/agents` endpoint returns bare capability-registry records and still has
 * no named schema — hence `RegistryAgent` below stays hand-written.
 */
export type MeshAgentDto = components["schemas"]["MeshAgentDto"];
export type MeshTopologyDto = components["schemas"]["MeshTopologyDto"];
export type MeshDashboardDto = components["schemas"]["MeshDashboardDto"];

/**
 * An entry of GET /api/mesh/agents, which returns bare capability-registry records
 * inside a `{count, agents}` envelope with no named wrapper type.
 */
export interface RegistryAgent {
  agentId: string;
  displayName?: string;
  endpoint?: string;
  healthScore?: number;
  latencyMs?: number;
  trustLevel?: string;
  [key: string]: unknown;
}

/** GET /api/stats is still unannotated. */
export interface StatsDto {
  totalInteractions: number;
  skillBased: number;
  direct: number;
  successRate: number;
  totalSkills: number;
  byDay: { date: string; total: number; skill: number; direct: number }[];
}

/** The A2A agent card served at /agent.manifest.json — not part of /api/*, so unannotated. */
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