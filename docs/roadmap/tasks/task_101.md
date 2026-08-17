# Task 101 — SkillSdk (Hercules.SkillSdk NuGet)

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `skill-sdk`
**Studio Stage:** 6

## Goal
Создать NuGet-пакет `Hercules.SkillSdk` с интерфейсами для доступа C# file-based apps к инструментам агента: IHttpClient, IMcpClient, ILlmClient, IMemoryClient, ISkillLogger, ISessionContext. Whitelist approach — file-based apps используют только разрешённые API. См. PLAN-v2.md Stage 2.

## Acceptance criteria

### Sub-tasks
- [x] `src/agent/SkillSdk/` — новый проект `Hercules.SkillSdk.csproj` (class library, NuGet-упакованный)
- [x] Interfaces:
  - `IHttpClient` — REST calls, only allowed domains (from config Http.AllowedDomains)
  - `IMcpClient` — MCP tool calls (invoke MCP tools registered on agent)
  - `ILlmClient` — LLM chat completion (via agent's LLM providers)
  - `IMemoryClient` — read/write agent memory (working/durable/episodic)
  - `ISkillLogger` — structured logging (to agent's logger, visible in Studio)
  - `ISessionContext` — current session info (userId, sessionId, metadata, cancellationToken)
  - `IHerculesSkillContext` — aggregate: combines all above + config access (read-only)
- [x] `HerculesSkillContext` — implementation, injected into file-based app at execution
  - Created per execution, scoped to one skill run
  - Security: all methods enforce agent's policy (allowed domains, memory namespaces, tool permissions)
- [x] `SkillSdkExecutor` (PLAN-v2 Stage 2) — in-process compile + load via AssemblyLoadContext
  - App hosted in agent process (collectible `AssemblyLoadContext` + Roslyn)
  - Whitelist: only `Hercules.SkillSdk` + BCL safe namespaces (System, System.Collections, System.Linq, System.Threading.Tasks, System.Net.Http, System.Text, System.Text.Json, System.IO)
  - `DangerousCodeScanner` (blacklist) + `SkillSdkWhitelistScanner` (whitelist + forbidden namespaces) check code before execution
  - Code that references `Hercules.SkillSdk` is routed to `SkillSdkExecutor`; plain C# still goes to `DotnetFileBasedExecutor`
- [x] Template: `skill.code-execution.v1.md` updated to show SkillSdk usage
- [x] Unit tests: IHttpClient enforces allowed domains, IMemoryClient scoped, ISessionContext, executor rejects dangerous code, reference assemblies locatable
- [x] `dotnet build` + `dotnet test` pass

## Security
- File-based app НЕ может:
  - Доступ к FS вне sandbox dir
  - Запускать процессы
  - Сеть вне allowed domains
  - Доступ к config/keys агента
  - Использовать反射 для обхода ограничений
- DangerousCodeScanner = blacklist (regex patterns)
- SkillSdk = whitelist (only via interfaces)

## Dependencies
- PLAN-v2 Stage 2 (DotnetFileBasedExecutor, DangerousCodeScanner, SandboxOptions) — должен быть done

## Scope / Likely files
src/agent/SkillSdk/ (new project), src/agent/CodeExecution/DotnetFileBasedExecutor.cs, src/agent/Tools/skill.code-execution.v1.md

## Links
- PLAN-v2: [../../PLAN-v2.md](../../PLAN-v2.md) Stage 2
- Studio Stage 3: [../EPIC_Hercules_Studio/tasks/stage_03_skill_push.md](../EPIC_Hercules_Studio/tasks/stage_03_skill_push.md)
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)

## Done

- Status flipped to `done` on 2026-08-17.
- Implemented `Hercules.SkillSdk` NuGet-ready class library with whitelist interfaces (`IHttpClient`, `IMcpClient`, `ILlmClient`, `IMemoryClient`, `ISkillLogger`, `ISessionContext`, `IHerculesSkillContext`).
- Added agent-side adapters and `HerculesSkillContext` in `Hercules.CodeExecution`.
- Added `SkillSdkExecutor` for in-process execution via Roslyn + collectible `AssemblyLoadContext`, injecting `IHerculesSkillContext` into file-based C# skills.
- Integrated both `DotnetFileBasedExecutor` and `SkillSdkExecutor` into DI; `CodeExecutionTool` auto-routes SkillSdk code to the new executor.
- Updated `skill.code-execution.v1.md` template with SkillSdk examples and security notes.
- Added unit tests covering HTTP domain enforcement, memory scoping, session context, executor entry-point execution, and rejection of dangerous/reflection code.
- `dotnet build` passes for `Hercules`, `Hercules.WebApi`, and `Hercules.Agent.Tests`; all 13 new SkillSdk tests pass.