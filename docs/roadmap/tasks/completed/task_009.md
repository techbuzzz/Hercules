# Task 9 — Граница инструментов и policy engine

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `tool-boundary-policy`

## Goal
Локальные инструменты объявляют input/output схемы, side-effect level, required permission, timeout, retry, idempotency. ToolPolicy применяет allow/deny перед вызовом.

## Acceptance criteria

### Sub-tasks

- [x] `ToolPolicy.cs` — `SideEffectLevel` enum (None/Read/Local/External/Critical), `ToolPermission` flags enum, `ToolDescriptor` record (Name, InputSchema, OutputSchema, SideEffectLevel, RequiredPermissions, TimeoutSeconds, MaxRetries, Idempotent), `PolicyContext` record, `ToolPolicyResult` record with `Allowed/Denied/RequiresApproval/UnknownTool` decisions
- [x] `ToolPermissionSet.cs` — `Has/HasAll/Missing` checks, `ToHumanReadable()`, `ParseFromString()` extension methods, `Default` / `ReadOnly` static factories
- [x] `ToolPolicyEngine.cs` — `Register(ToolDescriptor)`, `Register(ITool)`, `Evaluate(PolicyContext)`; deny-list (glob patterns), side-effect approval threshold, permission check, dry-run mode, unknown-tool policy
- [x] `ITool` — добавлен `IToolDescriptorProvider` interface с `GetPolicyDescriptor()`; `ToolRegistry.Register` вызывает `_policy?.Register(tool)`
- [x] `AgentCore.RunWithToolsAsync` — вызывает `_policy.Evaluate()` перед `tool.ExecuteAsync()`; denied/approval-required tools возвращают user-friendly сообщение без выполнения
- [x] `AppConfig` — добавлена `ToolPolicyConfig` секция (DryRun, AllowUnknownTools, MinSideEffectLevelForApproval, DeniedTools, AgentPermissions, DefaultTimeoutSeconds)
- [x] DI registration в `Program.cs` (CLI + WebAPI): `ToolPermissionSet`, `ToolPolicyEngine`, `ToolPolicyConfig`
- [x] Unit tests: `ToolPolicyEngineTests.cs` — 18 тестов (registration, deny-list, unknown tools, dry-run, permissions, approval threshold)
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (389/389)

## Scope / Likely files
src/agent/Tools/, src/agent/Tools/Policy/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Сложные правила сложно отлаживать; нужен policy dry-run режим.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Tools/Policy/ToolPolicy.cs`** — типы для tool policy engine:
- `SideEffectLevel` enum: None(0), Read(1), Local(2), External(3), Critical(4)
- `ToolPermission` [Flags] enum: None, Read, Write, Delete, Network, Shell, Financial, Hardware, Memory
- `ToolDescriptor` record: Name, InputSchema, OutputSchema, SideEffectLevel, RequiredPermissions, TimeoutSeconds, MaxRetries, Idempotent
- `PolicyContext`: ToolName, ArgumentsJson, SessionId, ApprovedToolsThisSession
- `PolicyDecision` enum: Allowed, Denied, RequiresApproval, UnknownTool
- `ToolPolicyResult`: IsAllowed, IsDenied, RequiresApproval, DryRun, static factories

**`Tools/Policy/ToolPermissionSet.cs`** — permission management:
- `ToolPermissionSet`: granted permissions set, `Has/HasAll/Missing` checks
- `ToolPermissionExtensions`: `ToHumanReadable()`, `ParseFromString("Read|Write|Network")`

**`Tools/Policy/ToolPolicyEngine.cs`** — policy evaluation:
- `Register(ToolDescriptor)` — direct descriptor registration
- `Register(ITool)` — uses `IToolDescriptorProvider.GetPolicyDescriptor()` if available, otherwise infers from tool name
- `Evaluate(PolicyContext)` — deny-list → unknown-tool → side-effect threshold → permission check → critical check
- `IToolDescriptorProvider` — interface for tools that want to declare their own descriptor
- `DryRun` mode: only logs, never blocks
- `IConfigReload` implementation for runtime config updates

**`Config/AppConfig.cs`** — `ToolPolicyConfig` section:
- DryRun, AllowUnknownTools, MinSideEffectLevelForApproval, DeniedTools, AgentPermissions, DefaultTimeoutSeconds

**`Tools/ITool.cs`** — added `IToolDescriptorProvider` interface

**`Tools/ToolRegistry.cs`** — `_policy?.Register(tool)` after each tool registration

**`Agent/AgentCore.cs`** — policy check in `RunWithToolsAsync`:
- Before `tool.ExecuteAsync()`: evaluates `PolicyContext`
- Denied → returns user-friendly message, continues to next iteration
- RequiresApproval → returns message prompting user to confirm

**`Program.cs` (CLI + WebAPI)** — DI:
- `ToolPolicyConfig` singleton
- `ToolPermissionSet` (from AgentPermissions string)
- `ToolPolicyEngine` singleton
- `ToolRegistry` receives policy engine in constructor

**Tests** — `ToolPolicyEngineTests.cs`: 18 tests covering all policy scenarios

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 389/389 passed

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
