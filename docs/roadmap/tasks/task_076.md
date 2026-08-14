# Task 76 — ResilientLLMClient per-call provider/model

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `resilient-llm-per-call-provider`

## Goal
`ResilientLLMClient.ProviderName` и `ModelName` (`LLM/ResilientLLMClient.cs:43-45`) — plain `public string` instance properties с `private set`. Они записываются из `CompleteMainAsync` (`:151-152`), `InvokeRoleAsync` (`:211-212,239-240`), и streaming paths (`:290-291`). Concurrent запроси race на эти записи — last-writer-wins, и reader видит значение от другого запроса. Это портит tracing/metrics/audit (неправильный provider/model в логах и OTel tags).

## Acceptance criteria
### Sub-tasks
- [x] Убрать записи `ProviderName`/`ModelName` из `ResilientLLMClient.CompleteMainAsync`, `InvokeRoleAsync`, `StreamMainAsync`, `StreamSingleAsync` — instance properties остаются readonly (set только в ctor и `RebuildChain`).
- [x] `LlmResponse` уже содержит `Provider`/`Model` (`ChatModels.cs:24-28`) — propagate per-call provider/model через response, не через shared instance state.
- [x] `CompleteMainAsync` (`:143-160`) — OTel `Activity` tags и metrics используют локальные переменные (`client.ProviderName`/`client.ModelName`) и `resp.Provider`/`resp.Model` (per-call).
- [x] `InvokeRoleAsync` — локальные `client.ProviderName`/`client.ModelName` для логов/OTel; возвращает `resp` с правильным `Provider`/`Model`.
- [x] Streaming path (`:290-291`) — provider/model вычитываются из concrete client локально, логируются и кладутся в OTel `Activity` tags, **не** в instance fields. `IAsyncEnumerable<string>` contract не ломаем.
- [x] Найти всех callers которые читают `ResilientLLMClient.ProviderName`/`ModelName` — `grep` чист: ни одного читателя вне самого `ResilientLLMClient.cs` и тестов `LlmClientFactoryTests` (тесты читают с concrete client, не с wrapper).
- [x] `AgentCore` и другие callers — используют `response.Provider`/`response.Model` (через `LlmResponse`, не instance properties wrapper-а).
- [x] `ILLMClient` interface — `ProviderName`/`ModelName` остаются как read-only instance properties на concrete clients (`YandexGPTClient`, `LocalLLMClient`, `LMStudioClient`); на `ResilientLLMClient` они теперь отражают **настроенный primary** (set в ctor/`RebuildChain`), не per-call.
- [x] Unit-тест: 10 параллельных `CompleteAsync` через `ResilientLLMClient` с разными fallback chains — каждый response содержит правильный provider/model (не застрявший от другого запроса).
- [x] Unit-тест: OTel `Activity` tags per-call содержат правильный provider (mock `IOtelService`).
- [x] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/LLM/ResilientLLMClient.cs, src/agent/LLM/ILLMClient.cs, src/agent/LLM/LlmResponse.cs (or Models.cs), src/agent/Agent/AgentCore.cs, src/agent/Observability/OtelMetrics.cs

## Dependencies
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)

## Risks / Rollback
Interface change (`LlmResponse` получает новые поля) — backward-compatible если поля optional/default. Rollback: вернуть instance properties (но race останется).

## Implementation notes
- `ResilientLLMClient.ProviderName` / `ModelName` теперь derived from configured `LlmConfig.Provider` (set только в ctor и `Reload`/`RebuildChain`). Это устраняет per-call race, но также означает что wrapper больше **не трекает** фактически ответивший провайдер в instance state. Эта информация теперь доступна через `LlmResponse.Provider` / `LlmResponse.Model`, что и хочется.
- OTel tags (`llm.provider`, `llm.model`) устанавливаются **per-call** с использованием `client.ProviderName` / `client.ModelName` (concrete client) и обновляются фактическими значениями из `resp.Provider` / `resp.Model` после успешного вызова.
- Стрим: `IAsyncEnumerable<string>` interface contract сохранён; provider/model попадают в OTel `Activity` (создаётся при старте стрима и закрывается при завершении) и в `ILogger` на первом chunk-е. Никакого shared state.
- `LlmResponse.Provider`/`Model` уже корректно заполняются в `ChatClientLLMClient.CompleteAsync` (`:35`) — используем их как source of truth.

## Validation
- `dotnet build src/agent/Hercules.csproj` — passes.
- `dotnet test tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj --filter "FullyQualifiedName~ResilientLLMClient"` — passes (новые тесты + старые retry-тесты).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
