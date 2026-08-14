# Task 76 — ResilientLLMClient per-call provider/model

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `resilient-llm-per-call-provider`

## Goal
`ResilientLLMClient.ProviderName` и `ModelName` (`LLM/ResilientLLMClient.cs:43-45`) — plain `public string` instance properties с `private set`. Они записываются из `CompleteMainAsync` (`:151-152`), `InvokeRoleAsync` (`:211-212,239-240`), и streaming paths (`:290-291`). Concurrent запроси race на эти записи — last-writer-wins, и reader видит значение от другого запроса. Это портит tracing/metrics/audit (неправильный provider/model в логах и OTel tags).

## Acceptance criteria
### Sub-tasks
- [ ] Убрать `ProviderName`/`ModelName` instance properties из `ResilientLLMClient`.
- [ ] Добавить `Provider`/`Model` поля в `LlmResponse` (если ещё нет) и в streaming chunk metadata.
- [ ] `CompleteMainAsync` (`:143-160`) — записывать provider/model в OTel `Activity` tags и metrics **per-call** (локальные переменные), не в instance fields.
- [ ] `InvokeRoleAsync` — аналогично: provider/model из `client.ProviderName`/`client.ModelName` в локальную переменную, записать в response.
- [ ] Streaming path (`:290-291`) — provider/model в первый chunk metadata или в `StreamingResult` envelope.
- [ ] Найти всех callers которые читают `ResilientLLMClient.ProviderName`/`ModelName` — заменить на чтение из `LlmResponse.Provider`/`Model` или из `Activity` tags.
- [ ] `AgentCore` и другие callers: использовать `response.Provider`/`response.Model` для logging/metrics, не `client.ProviderName`.
- [ ] Если `ILLMClient` interface требует `ProviderName`/`ModelName` — оставить как read-only instance properties на concrete clients (`YandexGPTClient`, `LocalLLMClient`), убрать только с `ResilientLLMClient` wrapper.
- [ ] Unit-тест: 10 параллельных `CompleteAsync` через `ResilientLLMClient` с разными fallback chains — каждый response содержит правильный provider/model (не застрявший от другого запроса).
- [ ] Unit-тест: OTel `Activity` tags per-call содержат правильный provider (mock `IOtelService`).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/LLM/ResilientLLMClient.cs, src/agent/LLM/ILLMClient.cs, src/agent/LLM/LlmResponse.cs (or Models.cs), src/agent/Agent/AgentCore.cs, src/agent/Observability/OtelMetrics.cs

## Dependencies
- блокирует / опирается на: [task_004 — multi-provider-llm](task_004.md)
- блокирует / опирается на: [task_013 — opentelemetry](task_013.md)

## Risks / Rollback
Interface change (`LlmResponse` получает новые поля) — backward-compatible если поля optional/default. Rollback: вернуть instance properties (но race останется).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)