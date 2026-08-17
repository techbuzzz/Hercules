# Task 96 — Port migration 5000 → 8421

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `port-migration-8421`
**Studio Stage:** 1 (pre-PR, до старта Studio)

## Goal
Перенести дефолтный порт Hercules.WebApi с 5000 на 8421 (новый диапазон 8421-8521). См. [ADR-0003](../EPIC_Hercules_Studio/adr/0003-port-range-8421.md).

## Acceptance criteria

### Sub-tasks
- [x] `src/agent/Hercules.WebApi/Program.cs` — default URL `http://localhost:8421` (Development) / `http://0.0.0.0:8421` (Production); CORS dev-fallback origin 5000 → 8421; startup banner 5000 → 8421
- [x] `src/agent/appsettings.json` — `Mesh.Endpoint` 5000 → 8421
- [x] `src/agent/Config/AppConfig.cs` — `MeshConfig.Endpoint` default 5000 → 8421
- [x] `src/hercules-web/.env.example` — `PUBLIC_API_BASE=http://localhost:8421`
- [x] `src/hercules-web/src/lib/api.ts` — `API_BASE` default 5000 → 8421
- [x] `src/hercules-web/src/components/MeshRouterPanel.astro` — `API_BASE` default 5000 → 8421
- [x] `src/hercules-web/src/components/AgentCardPanel.astro` — `API_BASE` default 5000 → 8421
- [x] `src/hercules-web/README.md` — упоминания 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase3Tests/AgentManifestTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase3Tests/AgentCardServiceTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase3Tests/DiscoveryTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase3Tests/IntentEnvelopeTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase3Tests/CapabilityRegistryTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase4Tests/MeshRouterTests.cs` — порты 5000 → 8421
- [x] `tests/Hercules.Agent.Tests/Phase4Tests/SharedMemorySyncTests.cs` — порты 5000 → 8421
- [x] `docs/QUICKSTART-EN.md`, `QUICKSTART-RU.md` — упоминания 5000 → 8421
- [x] `README.md`, `README-RU.md` — упоминания 5000 → 8421
- [x] `docs/API-EN.md`, `docs/API-RU.md` — упоминания 5000 → 8421
- [x] `docs/AGENT-MESH-EN.md`, `docs/AGENT-MESH-RU.md` — упоминания 5000 → 8421
- [x] `docs/CONFIGURATION-EN.md`, `docs/CONFIGURATION-RU.md` — упоминания 5000 → 8421
- [x] `CHANGELOG-EN.md`, `CHANGELOG-RU.md` — breaking change note для port 5000 → 8421
- [x] `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors
- [x] `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj -c Debug` → 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~AgentManifest|FullyQualifiedName~AgentCard|FullyQualifiedName~Discovery|FullyQualifiedName~IntentEnvelope|FullyQualifiedName~CapabilityRegistry|FullyQualifiedName~MeshRouter|FullyQualifiedName~SharedMemorySync"` → 121/121 passed
- [x] `npm --prefix src/hercules-web run build` → exit 0, 7 pages built

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/appsettings.json, src/agent/Config/AppConfig.cs, src/hercules-web/.env.example, src/hercules-web/src/lib/api.ts, src/hercules-web/src/components/MeshRouterPanel.astro, src/hercules-web/src/components/AgentCardPanel.astro, src/hercules-web/README.md, tests/Hercules.Agent.Tests/Phase3Tests/*.cs, tests/Hercules.Agent.Tests/Phase4Tests/*.cs, docs/QUICKSTART-*.md, README*.md, docs/API-*.md, docs/AGENT-MESH-*.md, docs/CONFIGURATION-*.md, CHANGELOG-*.md

## Out of scope (НЕ менять)
- `src/hercules-studio/**` — Studio продолжает сканировать 5000 как legacy fallback (ADR-0003 §Legacy fallback)
- `docs/EPIC_Hercules_Studio/**` — design docs про Studio scanner, намеренно сохраняют 5000
- Любые `5000` как число (timeout ms, MaxLatencyMs, token budget, MakeReadings и т.п.) — это не порт
- `ADR-0003` — этот документ объясняет миграцию, должен сохранять ссылку на 5000
- `task_096.md` и `task_088.md` — исторические заметки про 5000
- `docs/roadmap/backlog.md:163` — описание задачи в таблице

## Implementation notes

### Round 1 (this tick) — completed

Port migration 5000 → 8421 для Hercules.WebApi, в полном соответствии с ADR-0003
(диапазон 8421-8521, мнемоника "степени двойки", 100 портов для fleet на одной машине).

**Что изменилось (28 файлов, +156/-89):**

- `src/agent/Hercules.WebApi/Program.cs` — `UseUrls("http://localhost:8421")` (Dev) /
  `UseUrls("http://0.0.0.0:8421")` (Production) при отсутствии явного override;
  CORS dev-fallback origin `localhost:5000/127.0.0.1:5000` → `…8421`; startup
  banner обновлён; header-комментарий ссылается на ADR-0003.
- `src/agent/appsettings.json:135` — `Mesh.Endpoint` `http://localhost:5000` → `:8421`.
- `src/agent/Config/AppConfig.cs:816` — `MeshConfig.Endpoint` default `…5000` → `…8421`.
- `src/hercules-web/.env.example:3` — `PUBLIC_API_BASE` `…5000` → `…8421`.
- `src/hercules-web/src/lib/api.ts:6` — `API_BASE` default `…5000` → `…8421`.
- `src/hercules-web/src/components/MeshRouterPanel.astro:64` — `API_BASE` default.
- `src/hercules-web/src/components/AgentCardPanel.astro:366` — `…5000` → `…8421` в fetch-URL.
- `src/hercules-web/README.md` — упоминания порта в Requirements + env table.
- **Backend tests** (Phase3 + Phase4, 7 файлов, ~70 вхождений `:5000`):
  `AgentManifestTests`, `AgentCardServiceTests`, `DiscoveryTests`,
  `IntentEnvelopeTests`, `CapabilityRegistryTests`, `MeshRouterTests`,
  `SharedMemorySyncTests`. Все `localhost:5000` / `peer1:5000` / `old:5000` /
  `192.168.1.100:5000` / `p1:5000` → соответствующие `:8421`. Везде где было
  equality-assertion на URL, обе стороны меняются одновременно, тесты остаются зелёными.
- **Документация:** `README.md`, `README-RU.md`, `docs/API-EN.md`, `docs/API-RU.md`,
  `docs/AGENT-MESH-EN.md`, `docs/AGENT-MESH-RU.md`, `docs/CONFIGURATION-EN.md`,
  `docs/CONFIGURATION-RU.md`, `docs/QUICKSTART-EN.md`, `docs/QUICKSTART-RU.md` —
  все `localhost:5000` / `port :5000` → `localhost:8421` / `port :8421`.
- `CHANGELOG-EN.md`, `CHANGELOG-RU.md` — новый bullet в `[Unreleased] ## Changed`:
  **BREAKING: Default agent port 5000 → 8421** с миграционным путём
  (`ASPNETCORE_URLS=http://localhost:8421` или override `Mesh.Endpoint`),
  ссылкой на ADR-0003 и причиной (Flask/Synology/UPnP/Syncthing конфликт).

**Что НЕ тронуто (out of scope, по ADR-0003):**

- `src/hercules-studio/**` — Studio продолжает сканировать 5000 как legacy fallback.
- `docs/EPIC_Hercules_Studio/**` — design docs про scanner, намеренно сохраняют 5000.
- Все числовые `5000` (timeout ms, MaxLatencyMs, token budget, PRAGMA busy_timeout,
  MakeReadings fixtures, MaxTokensPerDay) — это не порт.
- `ADR-0003` — это сам документ, объясняющий миграцию, должен сохранять ссылку на 5000.
- `task_096.md` (этот файл) и `task_088.md:23` — исторические ссылки про 5000.
- `docs/roadmap/backlog.md:163` — описание задачи в таблице.

### Round 1 (this tick) — validation

- `dotnet build src/agent/Hercules.csproj -c Debug` → **0 errors**, 15 pre-existing warnings
  (same baseline; не относятся к этой задаче).
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj -c Debug` → **0 errors**,
  1 pre-existing warning (CS0436 Program type conflict — задокументирован).
- `dotnet test --filter "FullyQualifiedName~AgentManifest|FullyQualifiedName~AgentCard|
  FullyQualifiedName~Discovery|FullyQualifiedName~IntentEnvelope|
  FullyQualifiedName~CapabilityRegistry|FullyQualifiedName~MeshRouter|
  FullyQualifiedName~SharedMemorySync"` → **121/121 passed**.
- Полный `dotnet test` → 1962/1971 passed. **8 pre-existing failures** (5× OtelServiceTests,
  2× NumericValidatorTests, 1× RedisTaskQueueTests.EnqueueAsync_StoresTaskMetadata),
  все задокументированы в task_085 notes. **Ни одной новой регрессии от task_096.**
- `npm --prefix src/hercules-web run build` → exit 0, 7 pages built в 1.23s.
  Проверено: ни `lib/api.ts`, ни `MeshRouterPanel.astro`, ни `AgentCardPanel.astro`
  не сломали TS-проверку / Astro build.
- `git status --short` → 28 modified files, ровно те которые в sub-tasks.

## Validation
- `dotnet build src/agent/Hercules.csproj -c Debug` → 0 errors
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj -c Debug` → 0 errors
- `dotnet test tests/Hercules.Agent.Tests` (targeted filter, 7 namespaces) → 121/121 passed
- `dotnet test tests/Hercules.Agent.Tests` (full) → 1962/1971 passed, 8 pre-existing failures (task_085), 0 new regressions
- `npm --prefix src/hercules-web run build` → exit 0, 7 pages built

## Dependencies
- нет

## Risks / Rollback
- **Breaking change:** существующие установки на 5000 должны мигрировать
  (через `ASPNETCORE_URLS` или override `Mesh.Endpoint`). Документировано в CHANGELOG.
- **Rollback:** revert single commit. Все изменения порта сосредоточены в 1 PR.
- **Studio:** продолжает сканировать 5000 как legacy fallback (ADR-0003), так что
  существующие Studio-установки продолжат работать с legacy-агентами.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
- ADR-0003: [../EPIC_Hercules_Studio/adr/0003-port-range-8421.md](../EPIC_Hercules_Studio/adr/0003-port-range-8421.md)

## Dependencies
- нет

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/appsettings.json, src/agent/Hercules.WebApi/appsettings.json, src/hercules-web/.env.example, docs/QUICKSTART-*.md

## Links
- ADR-0003: [../EPIC_Hercules_Studio/adr/0003-port-range-8421.md](../EPIC_Hercules_Studio/adr/0003-port-range-8421.md)
- Backlog: [../backlog.md](../backlog.md)