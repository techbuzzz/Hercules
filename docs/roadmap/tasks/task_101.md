# Task 101 — SkillSdk (Hercules.SkillSdk NuGet)

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `skill-sdk`
**Studio Stage:** 6

## Goal
Создать NuGet-пакет `Hercules.SkillSdk` с интерфейсами для доступа C# file-based apps к инструментам агента: IHttpClient, IMcpClient, ILlmClient, IMemoryClient, ISkillLogger, ISessionContext. Whitelist approach — file-based apps используют только разрешённые API. См. PLAN-v2.md Stage 2.

## Acceptance criteria
- [ ] `src/agent/SkillSdk/` — новый проект `Hercules.SkillSdk.csproj` (class library, NuGet-упакованный)
- [ ] Interfaces:
  - `IHttpClient` — REST calls, only allowed domains (from config Http.AllowedDomains)
  - `IMcpClient` — MCP tool calls (invoke MCP tools registered on agent)
  - `ILlmClient` — LLM chat completion (via agent's LLM providers)
  - `IMemoryClient` — read/write agent memory (working/durable/episodic)
  - `ISkillLogger` — structured logging (to agent's logger, visible in Studio)
  - `ISessionContext` — current session info (userId, sessionId, metadata, cancellationToken)
  - `IHerculesSkillContext` — aggregate: combines all above + config access (read-only)
- [ ] `HerculesSkillContext` — implementation, injected into file-based app at execution
  - Created per execution, scoped to one skill run
  - Security: all methods enforce agent's policy (allowed domains, memory namespaces, tool permissions)
- [ ] `DotnetFileBasedExecutor` (PLAN-v2 Stage 2) — injects `IHerculesSkillContext` into file-based app
  - App hosted in agent process (sandbox AppDomain or assembly load)
  - Whitelist: only `Hercules.SkillSdk` + `System.Net.Http` (restricted) + BCL safe APIs
  - DangerousCodeScanner checks code before execution
- [ ] Template: `skill.code-execution.v1.md` updated to show SkillSdk usage
- [ ] Unit tests: IHttpClient enforces allowed domains, IMemoryClient scoped, ISessionContext
- [ ] `dotnet build` + `dotnet test` pass

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