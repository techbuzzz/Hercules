# Task 97 — Dual API keys (contribute + system)

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `dual-api-keys`
**Studio Stage:** 1

## Goal
Заменить одиночный `WebApi:ApiKey` на массив `WebApi:ApiKeys` с ролями `contribute` и `system`. См. [ADR-0004](../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md).

## Acceptance criteria

### Sub-tasks
- [x] `WebApiConfig` — `ApiKeys: List<ApiKeyEntry>` где `ApiKeyEntry = {Key, Role}` (role: "contribute"|"system")
- [x] Backward compat: если `ApiKeys` пуст, fallback на `ApiKey` (string) как contribute role
- [x] `ApiKeyMiddleware` — проверяет ключ, определяет role, ставит `HttpContext.Items["ApiKeyRole"]` и `["ApiKeyEntry"]`
- [x] `RequireSystemRoleFilter` (IEndpointFilter) — endpoint-level 403 для system-only endpoints
- [x] Role-based authorization: применить filter к `PUT /api/config` (system-only); оставить hook для `POST /api/system/restart` (task_099) и force-checkout (task_098)
- [x] `ApiKeyStore` — load-or-generate `data/security/keys.json` (random keys, prefix `hc_contrib_` / `hc_sys_`)
- [x] `Program.cs` — зарегистрировать `ApiKeyStore`, применить сгенерированные ключи к `webCfg.ApiKeys`, напечатать оба в консоль
- [x] Unit tests: middleware role check, fallback compat, key generation, store load/save
- [x] `dotnet build` + `dotnet test` pass

## Role permissions
| Endpoint category | contribute | system |
|---|---|---|
| chat, skills CRUD, memory, stats, reflect | ✓ | ✓ |
| config GET, config PATCH (non-destructive) | ✓ | ✓ |
| mesh read, tools enable/disable, marketplace install | ✓ | ✓ |
| config PUT (full replace) | ✗ | ✓ |
| system restart, lifecycle destructive | ✗ | ✓ |
| MCP add/remove, quota changes, force checkout | ✗ | ✓ |

## Dependencies
- нет (можно делать параллельно с task_096)

## Scope / Likely files
src/agent/Hercules.WebApi/Config/WebApiConfig.cs, src/agent/Hercules.WebApi/Auth/ApiKeyMiddleware.cs, src/agent/Hercules.WebApi/Program.cs, src/agent/Config/AppConfig.cs

## Implementation notes

### Round 1 (this tick) — completed

Реализован dual API keys pipeline (ADR-0004):

**Config (`WebApiConfig.cs`):**
- `ApiKeyRole` enum: `Contribute` (оператор), `System` (админ).
- `ApiKeyEntry { Key, Role, Description }` — sealed class.
- `WebApiConfig.ApiKeys: List<ApiKeyEntry>` — новый список ключей.
- `WebApiConfig.ApiKey` — legacy single string (fallback, backward compat).

**Middleware (`ApiKeyMiddleware.cs`):**
- Поддерживает два пути: сконфигурированный список (через `IReadOnlyList<ApiKeyEntry>`) или `ApiKeyStore`.
- Lookup в `_expectedKeyBytes[]` таблице через `CryptographicOperations.FixedTimeEquals` (constant-time, защита от timing attacks).
- При совпадении ставит `HttpContext.Items["ApiKeyRole"]` = enum и `HttpContext.Items["ApiKeyEntry"]` = entry.
- Backward compat: при пустом списке — auth-bypass (как раньше), иначе — 401 при невалидном ключе.
- Bypass для OPTIONS (CORS preflight) и `/api/health*` — сохранено.

**Endpoint filter (`RequireSystemRoleFilter.cs`):**
- `IEndpointFilter` — 403 для contribute роли, 401 для отсутствующей роли (только в unit-тестах; в проде middleware всегда стоит раньше).
- Sugar-расширение `RouteHandlerBuilder.RequireSystemRole()` для одной строки на endpoint.

**Store (`ApiKeyStore.cs`):**
- `LoadOrGenerate(configured)` — если в конфиге есть ключи, используем их; иначе читаем `data/security/keys.json`; иначе генерируем пару и сохраняем.
- `Generate()` — пара ключей с криптостойкой энтропией (32 байта = 256 бит, Base64Url), префиксы `hc_contrib_` / `hc_sys_`.
- JSON-формат файла: `{ "apiKeys": [{ "key": "...", "role": "contribute" }, { "key": "...", "role": "system" }] }` (роль — строка, не int).
- Идемпотентен: не перегенерирует, если файл уже есть.

**Program.cs wiring:**
- Backward-compat: при пустых `ApiKeys` + непустом legacy `ApiKey` → создаём одну entry с role=contribute.
- `ApiKeyStore` зарегистрирован в DI как singleton.
- Сразу после `app.Build()` вызываем `LoadOrGenerate` и перезаписываем `webCfg.ApiKeys` — чтобы middleware видел финальный список при первом запросе.
- Консольный вывод: для каждого ключа — строка `🔑 X-Api-Key [contribute]: hc_contrib_...` (или `[system]`).
- Если файл `keys.json` существует — печатаем его путь для дебага.

**Endpoint enforcement:**
- `PUT /api/config` (ConfigController) — добавлен `.RequireSystemRole()` (full replace требует system).
- `PATCH /api/config` — обе роли (non-destructive).
- `POST /api/system/restart` (task_099) и force-checkout (task_098) — оставлены как хуки; в их endpoint'ах добавим `.RequireSystemRole()`.

**Tests (47 total в WebApi, 21 новых):**
- `ApiKeyMiddlewareTests` (9) — valid keys, wrong key, missing key, empty keys (bypass), health bypass, CORS preflight bypass, legacy compat, multiple keys order.
- `RequireSystemRoleFilterTests` (4) — system passes, contribute 403, no role 401, filter applies only to marked endpoint.
- `ApiKeyStoreTests` (8) — generate prefixes, randomness, load-or-generate persistence, configured keys take priority, fresh regen, JSON structure.

### Round 1 — validation

- `dotnet build` → **exit 0** (0 errors, только pre-existing warnings).
- `dotnet test --filter "FullyQualifiedName~WebApi"` → **47/47 passed** (включая 21 новых тестов).
- Полный test suite: 9 pre-existing failures (OtelService, RedisTaskQueue, WasmTool, NumericValidator, ResilientLLMClientSampledLog) — все они требуют внешних сервисов (OTLP collector, Redis, Wasmtime, LLM provider) и **не связаны с auth-изменениями** (нет ни одной ссылки на `ApiKey*`/`WebApiConfig` в этих тестах).
- Manual smoke не выполнялся (нельзя запустить `dotnet run` в cron-окружении); вместо этого — TestServer-based integration test для фильтра, который проверяет реальный HTTP-status code.

### Notes

- Legacy `WebApi:ApiKey` оставлен для backward compat: если кто-то ещё держит single-key конфиг, он автоматически становится contribute entry. Studio (когда подключится в task_098+) увидит обе роли через новый `WebApi:ApiKeys` список.
- `keys.json` создаётся в `DataRoot/security/`. `DataRoot` по умолчанию `data/` (рядом с `sessions.db`). Если хочется ротировать ключи — удалить файл и перезапустить.
- Для Studio: в `connections.ts` теперь нужно хранить ОБА ключа в safeStorage. У `mutator.ts` будет выбор per-request: для system-only endpoint'ов отправлять system-ключ, для остальных — contribute.

## Links
- ADR-0004: [../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md](../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md)
- Backlog: [../backlog.md](../backlog.md)