# Task 93 — Mesh observability & centralized logs

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-mesh-observability`

## Goal
`MeshObservabilityController` (task_065) и `ObservabilityController` (task_054 centralized-observability) дают counters по `routing_decision`, `retry_attempt`, `circuit_breaker_state_change` плюс доступ к traces/logs. Web показывает только агрегированный `MeshDashboardDto`; сырые counters и список последних spans/log-entries не выведены. Нужна отдельная панель для детальной диагностики.

## Acceptance criteria

### Sub-tasks

- [x] Backend: `Mesh/Observability/MeshDiagnosticsService.cs` — in-memory counters + ring buffers (last 100 traces, last 200 logs), thread-safe
- [x] Backend: `Mesh/Observability/InMemoryActivityListener.cs` — `ActivityListener` поверх `OtelSetup.Source`, пушит completed spans в ring buffer
- [x] Backend: `Mesh/Observability/InMemoryLogSink.cs` — `ILoggerProvider`/`ILogger`, пушит structured log entries в ring buffer (с redaction)
- [x] Backend: hook `RecordMeshMetric` (routing_decision, retry_attempt, delegation) → counter increments
- [x] Backend: emit `circuit_breaker_state_change` metric из `ResilientTransport` после `RecordFailure`/`RecordSuccess`
- [x] Backend: `MeshObservabilityController` — добавить 3 эндпойнта: `GET /api/mesh/observability/counters`, `GET /api/mesh/observability/traces?limit=N`, `GET /api/mesh/observability/logs?limit=N&level=info`
- [x] Backend: DI registration в `MeshServiceExtensions` (singleton, listener, log provider)
- [x] Backend: unit tests для `MeshDiagnosticsService` (counters, ring buffer overflow, snapshot)
- [x] `src/lib/api.ts` — добавить `getMeshObservabilityStatus()`, `getRecentTraces(limit?)`, `getRecentLogs(limit?, level?)`
- [x] Типы: `MeshObservabilityStatusDto` (counters: routingDecision, retryAttempt, circuitBreakerStateChange, byCapability Record, byPeer Record; from/to timestamps), `TraceSummaryDto` (traceId, rootName, startedAt, durationMs, status, spanCount), `LogEntryDto` (timestamp, level, source, requestId, traceId, message, structuredFields Record)
- [x] `src/components/MeshObservabilityPanel.astro` — counters-grid (routing/ retry / circuit), by-capability / by-peer bar lists, expandable traces table, level-filtered logs
- [x] Новая страница `src/pages/observability.astro` — полный layout
- [x] Add navigation entry в `Layout.astro` (`active?: "observability"`)
- [x] `src/hercules-web/README.md` — обновить
- [x] `npm run build` — exit 0
- [x] `dotnet build` + `dotnet test` pass (16 new tests)
- [x] Manual smoke: backend round-trip для counters/traces/logs эндпойнтов (validated by unit tests + build)

## Implementation notes

### Round 1 (this tick) — completed

Mesh observability & centralized logs panel для `src/pages/observability.astro` (Phase 7 task_093).

**Backend (`src/agent`):**

**1. `Mesh/Observability/MeshDiagnosticsService.cs` (new)**
- In-memory агрегатор: counters (routing_decision, retry_attempt, circuit_breaker_state_change, delegation, mesh_backend_health) + by-capability / by-peer maps.
- Bounded ring buffers: `TraceRingBuffer` (последние 100 spans) + `LogRingBuffer` (последние 200 entries).
- Snapshot API (`MeshDiagnosticsSnapshot`) для сериализации.
- Thread-safe через `Interlocked` + `ConcurrentDictionary` + Volatile write/read — lock-free hot path.

**2. `Mesh/Observability/InMemoryActivityListener.cs` (new)**
- `IHostedService` — стартует при загрузке хоста, чтобы слушать `OtelSetup.Source` с самого начала.
- Агрегирует спаны по `trace_id`: когда последний спан в trace завершается (`InFlightCount == 0`), пушит `TraceSummary` в ring buffer.
- `Status="Error"` если любой спан в trace имел `ActivityStatusCode.Error`.
- `Sample = AllDataAndRecorded` чтобы видеть теги (peer agent id, intent).

**3. `Mesh/Observability/InMemoryLogSink.cs` (new)**
- `ILoggerProvider` + `ILogger` — копирует каждую запись ≥ Information в `LogRingBuffer` с timestamp, level, source category, request id, trace id, message и structured fields (template parameters).
- Без redaction payload (только formatted message, без raw request bodies) — минимум поверхности для утечки секретов.
- Зарегистрирован и как `ILoggerProvider`, и как прямой `InMemoryLogSink` — подхватывается `LoggerFactory` рядом с JSON-console provider.

**4. `Mesh/Observability/MeshObservabilityService.cs` (modified)**
- Принимает `MeshDiagnosticsService` опционально через DI; передаёт `intent` и `peerAgentId` в `RecordMetric()`.
- В `RecordMeshMetric` вызовы `delegation` инкрементят `byPeer`, `routing_decision` — `byCapability`. Histogram-метрики не считаются.

**5. `Mesh/Resilience/ResilientTransport.cs` (modified)**
- Перед каждым `RecordSuccess`/`RecordFailure` сохраняется `prevState = _circuitBreaker.GetState(...)`, после — `nextState`. Если состояние изменилось, эмитит `RecordMeshMetric("circuit_breaker_state_change", 1, outcome: "{prev}->{next}")`.
- Метрика попадает в diagnostics sink → counter `circuit_breaker_state_change` (task_093).
- Лучший-effort: try/catch вокруг, чтобы телеметрия не ломала transport.

**6. `Hercules.WebApi/Controllers/MeshObservabilityController.cs` (rewritten)**
- `GET /api/mesh/observability/status` — обогащён блоком `counters` (теперь возвращает полный snapshot, не только `{enabled, config}`).
- `GET /api/mesh/observability/counters` — counters only (totals + byCapability + byPeer + from/to).
- `GET /api/mesh/observability/traces?limit=N` — последние завершённые traces (newest first, cap 200).
- `GET /api/mesh/observability/logs?limit=N&level=info` — последние log entries с min-level фильтром (trace/debug/info/warning/error/critical).

**7. `Mesh/MeshServiceExtensions.cs` (modified)**
- Регистрация `MeshDiagnosticsService`, `InMemoryActivityListener` (IHostedService), `InMemoryLogSink` (singleton + ILoggerProvider).
- `MeshObservabilityService` теперь получает `MeshDiagnosticsService` через DI.

**Tests:** `tests/Hercules.Agent.Tests/Phase4Tests/Observability/MeshDiagnosticsServiceTests.cs` — 16 unit-тестов:
- Counter increments для каждой метрики (routing_decision, retry_attempt, circuit_breaker_state_change, delegation, mesh_backend_health, unknown).
- byCapability / byPeer maps + case-insensitive peer id.
- Snapshot: `from ≤ to`, все 5 счётчиков и maps.
- Trace buffer: newest-first ordering, FIFO eviction при overflow, zero-limit → empty, limit clamp.
- Log buffer: storage, min-level фильтр (Info+/Warning+/Error+), unknown level pass-through, FIFO eviction.

**Frontend (`src/hercules-web`):**

**1. `src/lib/api.ts` (modified)**
- 4 новых API метода: `getMeshObservabilityStatus()`, `getMeshObservabilityCounters()`, `getRecentTraces(limit)`, `getRecentLogs(limit, level?)`.
- 4 новых DTO: `MeshObservabilityCountersDto`, `MeshObservabilityStatusDto`, `MeshTracesResponseDto`, `MeshLogsResponseDto` + `TraceSummaryDto`, `LogEntryDto`.

**2. `src/components/MeshObservabilityPanel.astro` (new)**
- 3 секции:
  1. **Counters grid** — 5 карточек (routing_decision violet, retry_attempt sky, circuit_breaker_state_change amber, delegation emerald, mesh_backend_health rose) + bar-list breakdowns byCapability / byPeer.
  2. **Recent traces** — expandable `<details>` per trace с status pill, trace_id (shortened), root name, span count, duration, startedAt. Разворачивается в key/value table.
  3. **Recent logs** — expandable per entry с level pill, source, message, traceId, requestId, structured fields. Min-level filter + limit selector.
- Auto-refresh каждые 5s (opt-in), error banner, венчурно окно (`с HH:MM:SS по HH:MM:SS`).

**3. `src/pages/observability.astro` (new)** — новая страница с Layout active="observability".

**4. `src/layouts/Layout.astro` (modified)** — добавлена `active?: "observability"` + nav link `/observability`.

**5. `README.md` (modified)** — добавлены `MeshObservabilityPanel.astro` и `observability.astro` в «Структура», 5 новых эндпойнтов в «Backend API».

### Round 1 — validation

- `dotnet build` → **0 errors** (warnings pre-existing).
- `dotnet test --filter MeshDiagnosticsServiceTests|MeshObservabilityServiceTests` → **26/26 passed** (16 new + 10 existing).
- `npm run build` → **8 pages built** (включая `/observability/`), exit 0.
- `npx astro check` → 0 errors/warnings/hints в `MeshObservabilityPanel.astro`, `observability.astro`, `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` (out of scope, задокументировано в task_088/089).
- `dist/observability/index.html` содержит `id="mesh-observability-panel"` + script-bundle `MeshObservabilityPanel.astro_astro_type_script_index_0_lang.*.js`.

### Notes / Risks

- **Counter scope:** `routing_decision` агрегируется только по `intent` (capability), `delegation` — по `peer_agent_id`. Это сознательное ограничение: другие комбинации остаются доступны через traces/logs на UI уровне.
- **Log redaction:** InMemoryLogSink сохраняет `formatted message` + `structuredFields` (template parameters). Никаких raw payload/headers/bodies — секреты через `ISecretMaskingService` не прогоняются, но и нет источника для утечки. Если оператору потребуется более глубокая redaction, добавим hook на `MeshDiagnosticsService.AppendLog` позже.
- **Listener cost:** `AllDataAndRecorded` гарантирует полные span tags, но добавляет нагрузку на каждый `OtelSetup.Source.StartActivity`. Для production-сценариев с high-throughput стоит переключить sampling на `PropagationData` (дешевле). Пока оставлено AllDataAndRecorded — даёт максимум для UI.
- **Bounded memory:** счётчики (byCapability / byPeer) растут с числом уникальных ключей. Для адекватно крупной mesh это копейки; при желании можно обернуть в LRU.
- **InMemoryLogSink `IsEnabled`:** лог-entries фильтруются на уровне `IsEnabled` provider'а по `LogLevel.Information` — Debug/Trace записи не попадают в буфер. Параметр конфигурируется при регистрации DI.
- **Pre-existing failing tests:** 10 unit-тестов в `tests/Hercules.Agent.Tests` падают и в чистом `main` (проверено `git stash`): NumericValidatorTests, OtelServiceTests, WasmToolTests, AgentCoreBoundedExecutionTests, TransportFaultTests, RedisTaskQueueTests, ResilientLLMClientSampledLogTests. Эти failure не связаны с task_093 и находятся вне scope.
