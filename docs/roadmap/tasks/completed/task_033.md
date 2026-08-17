# Task 33 — A2A Agent Card совместимость

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `a2a-agent-card`

## Goal
Hercules публикует и потребляет A2A-совместимую Agent Card с метаданными capabilities и skills, сохраняя нативный манифест для локальных deployment.

## Acceptance criteria

### Sub-tasks

- [x] `Mesh/A2A/IAgentCardService.cs` — интерфейс: `GetAgentCardAsync()` (из локального manifest), `ImportFromUrlAsync(url)` (fetch + parse remote Agent Card), `IsCurrent()` (проверка freshness)
- [x] `Mesh/A2A/AgentCard.cs` — A2A-совместимые типы: `AgentCard` record (spec fields: name, description, url, version, provider, capabilities, skills, defaultInputModes, defaultOutputModes, authentication, tags), `AgentCardCapabilities` (streaming, pushNotifications, stateTransitionReports), `AgentCardSkill` (id, name, description, tags, inputModes, outputModes), `A2AAuthentication` (schemes, credentials)
- [x] `Mesh/A2A/AgentCardService.cs` — реализация: `FromManifest(AgentManifest)` — конвертирует локальный `AgentManifest` в A2A `AgentCard`; `GetAgentCardAsync()` → локальный кард; `ImportFromUrlAsync(url)` → HTTP GET → десериализация + валидация; `IsCurrent()` → сравнение с кэшем
- [x] `Config/AppConfig.cs` — расширить `A2AConfig`: `AgentCard.Publish` (bool, default true), `AgentCard.Endpoint` (default "/agent-card.json"), `AgentCard.CacheTtlMinutes` (default 60), `Discovery.Endpoints` (список URL для автодискавери)
- [x] `Mesh/MeshServiceExtensions.cs` — зарегистрировать `IAgentCardService` → `AgentCardService` в DI
- [x] `WebApi/Program.cs` — при старте: сохранять `agent-card.json` если `A2A.AgentCard.Publish=true`; добавить middleware/endpoint для обслуживания `GET /agent-card.json`
- [x] `Hercules.WebApi/Controllers/A2AController.cs` — WebAPI endpoints: `GET /api/a2a/agent-card` (локальный кард), `POST /api/a2a/discover` (body: array of URLs → fetch + merge Agent Cards), `GET /api/a2a/agent-card/from?url=xxx` (fetch remote card)
- [x] CLI `/agent-card show|refresh|import <url>` — ConsoleUI команда для просмотра локального карда, рефреша из manifest, импорта remote card
- [x] `tests/.../Phase3Tests/AgentCardServiceTests.cs` — unit-тесты: FromManifest mapping, ImportFromUrl success/error, IsCurrent freshness, cache behaviour, malformed JSON handling
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (18 new tests pass; baseline: 891/898 + 7 pre-existing failures unchanged)

## Scope / Likely files
src/agent/Mesh/A2A/AgentCard.cs, src/agent/Mesh/A2A/AgentCardService.cs, src/agent/Mesh/A2A/IAgentCardService.cs, src/agent/Hercules.WebApi/Controllers/A2AController.cs, src/agent/CLI/ConsoleUI.cs, src/agent/Config/AppConfig.cs, src/agent/Mesh/MeshServiceExtensions.cs

## Dependencies
- блокирует / опирается на: [task_032 — agent-manifest](task_032.md)

## Implementation notes

### 2026-08-13

**Добавлено:**

**`src/agent/Mesh/A2A/`** — 3 новых файла:
- `IAgentCardService.cs` — интерфейс: GetAgentCardAsync, ImportFromUrlAsync, IsCurrent, PublishAsync, DiscoverAsync
- `AgentCard.cs` — A2A Agent Card типы: AgentCard, AgentCardCapabilities, A2AProvider, A2AAuthentication, AgentCardSkill
- `AgentCardService.cs` — реализация: FromManifest mapping, HTTP import с валидацией, in-memory cache с TTL, PublishAsync

**`src/agent/Config/AppConfig.cs`** — расширен `A2AConfig`:
- `A2AAgentCardConfig`: Publish (default true), Endpoint ("agent-card.json"), CacheTtlMinutes (60)
- `A2ADiscoveryConfig`: Endpoints (список URLs), AutoDiscover, RefreshIntervalMinutes

**`src/agent/Mesh/MeshServiceExtensions.cs`** — DI регистрация IAgentCardService → AgentCardService

**`src/agent/Hercules.WebApi/Controllers/A2AController.cs`** — 5 endpoints:
- `GET /api/a2a/agent-card` — локальный Agent Card
- `GET /api/a2a/agent-card/from?url=xxx` — импорт remote card
- `POST /api/a2a/discover` — bulk discover из списка URLs
- `POST /api/a2a/agent-card/publish` — publish
- `GET /api/a2a/agent-card/fresh` — freshness check

**`src/agent/Hercules.WebApi/Program.cs`** — startup:
- Публикация agent-card.json при старте если Publish=true
- `GET /agent-card.json` endpoint (A2A spec convention)

**`src/agent/CLI/ConsoleUI.cs`** — новые команды:
- `/agent-card show` — показать локальный Agent Card
- `/agent-card refresh` — обновить из manifest
- `/agent-card import <url>` — импортировать remote card
- Help entries добавлены

**Tests** — `tests/.../Phase3Tests/AgentCardServiceTests.cs` — 18 тестов:
- FromManifest: basic fields, skills, generatedAt, capabilities default, no-auth when type=none
- GetAgentCardAsync: generates from manifest, caching
- ImportFromUrlAsync: success, HTTP error, invalid JSON, missing name, empty URL
- PublishAsync: writes file, disabled config
- DiscoverAsync: collects multiple cards, handles failures
- IsCurrent: false when not cached, true after get, respects TTL

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.Agent.Tests.csproj — 0 errors
- `dotnet test --filter AgentCardService` — 18/18 passed
- `dotnet test` — 891/898 passed (7 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1)

## Risks / Rollback
A2A спецификация может дрейфовать; версионные адаптеры.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
