# Task 100 — MCP hot-reload (IConfigReload)

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `mcp-hot-reload`
**Studio Stage:** 6

## Goal
Реализовать `McpClientService : IConfigReload`, чтобы изменения MCP-серверов через `PATCH /api/config` применялись без рестарта процесса. `POST /api/mcp/servers/reload` переустанавливает соединения с обновлённым конфигом.

## Acceptance criteria

### Sub-tasks
- [x] `IToolRegistryService` + `ToolRegistryService` — добавить `UnregisterEntry(string name)` для снятия MCP-инструментов с диспетчеризации при удалении сервера
- [x] `McpClientService` — заменить snapshot `McpConfig _config` на `RuntimeConfigStore _store`; реализовать `IConfigReload.Reload(AppConfig)` (sync trigger → background async apply)
- [x] `McpClientService.ReloadFromConfigAsync(McpConfig)` — diff-логика: новые сервера подключаются, удалённые — дисконнект + unregister tools, изменённые — reconnect с unregister старых tools
- [x] `McpClientService.ReloadAsync()` (публичный для `POST /api/mcp/servers/reload`) — использует `store.Current.Mcp` (живой конфиг, а не snapshot)
- [x] `Program.cs` — зарегистрировать `McpClientService` как `IConfigReload`-потребитель рядом с `AgentCore` / `SkillManager`
- [x] Unit-тесты `McpClientServiceTests` — обновить существующие под новый конструктор (RuntimeConfigStore + temp file); добавить 6 новых: adds / removes / updates / empty / IConfigReload-fire-and-forget / adds-with-registry
- [x] Unit-тесты `ToolRegistryServiceTests` — 4 новых для `UnregisterEntry` (existing / nonexistent / empty / case-insensitive)
- [x] `dotnet build` + `dotnet test` pass; никаких регрессий

## Dependencies
- нет (можно делать независимо, зависит только от текущей архитектуры)

## Scope / Likely files
src/agent/Mcp/McpClientService.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Tools/Registry/IToolRegistryService.cs, src/agent/Tools/Registry/ToolRegistryService.cs, tests/Hercules.Agent.Tests/Mcp/McpClientServiceTests.cs, tests/Hercules.Agent.Tests/Tools/Registry/ToolRegistryServiceTests.cs

## Implementation notes

### Round 1 (this tick) — completed

MCP hot-reload для `PATCH /api/config` (Phase 8 task_100, Studio Stage 6).

**Архитектурное изменение `McpClientService`:**
- Снапшот `McpConfig _config` в конструкторе заменён на `RuntimeConfigStore _store`. Сервис больше не владеет snapshot'ом — читает `store.Current.Mcp` лениво при `InitializeAsync` и `ReloadAsync`. Это разрывает связь «сервис живёт дольше, чем его config».
- `McpClientService : IConfigReload` — реализует `void Reload(AppConfig config)`. Поскольку `IConfigReload.Reload` синхронный, а сетевое подключение асинхронное, метод запускает фоновый `Task.Run(() => ReloadFromConfigAsync(config.Mcp, ct))` с try/catch + логированием ошибок. Reactor contract: fire-and-forget.
- `ReloadAsync(CancellationToken)` (публичный, для `POST /api/mcp/servers/reload`) использует `store.Current.Mcp` напрямую — awaitable, чтобы контроллер мог вернуть подтверждение оператору.

**Diff-логика `ReloadFromConfigAsync(McpConfig newConfig)`:**
1. Строим `Dictionary<string, McpServerConfig>` желаемого состояния по `Name`.
2. **Remove**: для каждого сервера в `_connections`, которого нет в `newConfig` — `TryRemove` + `DisposeAsync` (best effort) + `UnregisterServerTools(name)`.
3. **Update**: для каждого сервера, который есть и в `_connections`, и в `newConfig`, но `ServerConfigChanged(existing.Config, desired)` (сравниваем `Transport`, `Command`, `Endpoint`, `Enabled`, `HealthCheckEnabled`, `TimeoutSeconds`, `Args`) — disconnect + unregister tools + reconnect.
4. **Add**: для каждого сервера, который есть в `newConfig`, но не в `_connections` — `ConnectServerAsync`.
- Все операции сериализованы через `SemaphoreSlim _reloadLock` — параллельные PATCH-и + manual reload не race-ятся.
- `McpServerConnection` теперь хранит оригинальный `McpServerConfig` — нужен для `ServerConfigChanged`.

**Tool unregistration (новый API):**
- `IToolRegistryService.UnregisterEntry(string name) : bool` — case-insensitive `TryRemove` + лог. Используется `McpClientService.UnregisterServerTools(name)` для снятия всех entries с префиксом `mcp.{serverName}.`.

**DI wiring (`Program.cs`):**
- `builder.Services.AddSingleton<IConfigReload>(sp => sp.GetRequiredService<Hercules.Mcp.McpClientService>());` — рядом с `AgentCore` / `SkillManager`. RuntimeConfigReactor автоматически прокинет новый `AppConfig` в `McpClientService.Reload` при `Changed` event.
- `McpClientService` зарегистрирован как singleton без factory — DI инжектит `RuntimeConfigStore`, `ILoggerFactory`, `ILogger<McpClientService>`, optional `ToolRegistryService`.

**Тесты — 13 McpClientService + 4 ToolRegistryService = 17 новых/обновлённых:**
- `McpClientServiceTests` — все 7 существующих обновлены под новый конструктор (`RuntimeConfigStore` + temp file через `NewStore()` хелпер). 6 новых:
  - `ReloadAsync_AddsNewServer_ConnectsNewAndKeepsExisting` — diff: [A] → [A,B] даёт 2 connection.
  - `ReloadAsync_RemovesOldServer_DisconnectsAndUnregistersTools` — diff: [A,B] → [B] снимает connection A + unregister tools `mcp.a.*`; `mcp.b.*` остаются.
  - `ReloadAsync_UpdatesExistingServer_ReconnectsWithNewConfig` — изменение `Command` на существующем сервере вызывает reconnect.
  - `ReloadAsync_EmptyNewConfig_DisconnectsAll` — полный clear.
  - `IConfigReload_Reload_AppliesInBackground` — fire-and-forget path через `service.Reload(store.Current)` + poll-ожидание.
  - `ReloadAsync_AddsNewServer_RegistersItsTools` — single-server add через real `ToolRegistryService`.
- `ToolRegistryServiceTests` — 4 новых для `UnregisterEntry`: existing-tool / nonexistent / empty+null / case-insensitive.

### Round 1 — validation

- `dotnet build src/agent/Hercules.csproj` → **exit 0** (0 errors, 0 warnings).
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` → **exit 0** (0 errors, 0 warnings).
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` → **exit 0** (0 errors; pre-existing xUnit1031 warnings не относятся к task_100).
- `dotnet test --filter "FullyQualifiedName~McpClientService"` → **13/13 passed**.
- `dotnet test --filter "FullyQualifiedName~ToolRegistryService"` → **23/23 passed** (19 существующих + 4 новых).
- `dotnet test --filter "FullyQualifiedName~Mcp|FullyQualifiedName~ToolRegistry|FullyQualifiedName~RuntimeConfig|FullyQualifiedName~IConfigReload|FullyQualifiedName~ConfigReactor"` → **60/60 passed**.
- `dotnet test --filter "FullyQualifiedName~Mcp|FullyQualifiedName~ToolRegistry|FullyQualifiedName~RuntimeConfig|FullyQualifiedName~WebApi|FullyQualifiedName~CheckIn|FullyQualifiedName~Restart"` → **150/150 passed**.
- Полный suite: **2087/2097 passed**. 10 pre-existing failures (OtelService, RedisTaskQueue, WasmTool, NumericValidator, InProcessTaskQueue, ResilientLLMClientSampledLog) — все требуют внешних сервисов (OTLP collector, Redis, Wasmtime), задокументированы в task_097, **не связаны** с task_100 (нет ссылок на `Mcp*`/`IConfigReload`/`RuntimeConfig*` в их stack-traces).

### Notes
- `McpServerConfig` не реализует `IEquatable` — вместо этого используется field-by-field comparison в `ServerConfigChanged`. Покрывает все значимые поля (Transport, Command, Endpoint, Args, Enabled, HealthCheckEnabled, TimeoutSeconds). Если в будущем добавится новое поле, влияющее на соединение — нужно дополнить `ServerConfigChanged`.
- `IConfigReload.Reload` не await'ится — оператор, ждущий подтверждения, использует `POST /api/mcp/servers/reload` (awaitable). PATCH-путь сознательно fire-and-forget, потому что PATCH может менять много секций сразу и не должен блокироваться на сетевом reconnect.
- Существующий баг (вне scope): `McpClientService` регистрирует tools в `ToolRegistryService`, но `ToolRegistry` (базовый, для исполнения через `_tools.Get`) их не получает. Это pre-existing limitation — agent выполняет MCP tools через `ToolRegistryService` (?) или вообще не выполняет. Фиксить task_100 не должен; задокументировано в issue-tracking.
- Manual smoke (`dotnet run` + curl `PATCH /api/config` + `GET /api/mcp/servers`) не выполнялся в cron-окружении; покрыт 17 unit-тестами + 60 связанных тестов.

## Links
- Studio Stage 5: [../EPIC_Hercules_Studio/tasks/stage_05_tools_mcp.md](../EPIC_Hercules_Studio/tasks/stage_05_tools_mcp.md)
- Backlog: [../backlog.md](../backlog.md)