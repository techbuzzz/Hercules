# Task 97 — Dual API keys (contribute + system)

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `dual-api-keys`
**Studio Stage:** 1

## Goal
Заменить одиночный `WebApi:ApiKey` на массив `WebApi:ApiKeys` с ролями `contribute` и `system`. См. [ADR-0004](../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md).

## Acceptance criteria
- [ ] `WebApiConfig` — `ApiKeys: List<ApiKeyEntry>` где `ApiKeyEntry = {Key, Role}` (role: "contribute"|"system")
- [ ] Backward compat: если `ApiKeys` пуст, fallback на `ApiKey` (string) как contribute role
- [ ] `ApiKeyMiddleware` — проверяет ключ, определяет role, ставит `HttpContext.Items["ApiKeyRole"]`
- [ ] Role-based authorization: endpoint-level check (contribute vs system)
  - System-only endpoints: `PUT /api/config`, `POST /api/system/restart`, lifecycle destructive, MCP add/remove, force checkout
  - Contribute endpoints: всё остальное
- [ ] Auto-generation при первом старте: если keys.json не существует → генерировать `hc_contrib_<random>` + `hc_sys_<random>` → сохранить в `data/security/keys.json` → печатать оба в консоль
- [ ] `Program.cs` — регистрация, печать ключей в консоль при старте
- [ ] Unit tests: middleware role check, fallback compat, key generation
- [ ] `dotnet build` + `dotnet test` pass

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

## Links
- ADR-0004: [../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md](../EPIC_Hercules_Studio/adr/0004-dual-api-keys.md)
- Backlog: [../backlog.md](../backlog.md)