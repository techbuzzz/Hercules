# Task 93 — Mesh observability & centralized logs

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-mesh-observability`

## Goal
`MeshObservabilityController` (task_065) и `ObservabilityController` (task_054 centralized-observability) дают counters по `routing_decision`, `retry_attempt`, `circuit_breaker_state_change` плюс доступ к traces/logs. Web показывает только агрегированный `MeshDashboardDto`; сырые counters и список последних spans/log-entries не выведены. Нужна отдельная панель для детальной диагностики.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `getMeshObservabilityStatus(): Promise<MeshObservabilityStatusDto>`, `getRecentTraces(limit?): Promise<TraceSummaryDto[]>`, `getRecentLogs(limit?, level?): Promise<LogEntryDto[]>`
- [ ] Типы: `MeshObservabilityStatusDto` (counters: routingDecision, retryAttempt, circuitBreakerStateChange, byCapability Record, byPeer Record; from/to timestamps), `TraceSummaryDto` (traceId, rootName, startedAt, durationMs, status, spanCount), `LogEntryDto` (timestamp, level, source, requestId, traceId, message, structuredFields Record)
- [ ] `src/components/MeshObservabilityPanel.astro` — counters-grid (routing/ retry / circuit), bar chart последних 24h counts, expandable table последних 50 traces, фильтр-селектор по level для logs
- [ ] Новая страница `src/pages/observability.astro` (или секция в mesh) — полный layout
- [ ] Add navigation entry в `Layout.astro` (`active?: "observability"`)
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: после нескольких chat + skill-use запросов — counters не нулевые; trace-таблица показывает реальные trace_id'шники, по клику — детали (если backend отдаёт)
