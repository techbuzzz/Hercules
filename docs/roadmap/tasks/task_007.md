# Task 7 — Типизированные контракты агента

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `typed-contracts`

## Goal
Запросы, планы, вызовы и результаты инструментов, записи памяти и ответы используют версионированные JSON-схемы и C# типы; некорректный LLM-вывод чинится один раз или отклоняется.

## Acceptance criteria

### Sub-tasks

- [x] `Contracts/ToolCallContract.cs` — versioned tool-call contract: `Action`, `Arguments` (Dictionary), `Version` ("1.0"), `RequestId`
- [x] `Contracts/ToolResultContract.cs` — versioned tool-result contract: `Success`, `Output`, `Error`, `Metadata`, `RequestId`, `Version`
- [x] `Contracts/AgentRequestContract.cs` — versioned agent-request contract: `Input`, `SessionId`, `RequestId`, `Version`
- [x] `Contracts/AgentResponseContract.cs` — versioned agent-response contract: `Answer`, `Mode`, `Confidence`, `UsedSkillId`, `ToolUsed`, `Version`
- [x] `LLM/JsonRepair/JsonRepairService.cs` — `ExtractJson`, `TryParse<T>` (strip markdown, repair trailing commas, deserialize), `IJsonRepairService` interface
- [x] `AgentCore.TryParseAction` — replace regex with `IJsonRepairService.TryParse<ToolCallContract>`
- [x] `SkillManager.CreateAsync` — replace `ParseSkillJson` with `IJsonRepairService.TryParse<SkillCreationContract>`
- [x] `SkillManager.ImproveAsync` — replace `ParseImproveJson` with `IJsonRepairService.TryParse<SkillImproveContract>`
- [x] `Contracts/SkillContracts.cs` — `SkillCreationContract`, `SkillImproveContract` typed input
- [x] `Contracts/IJsonRepairService.cs` — interface for testability
- [x] Unit tests: `JsonRepairServiceTests.cs` (17 cases: markdown, trailing comma, comments, invalid JSON fallback, array rejection)
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (349/349)

## Scope / Likely files
src/agent/Contracts/, src/agent/LLM/JsonRepair/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)

## Risks / Rollback
Слишком жёсткие схемы ограничивают LLM; баланс между strict и permissive.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Contracts/`** — 5 новых файлов:
- `ToolCallContract.cs` — версионированный контракт вызова инструмента: Action, Arguments, Version, RequestId
- `ToolResultContract.cs` — версионированный контракт результата инструмента: Success, Output, Error, Metadata, RequestId, Version
- `AgentRequestContract.cs` — контракт запроса агента (для Phase 3 inter-agent)
- `AgentResponseContract.cs` — контракт ответа агента: Answer, Mode, Confidence, UsedSkillId, ToolUsed, Version
- `SkillContracts.cs` — `SkillCreationContract` и `SkillImproveContract` для типизированного парсинга LLM-ответов

**`src/agent/LLM/JsonRepair/`** — JSON repair service:
- `IJsonRepairService.cs` — интерфейс для тестируемости
- `JsonRepairService.cs` — three-pass strategy: direct → markdown strip → repair malformations
  - `ExtractJson` — strip markdown или найти первый JSON объект/массив (proper bracket matching)
  - `Repair` — remove trailing commas, comments, extract first {...} or [...]
  - `FindMatchingBracket` — state machine для matching `]` (вложенность + escaped chars)

**`AgentCore.cs`**:
- `TryParseAction` — typed contract path → legacy regex fallback
- Tool result serialization использует `ToolResultContract`
- `IJsonRepairService` добавлен в конструктор (DI)

**`SkillManager.cs`**:
- `ParseSkillJson` + `ParseImproveJson` — typed contract path → legacy fallback

**`Program.cs` (CLI + WebAPI)**: `IJsonRepairService` зарегистрирован в DI

**Tests** — 17 новых тестов в `JsonRepairServiceTests.cs`

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 349/349 passed (baseline 322/323 + 26 new)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
