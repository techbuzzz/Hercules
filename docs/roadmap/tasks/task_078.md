# Task 78 — IHttpClientFactory adoption and standard resilience handlers

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** —
**Slug:** `httpclient-factory-adoption`

## Goal
`HttpClient` создаётся per-call в 8 местах → socket exhaustion под нагрузкой (TIME_WAIT linger). Дополнительно long-lived `new HttpClient` хранится в singleton-полях без DNS refresh. Mesh layer уже использует `IHttpClientFactory` правильно (`MeshServiceExtensions.cs:112,173,349`), но CLI `Program.cs` не регистрирует `AddHttpClient` для tools/detection. Нет Polly resilience handlers на HttpClient-level (`AddStandardResilienceHandler`).

Точки исправления:
- **Per-call `new HttpClient`:** `DegradationManager.cs:215`, `LMStudioClient.cs:54`, `ProviderHealthChecker.cs:73,123,173`, `ProviderCapabilityDetector.cs:67,129`
- **Long-lived `new HttpClient` в полях:** `HttpTool.cs:30`, `A2AClient.cs:27`, `HttpTransportAdapter.cs:41`, `GrpcTransportAdapter.cs:61`, `IntentTransport.cs:25`, `NetworkMonitor.cs:31`, `OperatorNotificationService.cs:25`, `SkillMarketplace.cs:245`

## Acceptance criteria
### Sub-tasks
- [x] `Program.cs` (CLI) — добавить `services.AddHttpClient();` (default factory) если ещё нет.
- [x] Конвертировать per-call `new HttpClient` → inject `IHttpClientFactory` через DI: `DegradationManager` (ping `google.com` удалён, теперь Degraded если NetworkMonitor не зарегистрирован), `LMStudioClient`, `ProviderHealthChecker`, `ProviderCapabilityDetector`.
- [x] Конвертировать long-lived `new HttpClient` в полях → inject `IHttpClientFactory` + `CreateClient("Named")` per-call: `HttpTool`, `A2AClient`, `HttpTransportAdapter` (legacy ctor через factory), `NetworkMonitor`, `OperatorNotificationService`, `SkillMarketplace`.
- [x] `IntentTransport` — оставлен legacy `new HttpClient` ctor для CLI-режима; добавлен `HttpClientName` const + `AddHttpClient<IntentTransport>` в `MeshServiceExtensions.cs:173` уже использует named-client pattern.
- [x] `DegradationManager.cs:215` — убрал hardcoded ping `google.com`; без зарегистрированного `NetworkMonitor` помечает network как `Degraded` (cannot probe) вместо синтетического ping.
- [x] Named clients: `llm-health` (5s timeout), `llm-capabilities` (8s), `lm-studio-info` (5s), `network-monitor` (config), `intent-transport`/`http-transport` (mesh timeout + resilience), `grpc-transport`, `http-tool` (HttpConfig timeout + resilience), `a2a-client` (A2AConfig timeout + resilience), `operator-notify` (10s + resilience), `skill-marketplace` (2 min).
- [x] Standard resilience handlers (`AddStandardResilienceHandler`) на named clients: `intent-transport` (full: retry + CB + timeout), `http-transport` (retry + timeout), `http-tool` (retry + timeout), `a2a-client` (retry + timeout), `operator-notify` (soft retry). Health/probe клиенты (`llm-health`, `llm-capabilities`, `lm-studio-info`, `network-monitor`, `grpc-transport`, `skill-marketplace`) — без resilience, best-effort.
- [x] `Hercules.csproj` — добавлен `Microsoft.Extensions.Http.Resilience` 10.0.0.
- [x] Конфигурация resilience через `appsettings.json`: новая секция `Http.Resilience` (`TimeoutSeconds`, `RetryCount`, `RetryBaseDelayMs`, `CircuitBreakerFailureRatio`, `CircuitBreakerSamplingDurationSeconds`, `CircuitBreakerMinimumThroughput`, `CircuitBreakerBreakDurationSeconds`).
- [x] `GrpcTransportAdapter.cs` — `HttpClientName = "grpc-transport"` const + HttpClient managed by factory when DI-wired. CLI legacy ctor создаёт собственный `SocketsHttpHandler` + `HttpClient` (gRPC-специфика: `EnableMultipleHttp2Connections`).
- [x] Unit-тест `tests/Hercules.Agent.Tests/Http/HttpClientFactoryTests.cs` — 13 тестов: HttpTool/A2AClient/NetworkMonitor/OperatorNotificationService/ProviderHealthChecker constructor with and without factory, named-client usage через mock factory, allow-list всё ещё работает, `NamedClientNames_AreUnique` (11 уникальных имён), resilience handler retry на 503 (2 attempts, 200 OK).
- [x] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Degradation/DegradationManager.cs, src/agent/LLM/Providers/LMStudioClient.cs, src/agent/LLM/ProviderHealthChecker.cs, src/agent/LLM/ProviderCapabilityDetector.cs, src/agent/LLM/LlmClientFactory.cs, src/agent/Tools/HttpTool.cs, src/agent/Tools/A2AClient.cs, src/agent/Mesh/Transport/HttpTransportAdapter.cs, src/agent/Mesh/Transport/GrpcTransportAdapter.cs, src/agent/Mesh/IntentTransport.cs, src/agent/Offline/NetworkMonitor.cs, src/agent/Degradation/OperatorNotificationService.cs, src/agent/Skills/SkillMarketplace.cs, src/agent/Config/AppConfig.cs, src/agent/Hercules.csproj, src/agent/appsettings.json, tests/Hercules.Agent.Tests/Http/HttpClientFactoryTests.cs (new)

## Dependencies
- блокирует / опирается на: [task_024 — tool-registry](task_024.md)
- блокирует / опирается на: [task_033 — a2a-agent-card](task_033.md)
- блокирует / опирается на: [task_061 — local-degradation](task_061.md)

## Risks / Rollback
`IHttpClientFactory` требует DI — CLI `Program.cs` должен использовать `Host` builder (уже использует). `AddStandardResilienceHandler` добавляет latency на retry; настроить timeout чтобы не превышать deadline. Rollback: вернуть `new HttpClient` (но socket exhaustion останется).

## Implementation notes

**`src/agent/Hercules.csproj`** — добавлен `Microsoft.Extensions.Http.Resilience` 10.0.0.

**`src/agent/Config/AppConfig.cs`** — добавлена `HttpResilienceConfig` (TimeoutSeconds, RetryCount, RetryBaseDelayMs, CircuitBreakerFailureRatio/SamplingDuration/MinimumThroughput/BreakDuration) + property `HttpConfig.Resilience` со default = `new HttpResilienceConfig()`.

**`src/agent/appsettings.json`** — добавлена секция `Http.Resilience` с дефолтами (TimeoutSeconds=30, RetryCount=3, CircuitBreakerFailureRatio=0.5 и т.д.).

**Named-client constants** (все уникальные, проверяется тестом `NamedClientNames_AreUnique`):
- `HttpTool.HttpClientName = "http-tool"`
- `A2AClient.HttpClientName = "a2a-client"`
- `NetworkMonitor.HttpClientName = "network-monitor"`
- `OperatorNotificationService.HttpClientName = "operator-notify"`
- `ProviderHealthChecker.HttpClientName = "llm-health"`
- `ProviderCapabilityDetector.HttpClientName = "llm-capabilities"`
- `LMStudioClient.HttpClientName = "lm-studio-info"`
- `IntentTransport.HttpClientName = "intent-transport"`
- `HttpTransportAdapter.HttpClientName = "http-transport"`
- `GrpcTransportAdapter.HttpClientName = "grpc-transport"`
- `SkillMarketplace.HttpClientName = "skill-marketplace"`

**Refactored classes** (все используют `IHttpClientFactory?` + legacy fallback):
- `HttpTool` — DI ctor `(HttpConfig, ILogger<HttpTool>, IHttpClientFactory?)`, legacy ctor сохранён. Per-call `using var client = CreateClient()`.
- `A2AClient` — DI ctor `(A2AConfig, IHttpClientFactory?)`, legacy ctor сохранён.
- `NetworkMonitor` — DI ctor `(OfflineSyncConfig, ILogger, IHttpClientFactory)`, legacy ctor сохранён. `_ownedHttp?.Dispose()`.
- `OperatorNotificationService` — DI ctor `(DegradationConfig, ILogger, IHttpClientFactory)`, legacy ctor сохранён.
- `ProviderHealthChecker` — DI ctor `(LlmConfig, ILogger?, IHttpClientFactory?)`, legacy ctor сохранён. Per-method `ResolveClient()`.
- `ProviderCapabilityDetector` — DI ctor `(LlmConfig, ILogger?, ICacheService?, IHttpClientFactory?)`, legacy ctor сохранён.
- `LMStudioClient` — DI ctor `(OpenAICompatibleConfig, ILogger?, IHttpClientFactory?)`, legacy ctor сохранён. Прокинут через `LlmClientFactory`.
- `LlmClientFactory` — DI ctor `(LlmConfig, ICacheService?, IHttpClientFactory?)`; создаёт `LMStudioClient` с factory.
- `HttpTransportAdapter` — DI-friendly `(ICapabilityLookup, int, IHttpClientFactory?)` legacy ctor, factory-aware.
- `IntentTransport`, `GrpcTransportAdapter` — добавлены `HttpClientName` const-ы (legacy ctors без изменений, gRPC-специфика).
- `SkillMarketplace` — DI ctor `(StorageConfig, SkillPackager, IMarketplaceSigningService?, IHttpClientFactory?)`. `ImportFromUrlAsync` использует `_httpFactory?.CreateClient(HttpClientName)`.
- `DegradationManager.CheckNetworkHealthAsync` — убран hardcoded `https://www.google.com` ping; теперь если `_networkMonitor == null` помечает network как `Degraded` ("cannot probe") вместо синтетического внешнего запроса.

**`src/agent/Program.cs` (CLI)** + **`src/agent/Hercules.WebApi/Program.cs`**:
- `services.AddHttpClient();` (default factory).
- Регистрации 11 named clients с timeout'ами из config + `AddStandardResilienceHandler(...)` для inter-agent / tool / operator-notify.
- DI registrations обновлены: `HttpTool`, `A2AClient`, `NetworkMonitor`, `OperatorNotificationService`, `SkillMarketplace`, `ProviderHealthChecker`, `ProviderCapabilityDetector` принимают `IHttpClientFactory` (через `sp.GetService<IHttpClientFactory>()` — optional).

**`tests/Hercules.Agent.Tests/Http/HttpClientFactoryTests.cs`** (new, 13 tests):
- `HttpTool_WithoutFactory_UsesLegacyClient` — constructor без factory.
- `HttpTool_WithFactory_UsesNamedClient` — mock factory → `factory.RequestedNames` содержит `"http-tool"`.
- `HttpTool_RespectsAllowList_EvenWithFactory` — allow-list проверяется до `CreateClient`.
- `A2AClient_WithoutFactory_Constructs` / `A2AClient_WithFactory_UsesNamedClient_UnknownAgentFailsBeforeCall`.
- `NetworkMonitor_WithoutFactory_Constructs` / `NetworkMonitor_WithFactory_UsesNamedClient_AndReports`.
- `OperatorNotificationService_WithoutFactory_Constructs` / `OperatorNotificationService_WithFactory_UsesNamedClient_DisabledDoesNotCall`.
- `ProviderHealthChecker_WithoutFactory_Constructs` / `ProviderHealthChecker_WithFactory_UsesNamedClient`.
- `NamedClientNames_AreUnique` — 11 уникальных имён.
- `HttpClient_WithStandardResilience_RetriesOnTransient503` — mock handler (503 → 200), `AddStandardResilienceHandler` срабатывает, attempts ∈ [2, 4].

## Validation
- `dotnet build src/agent/Hercules.csproj` — succeeded (0 errors, 15 pre-existing warnings).
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — succeeded (0 errors, 1 pre-existing warning).
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — succeeded (0 errors).
- `dotnet test --filter "FullyQualifiedName~HttpClientFactoryTests"` — 13/13 passed.
- `dotnet test --filter "FullyQualifiedName~ProviderHealthChecker|FullyQualifiedName~ProviderCapabilityDetector|FullyQualifiedName~LlmClientFactory|FullyQualifiedName~NetworkMonitor|FullyQualifiedName~Offline"` — 53/53 passed.
- `dotnet test --filter "FullyQualifiedName~HttpTool|FullyQualifiedName~A2A|FullyQualifiedName~Degradation|FullyQualifiedName~SkillMarketplace|FullyQualifiedName~OperatorNotification"` — 37/38 passed (1 failure = pre-existing `BudgetGuardTests.CheckAndGetDegradationMessage_WhenHardCapViolation_ReturnsMessage` — Russian text encoding environmental, не связано с правкой).
- Полный прогон: 1796 passed, 10 failed (все failures pre-existing environmental: `NumericValidatorTests`, `WasmToolTests`, `BudgetGuardTests`, `OtelServiceTests`, `RedisTaskQueueTests` — подтверждено в tasks 075/076/077).

## Commit
- `feat(roadmap): complete task 078 - IHttpClientFactory + standard resilience handlers`

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)