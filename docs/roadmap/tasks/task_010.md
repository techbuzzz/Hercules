# Task 10 — Гейты подтверждения

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `approval-gates`

## Goal
Read-only операции идут автоматически; write/delete/network/shell/financial/hardware требуют policy-based подтверждения с показом proposed action оператору.

## Acceptance criteria

### Sub-tasks

- [x] `ApprovalRequest` record в `Storage/Models.cs` — Id, SessionId, ToolName, ArgumentsJson, PolicyDecision, Reason, RequestedAt, RequestedBy, Status (Pending/Approved/Denied/Expired), ApprovedAt, DeniedAt
- [x] `IApprovalService` / `ApprovalService` в `Tools/Approval/` — RequestAsync, Approve, Deny, GetPending, GetBySession, ExpireOld (configurable TTL, default 30 min)
- [x] `ApprovalService` — in-memory ConcurrentDictionary + SQLite persistence (table `approval_requests`)
- [x] `SqliteSessionStore` — Add tables `approval_requests` + methods SaveApprovalRequestAsync, GetPendingApprovalsAsync, UpdateApprovalStatusAsync, ExpireOldApprovalsAsync
- [x] `ToolPolicyEngine` — integrate `IApprovalService`: при RequiresApproval → вызывает `_approvalService.RequestAsync(...)` и возвращает NeedsApproval с request ID
- [x] `AgentCore.RunWithToolsAsync` — перед tool execution: проверяет `IApprovalService.IsApproved(toolName, sessionId)`; если approved — выполняет; если pending — возвращает "waiting for approval" сообщение
- [x] `ApprovalController.cs` — WebAPI endpoints: GET /api/approvals/pending, GET /api/approvals/{id}, POST /api/approvals/{id}/approve, POST /api/approvals/{id}/deny
- [x] `ConsoleUI` — интерактивный поток: команда /approvals list|approve {id}|deny {id}
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать `IApprovalService` в DI
- [x] `AppConfig` — добавить `ApprovalConfig` (Enabled, DefaultTtlMinutes=30, MaxPending=50)
- [x] Unit-тесты: `ApprovalServiceTests.cs` — 14 тестов: request, approve, deny, get pending, TTL expiry, session filtering, idempotency
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (406/406)

## Scope / Likely files
src/agent/Tools/Approval/, src/agent/Hercules.WebApi/Controllers/ApprovalController.cs

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)

## Risks / Rollback
UX-фрикция в CLI/Web при частых подтверждениях; батчинг и доверенные scopes.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Storage/Models.cs`** — `ApprovalRequest` record:
- Id, SessionId, ToolName, ArgumentsJson, PolicyDecision, Reason, RequestedAt, RequestedBy, Status, ApprovedAt, DeniedAt

**`Config/AppConfig.cs`** — `ApprovalConfig` + `AppConfig.Approval`:
- Enabled (default true), DefaultTtlMinutes=30, MaxPending=50

**`Storage/SqliteSessionStore.cs`** — новая таблица + методы:
- `approval_requests` table (id TEXT PK, session_id, tool_name, arguments_json, policy_decision, reason, requested_at, requested_by, status, approved_at, denied_at)
- `SaveApprovalRequestAsync`, `GetPendingApprovalsAsync`, `GetApprovalRequestsAsync`, `UpdateApprovalStatusAsync`, `ExpireOldApprovalsAsync`
- Indexes: ix_approval_session, ix_approval_status

**`Tools/Approval/ApprovalService.cs`** — `IApprovalService` + `ApprovalService`:
- In-memory `ConcurrentDictionary<string, ApprovalResult>` (O(1) lookup для IsApproved)
- Session-level index: `ConcurrentDictionary<string, ConcurrentDictionary<string, bool>>`
- Auto-approve when `ApprovalConfig.Enabled = false`
- `ApproveAsync` / `DenyAsync` — idempotent, returns false if already processed
- `IsApproved(toolName, sessionId)` — O(1) check для hot path в AgentCore
- `ExpireOldApprovalsAsync` — fire-and-forget TTL cleanup

**`Tools/Policy/ToolPolicyEngine.cs`** — интеграция:
- Конструктор принимает `IApprovalService?` (nullable, backward-compatible)
- При RequiresApproval: вызывает `_approvalService.RequestAsync(...)` и возвращает NeedsApproval с request ID
- При Critical tools: также регистрирует approval request

**`Agent/AgentCore.cs`** — интеграция:
- Конструктор принимает `IApprovalService?` (nullable, backward-compatible)
- В `RunWithToolsAsync`: после `policyResult.RequiresApproval` проверяет `_approvals?.IsApproved(toolName, SessionId)` — если approved, выполняет tool; иначе возвращает waiting message

**`Hercules.WebApi/Controllers/ApprovalController.cs`** — 5 endpoints:
- `GET /api/approvals/pending` — pending approvals (опционально: ?sessionId=xxx)
- `GET /api/approvals/{id}` — конкретный запрос
- `POST /api/approvals/{id}/approve` — одобрить
- `POST /api/approvals/{id}/deny` — отклонить
- `GET /api/approvals` — все (last 100)

**`CLI/ConsoleUI.cs`** — `/approvals` command:
- `/approvals list` — таблица pending-запросов
- `/approvals approve {id}` — одобрить
- `/approvals deny {id}` — отклонить

**`Program.cs` (CLI + WebAPI)** — DI:
- `ApprovalConfig` singleton
- `IApprovalService` → `ApprovalService`
- `ToolPolicyEngine` получает `IApprovalService` в конструктор

**Tests** — `tests/.../Tools/ApprovalServiceTests.cs` — 14 тестов:
- request, approve, deny, get pending, TTL expiry, session filtering, idempotency, disabled mode

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 406/406 passed

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
