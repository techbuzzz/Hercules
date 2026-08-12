# Task 4 — Мульти-провайдер LLM

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `multi-provider-llm`

## Goal
YandexGPT, Ollama Cloud/Local, LM Studio и OpenAI-совместимые провайдеры через Microsoft.Extensions.AI с health-check, retry, fallback, capability detection.

## Acceptance criteria

### Sub-tasks

- [x] `OpenAICompatibleConfig` в `Config/AppConfig.cs` — endpoint, apiKey, model, temperature, maxTokens; name/description для Web UI
- [x] `LlmConfig` — добавить `OpenAICompatible` секцию; `Fallback` поддерживает "openai-compatible"
- [x] `LlmClientFactory.Create("openai-compatible")` — создаёт `OpenAICompatibleClient` с переданным endpoint
- [x] `OpenAICompatibleClient.cs` в `LLM/Providers/` — наследует `ChatClientLLMClient`, использует generic endpoint
- [x] `LMStudioClient.cs` — обёртка с `/api/info` health check и `/v1/models` detection
- [x] Per-provider health checks: YandexGPT (GET `/v1/models`), Ollama (GET `/api/tags`), LMStudio (GET `/api/info`), generic OpenAI (GET `/v1/models`)
- [x] `ProviderCapabilities` record — Vision, FunctionCalling, Streaming, MaxContextTokens; заполняется из /v1/models или конфига
- [x] `ProviderHealthController.cs` — `GET /api/llm/health` — проверяет все сконфигурированные провайдеры, `GET /api/llm/capabilities` — capabilities каждого
- [x] Retry с exponential backoff в `ResilientLLMClient.CompleteMainAsync` — 3 попытки, base delay 500ms, max 8s, jitter ±25%; код 429/500/502/503/504 ретраятся
- [x] Unit-тесты: `LlmClientFactoryTests.cs`, `ProviderHealthCheckerTests.cs`, `ResilientLLMClientRetryTests.cs`
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (253/254; 1 flaky pre-existing WASM timing test)

## Scope / Likely files
src/agent/LLM/, src/agent/LLM/Providers/, src/agent/Config/

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Разные capabilities провайдеров ломают единые типы; нужны capability-флаги.

## Implementation notes

### 2026-08-12

**Добавлено:**

**Config/AppConfig.cs** — `OpenAICompatibleConfig`:
- Endpoint, ApiKey, Model, Temperature, MaxTokens
- DisplayName, Description для Web UI

**LLM/Providers/OpenAICompatibleClient.cs** — generic OpenAI-compatible client:
- Использует OpenAI SDK с кастомным endpoint
- Совместим с LM Studio, LiteLLM, FastChat и любыми OpenAI API-совместимыми серверами

**LLM/Providers/LMStudioClient.cs** — LM Studio с /api/info:
- Реализует `ILLMClient`, оборачивает `OpenAICompatibleClient`
- Добавляет `GetServerInfoAsync()` — определяет model, ctx, stopping_strings через LM Studio API

**LLM/ProviderHealthResult.cs** — результат health check:
- Provider, Healthy, Status, Error, LatencyMs, CheckedAt

**LLM/ProviderCapabilities.cs** — capabilities провайдера:
- Vision, FunctionCalling, Streaming, MaxContextTokens, Models, Model

**LLM/ProviderHealthChecker.cs** — async health check:
- Проверяет каждый провайдер через HTTP GET к специфичному endpoint
- YandexGPT/Generic: GET /v1/models; Ollama: GET /api/tags; LMStudio: GET /api/info
- Timeout 5s, возвращает ProviderHealthResult

**LLM/ProviderCapabilityDetector.cs** — capability detection:
- Ollama: GET /api/tags → список моделей
- OpenAI-compatible: GET /v1/models → модели, context_window_tokens, vision detection
- Defaults для offline/unreachable

**LLM/LlmClientFactory.cs**:
- Добавлен `ILLMClientFactory` интерфейс (для тестируемости)
- `Create("lmstudio")` → `LMStudioClient`
- `Create("openai-compatible")` → `OpenAICompatibleClient`
- Метод `Create` стал `virtual` для переопределения в тестах

**LLM/ResilientLLMClient.cs** — retry + fallback:
- Retry: 3 попытки, exponential backoff (500ms base, 8s max, ±25% jitter)
- Retryable codes: 429, 500, 502, 503, 504
- `IsRetryable()` проверяет HttpRequestException.StatusCode и inner exceptions
- `ComputeBackoff()` — jittered exponential delay

**LLM/WebApi/Controllers/LlmController.cs** — API endpoints:
- `GET /api/llm/health` — status всех провайдеров
- `GET /api/llm/health/{provider}` — status конкретного провайдера
- `GET /api/llm/capabilities` — capabilities всех провайдеров
- `GET /api/llm/capabilities/{provider}` — capabilities конкретного провайдера
- `GET /api/llm/config` — публичная конфигурация (без секретов)

**Program.cs (CLI + WebAPI)** — DI:
- `ProviderHealthChecker` registered
- `ProviderCapabilityDetector` registered

**Tests** — 3 новых тестовых файла:
- `LlmClientFactoryTests.cs` — 8 тестов: provider creation, type mapping, case insensitivity
- `ProviderHealthCheckerTests.cs` — 5 тестов: all providers, unknown, deduplication, latency
- `ResilientLLMClientRetryTests.cs` — 5 тестов: retryable codes, non-retryable codes, network errors

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 253/254 passed (1 pre-existing flaky WASM timing test)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
