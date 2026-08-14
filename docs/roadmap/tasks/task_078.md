# Task 78 — IHttpClientFactory adoption and standard resilience handlers

**Phase:** 6
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `httpclient-factory-adoption`

## Goal
`HttpClient` создаётся per-call в 8 местах → socket exhaustion под нагрузкой (TIME_WAIT linger). Дополнительно long-lived `new HttpClient` хранится в singleton-полях без DNS refresh. Mesh layer уже использует `IHttpClientFactory` правильно (`MeshServiceExtensions.cs:112,173,349`), но CLI `Program.cs` не регистрирует `AddHttpClient` для tools/detection. Нет Polly resilience handlers на HttpClient-level (`AddStandardResilienceHandler`).

Точки исправления:
- **Per-call `new HttpClient`:** `DegradationManager.cs:215`, `LMStudioClient.cs:54`, `ProviderHealthChecker.cs:73,123,173`, `ProviderCapabilityDetector.cs:67,129`
- **Long-lived `new HttpClient` в полях:** `HttpTool.cs:30`, `A2AClient.cs:27`, `HttpTransportAdapter.cs:41`, `GrpcTransportAdapter.cs:61`, `IntentTransport.cs:25`, `NetworkMonitor.cs:31`, `OperatorNotificationService.cs:25`, `SkillMarketplace.cs:245`

## Acceptance criteria
### Sub-tasks
- [ ] `Program.cs` (CLI) — добавить `services.AddHttpClient();` (default factory) если ещё нет.
- [ ] Конвертировать per-call `new HttpClient` → inject `IHttpClientFactory` через DI: `DegradationManager`, `LMStudioClient`, `ProviderHealthChecker`, `ProviderCapabilityDetector`.
- [ ] Конвертировать long-lived `new HttpClient` в полях → inject `IHttpClientFactory` + `CreateClient("Named")` per-call (or per health-check cycle): `HttpTool`, `A2AClient`, `HttpTransportAdapter`, `NetworkMonitor`, `OperatorNotificationService`, `SkillMarketplace`.
- [ ] `IntentTransport` — уже использует `IHttpClientFactory` через `AddHttpClient<IntentTransport>` (`MeshServiceExtensions.cs:173`); проверить что `IntentTransport.cs:25` не создаёт дублирующий `new HttpClient`.
- [ ] `DegradationManager.cs:215` — убрать hardcoded ping `google.com`; использовать `NetworkMonitor` (уже injected) для network health.
- [ ] Добавить named clients: `AddHttpClient("llm-health", c => { c.Timeout = TimeSpan.FromSeconds(5); })`, `AddHttpClient("operator-notify", ...)`, etc.
- [ ] Добавить standard resilience handlers где уместно: `.AddStandardResilienceHandler()` (retry + circuit breaker + timeout) для `HttpTool`, `A2AClient`, `OperatorNotificationService`. Использовать `Microsoft.Extensions.Http.Resilience` package.
- [ ] `Hercules.csproj` — добавить `Microsoft.Extensions.Http.Resilience` если ещё нет.
- [ ] Конфигурация resilience через `appsettings.json`: `Http.Resilience.RetryCount`, `Http.Resilience.CircuitBreakerFailureRatio`, etc.
- [ ] `GrpcTransportAdapter.cs:61` — gRPC client не использует `HttpClient` напрямую; проверить что `GrpcChannel` создаётся через factory или корректно disposed.
- [ ] Unit-тест: `HttpTool` резолвится из DI с `IHttpClientFactory`; mock factory возвращает mock `HttpMessageHandler`.
- [ ] Unit-тест: resilience handler retry срабатывает на 503 (mock handler возвращает 503 twice then 200).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Program.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Degradation/DegradationManager.cs, src/agent/LLM/Providers/LMStudioClient.cs, src/agent/LLM/ProviderHealthChecker.cs, src/agent/LLM/ProviderCapabilityDetector.cs, src/agent/Tools/HttpTool.cs, src/agent/Tools/A2AClient.cs, src/agent/Mesh/Transport/HttpTransportAdapter.cs, src/agent/Offline/NetworkMonitor.cs, src/agent/Degradation/OperatorNotificationService.cs, src/agent/Skills/SkillMarketplace.cs, src/agent/Hercules.csproj

## Dependencies
- блокирует / опирается на: [task_024 — tool-registry](task_024.md)
- блокирует / опирается на: [task_033 — a2a-agent-card](task_033.md)
- блокирует / опирается на: [task_061 — local-degradation](task_061.md)

## Risks / Rollback
`IHttpClientFactory` требует DI — CLI `Program.cs` должен использовать `Host` builder (уже использует). `AddStandardResilienceHandler` добавляет latency на retry; настроить timeout чтобы не превышать deadline. Rollback: вернуть `new HttpClient` (но socket exhaustion останется).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)