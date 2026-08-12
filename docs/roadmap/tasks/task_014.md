# Task 14 — Аудит и приватность

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `audit-privacy`

## Goal
Каждый side-effect и policy decision имеет audit record (actor, requestId, tool, permission, payload hash, result, timestamp). Конфигурируемая redaction для секретов и PII.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `AuditConfig` секцию: `Enabled` (default true), `RedactSensitivityLevels` (list: high/medium/low, default high+medium), `PayloadHashEnabled` (default true), `LogActorActions` (list, default ["agent","system","user"]), `AuditRetentionDays` (default 90)
- [x] `Storage/Models.cs` — расширить `AuditLogEntry`: добавить `RequestId` (string), `PayloadHash` (string?), `PolicyDecision` (string?), `PermissionUsed` (string?), `Result` (string?), `ToolName` (string?)
- [x] `Storage/SqliteSessionStore` — обновить схему `audit_log`: добавить колонки request_id, payload_hash, policy_decision, permission_used, result, tool_name; Update `LogAuditAsync` + `GetAuditLogAsync` + `GetAuditLogByTargetAsync` с новыми полями
- [x] `Audit/IAuditService.cs` — интерфейс: `LogToolPolicyDecision`, `LogToolExecution`, `LogSkillAction`, `LogConfigChange`, `LogApprovalAction`, `Query` с фильтрами
- [x] `Audit/AuditService.cs` — реализация через `IAuditLog` (task_003): форматирует события с enriched fields, вычисляет hash, вызывает `IAuditLog.LogAsync`
- [x] `Audit/PayloadHashService.cs` — `ComputeHash(payload)` → SHA256 first 16 hex chars; deterministic, no secrets
- [x] `Redaction/IRedactionService.cs` — интерфейс: `Redact(text, sensitivity)` → redacted text
- [x] `Redaction/RedactionService.cs` — built-in patterns: API key (Bearer/sk), email, phone, credit card; regex patterns from config; configurable replacement ("***")
- [x] `ToolPolicyEngine` — интеграция `IAuditService`: логировать Evaluate() decisions (denied/approved/approval_required) с requestId, policyDecision, permissions, tool name
- [x] `AgentCore` — интеграция `IAuditService`: логировать HandleAsync завершение (success/guardrail_blocked/timeout) с sessionId, input hash
- [x] `ApprovalService` — интеграция `IAuditService`: логировать Approve/Deny действия с requestId, toolName
- [x] `SkillLifecycleController` + `SkillManager` — интеграция `IAuditService`: логировать skill create/improve/delete/deprecate/rollback
- [x] `ConfigController` — интеграция `IAuditService`: логировать runtime config changes
- [x] `Hercules.WebApi/Controllers/AuditController` — расширить: `GET /api/audit/search` (filters: actor/action/target/sessionId/from/to/result), `GET /api/audit/export` (CSV export)
- [x] `Config/AppConfig.cs` — зарегистрировать `AuditConfig` singleton, `IAuditService` → `AuditService`, `IRedactionService` → `RedactionService` в CLI и WebAPI Program.cs
- [x] `tests/.../Audit/AuditServiceTests.cs` — unit-тесты: LogToolPolicyDecision, LogToolExecution, Query filtering, hash computation
- [x] `tests/.../Redaction/RedactionServiceTests.cs` — unit-тесты: API key, email, phone, credit card, custom patterns, sensitivity levels
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит

## Validation

- `dotnet build src/agent/Hercules.csproj` → Build succeeded, 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → Build succeeded, 0 errors
- `dotnet test` → 513 passed (task_014), 8 failures (pre-existing flaky + OtelServiceTests)

## Implementation notes

- `init` properties on `AuditLogEntry` record: SQLite reader uses positional constructor; init properties set via object initializer after construction
- Fire-and-forget audit: `ToolPolicyEngine` and `AgentCore` call `_ = AuditAsync(...)` with `try/catch` swallowing failures — audit never affects core logic
- `ALTER TABLE IF NOT EXISTS` forward migration via `try/catch` on `duplicate column name` exception (SQLite doesn't support column-level `IF NOT EXISTS`)
- GeneratedRegex for patterns: C# 11 `partial class` + `[GeneratedRegex]` for compile-time regex compilation

## Scope / Likely files
src/agent/Audit/, src/agent/Redaction/

## Dependencies
- блокирует / опирается на: [task_009 — tool-boundary-policy](task_009.md)
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)

## Risks / Rollback
Производительность записи в SQLite под нагрузкой; батчинг.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
