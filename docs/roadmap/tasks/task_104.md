# Task 104 — hercules-workflow-server

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `workflow-server`
**Studio Stage:** 8

## Goal
Создать отдельный .NET сервис `hercules-workflow-server` для durable workflow execution: хранение определений, исполнитель, триггеры (webhook + cron), история, мониторинг. См. [ADR-0008](../EPIC_Hercules_Studio/adr/0008-workflow-server-architecture.md).

## Acceptance criteria
- [ ] `src/workflow-server/Hercules.WorkflowServer.csproj` — новый ASP.NET Core проект
- [ ] REST API:
  - `POST /api/workflows` — save definition (clientId/secret auth)
  - `GET /api/workflows` — list definitions
  - `GET /api/workflows/{id}` — get definition
  - `DELETE /api/workflows/{id}` — delete
  - `POST /api/workflows/{id}/run` — start execution (input optional)
  - `GET /api/workflows/{id}/executions` — list executions
  - `GET /api/workflows/executions/{eid}` — execution status (node states)
  - `POST /api/workflows/triggers/webhook/{token}` — webhook trigger (public, token-auth)
  - `GET /api/workflows/triggers/cron` — list cron triggers (clientId/secret)
  - `GET /api/workflows/health` — health check
- [ ] Auth: `clientId/clientSecret` (simplified, no JWT for MVP)
  - `WorkflowAuthConfig.Clients` = list of `{clientId, clientSecret, name}`
  - Auto-generate at first startup, print to console, save to `data/security/workflow-credentials.json`
  - Middleware: validate clientId/secret, set `HttpContext.Items["ClientId"]`
- [ ] Executor:
  - Reads workflow graph (task_105 model)
  - For ServiceTask: `POST /api/mesh/intent` to target agent (workflow-server→agent auth)
  - Persists state: workflow definitions + executions + node states (SQLite or Postgres)
  - Handles retries (retry policy per node), timeouts
  - DelegatedTask: linked to workflow execution (task_106)
  - Parent/child task relationships (task_107)
  - Checkpoint persistence (task_108)
- [ ] Triggers:
  - Webhook: `POST /api/workflows/triggers/webhook/{token}` → start workflow
  - Cron: Quartz/Hangfire scheduler → cron expression → start workflow
- [ ] Agent registration: workflow-server registers as trusted peer on agents (via `POST /api/mesh/agents/register`)
  - Agent config: `A2A.Endpoints["workflow-server"] = {url, clientId, clientSecret}`
- [ ] `appsettings.json` — config: port (8430), auth, storage, triggers
- [ ] Docker support (future): Dockerfile + docker-compose
- [ ] Unit tests: API CRUD, executor logic, trigger handling
- [ ] `dotnet build` + `dotnet test` pass

## Dependencies
- task_105 (workflow graph model + executor) — можно делать параллельно

## Sub-tasks (this tick — MVP foundation)
- [x] Create `src/workflow-server/Hercules.WorkflowServer.csproj` (ASP.NET Core 10 minimal API, port 8430)
- [x] Add `src/workflow-server/` to `Hercules.slnx` solution
- [x] Update core `Hercules.csproj` `DefaultItemExcludes` to skip `Hercules.WorkflowServer/**` (prevent globbing into core compile)
- [x] `Config/WorkflowServerConfig.cs` — port, storage, auth, agents list
- [x] `Auth/ClientCredentialStore.cs` — auto-generate clientId/clientSecret on first start, persist to `data/security/workflow-credentials.json` (ADR-0008)
- [x] `Auth/ClientAuthMiddleware.cs` — `X-Client-Id` + `X-Client-Secret` header auth (basic; roles out of scope for MVP)
- [x] `Models/WorkflowDefinition.cs` — DTO + entity (`Id`, `Name`, `Version`, `GraphJson`, `CreatedAt`, `UpdatedAt`)
- [x] `Storage/IWorkflowDefinitionStore.cs` + `Storage/SqliteWorkflowDefinitionStore.cs` — SQLite-backed CRUD (Microsoft.Data.Sqlite, WAL, SemaphoreSlim — same family as agent's session store, task_071)
- [x] `Controllers/WorkflowsController.cs` — REST API: POST/GET/DELETE `/api/workflows`, GET `/api/workflows/{id}`, POST `/api/workflows/{id}/run` (stub → 501 until task_105), GET `/api/workflows/{id}/executions` (stub list), GET `/api/workflows/executions/{eid}` (stub)
- [x] `Controllers/TriggersController.cs` — POST `/api/workflows/triggers/webhook/{token}` (records trigger event в in-memory ring), GET `/api/workflows/triggers/cron` (stub)
- [x] `Controllers/HealthController.cs` — GET `/api/workflows/health` (200 OK with version + uptime + storage health)
- [x] `Program.cs` — minimal API, Kestrel on port 8430, DI wiring, clientId/secret middleware, health endpoint
- [x] `appsettings.json` — port 8430, DataRoot, agent endpoints placeholder
- [x] Unit tests в `tests/Hercules.Agent.Tests/WorkflowServer/` (20 тестов):
  - `ClientCredentialStoreTests` (5 тестов: generation, configured-приоритет, persistence round-trip, regeneration on tampering)
  - `ClientAuthMiddlewareTests` (7 тестов: valid pass, wrong secret, missing headers, health bypass, no-creds open mode, CORS preflight, webhook bypass)
  - `SqliteWorkflowDefinitionStoreTests` (8 тестов: save+get round-trip, update, list summaries, get nonexistent, delete existing/nonexistent, IsHealthy)
- [x] `.gitignore` — добавлен `src/workflow-server/data/`
- [x] `dotnet build` (solution) → 0 errors
- [x] `dotnet test --filter "FullyQualifiedName~WorkflowServer"` → 20/20 passed

## Validation (this tick)
- `dotnet build src/workflow-server/Hercules.WorkflowServer.csproj` → 0 errors
- `dotnet build src/agent/Hercules.slnx` → 0 errors (143 pre-existing warnings — все не связаны с этой задачей)
- `dotnet test --filter "FullyQualifiedName~WorkflowServer"` → 20/20 passed
- Smoke test (реальный запуск workflow-server на порту 8430):
  - First start: clientId+clientSecret сгенерированы и сохранены в `data/security/workflow-credentials.json`
  - Second start: credentials загружены из файла (тот же clientId)
  - `GET /api/workflows/health` (без auth) → 200 OK с JSON `{status,service,version,uptimeSeconds,storage}`
  - `GET /api/workflows` без headers → 401 Unauthorized
  - `GET /api/workflows` с правильными X-Client-Id/X-Client-Secret → 200 OK
  - `POST /api/workflows` с workflow definition → 201 Created, location header
  - `GET /api/workflows/{id}` → 200 OK с полным definition (включая graphJson)
  - Persistence: workflow definition сохранён в SQLite (`data/workflow-server.db`), доступен после рестарта
  - `POST /api/workflows/{id}/run` → 501 Not Implemented (явный follow-up указатель на task_105)
  - `POST /api/workflows/triggers/webhook/{token}` → 202 Accepted (webhook bypass, token в URL)
  - `GET /api/workflows/triggers/cron` → 501 Not Implemented (cron scheduler — follow-up)

## Sub-tasks (follow-up — next ticks)
- [ ] `WorkflowExecutor` (graph traversal, ServiceTask → `/api/mesh/intent`, UserTask long-poll, Conditional, ParallelGateway, Timer, Error event) — task_105
- [ ] Quartz/Hangfire cron scheduler — real cron expression parsing
- [ ] Webhook token registry + per-token startWorkflow
- [ ] Agent registration on startup (`POST /api/mesh/agents/register` against configured agent endpoints)
- [ ] DelegatedTask linking + parent/child (task_106/107) integration
- [ ] DurableTask checkpoint persistence (task_108) integration
- [ ] Long-poll / SSE for live execution updates
- [ ] Dockerfile + docker-compose (ADR-0008)

## Implementation notes (this tick)
- Порт 8430 per ADR-0003 (agent: 8421, workflow-server: 8430).
- Аутентификация: simplified `clientId/clientSecret` header-based — out-of-scope JWT, roles, scopes (matches ADR-0008 MVP).
- Workflow graph JSON stored verbatim in `workflows.graph_json`; executor (task_105) будет парсить и интерпретировать.
- Storage: SQLite в `DataRoot/workflow-server.db` (отдельный файл от agent's `sessions.db` чтобы избежать конфликтов).
- DI: `IWorkflowDefinitionStore` interface → SQLite impl; auth middleware — отдельный pipeline (после ApiKey? нет, вместо — workflow-server standalone).
- Health endpoint НЕ требует auth (как у agent).
- Тесты: 6+ unit tests, target filter `FullyQualifiedName~WorkflowServer`.
- Не ломать `Hercules.csproj` glob — добавить `Hercules.WorkflowServer/**` в `DefaultItemExcludes`.

## Completion note (2026-08-17)

Создан новый .NET-сервис `src/workflow-server/Hercules.WorkflowServer.csproj` (ASP.NET Core 10 minimal API, порт 8430). MVP-фундамент готов:

**Реализовано в этом тике:**
- `Config/WorkflowServerConfig.cs` — секция `WorkflowServer` в `appsettings.json` (port, DataRoot, auth, agents list)
- `Auth/ClientCredentialStore.cs` — auto-generate clientId/clientSecret на первом старте, persist в `DataRoot/security/workflow-credentials.json` (идемпотентно)
- `Auth/ClientAuthMiddleware.cs` — `X-Client-Id` + `X-Client-Secret` header auth, constant-time comparison, bypass для CORS preflight, health-check, webhook trigger
- `Models/WorkflowDefinition.cs` — DTO + entity
- `Storage/IWorkflowDefinitionStore.cs` + `SqliteWorkflowDefinitionStore.cs` — SQLite CRUD с WAL и `SemaphoreSlim`(1,1) (тот же thread-safe паттерн, что и в agent's `SqliteSessionStore`, task_071)
- `Controllers/WorkflowsController.cs` — 7 endpoints: POST/GET/DELETE `/api/workflows`, GET `/api/workflows/{id}`, POST `/api/workflows/{id}/run` (501 stub), GET `/api/workflows/{id}/executions` (501 stub), GET `/api/workflows/executions/{eid}` (501 stub)
- `Controllers/TriggersController.cs` — POST `/api/workflows/triggers/webhook/{token}` (in-memory ring buffer), GET `/api/workflows/triggers/cron` (501 stub)
- `Controllers/HealthController.cs` — GET `/api/workflows/health` (200 OK с status, service, version, uptime, storage health)
- `Program.cs` — Kestrel на порту 8430, DI wiring, JSON options, middleware pipeline
- `appsettings.json` — defaults: port 8430, DataRoot "data", пустой auth + agents
- `src/agent/Hercules.slnx` — добавлен project reference
- `src/agent/Hercules.csproj` — добавлен `Hercules.WorkflowServer/**` в `DefaultItemExcludes` (чтобы не glob'ить .cs файлы в core)
- `tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — добавлен project reference
- `.gitignore` — добавлен `src/workflow-server/data/`
- 20 unit-тестов в `tests/Hercules.Agent.Tests/WorkflowServer/`

**Validation:**
- `dotnet build src/agent/Hercules.slnx` → 0 errors
- `dotnet test --filter "FullyQualifiedName~WorkflowServer"` → 20/20 passed
- Smoke test (реальный запуск workflow-server): health 200, auth 401, CRUD round-trip, persistence across restart, webhook 202, run stub 501, cron stub 501 — всё работает

**Известные ограничения (явно отмечены в API как 501 Not Implemented):**
- Workflow executor + graph interpretation → task_105
- Реальный cron-scheduler (Quartz/Hangfire) → follow-up
- Token-registry для webhook'ов + per-token startWorkflow → follow-up
- Agent registration call (`POST /api/mesh/agents/register` на startup) → follow-up
- DelegatedTask + parent/child (task_106/107) integration → follow-up
- DurableTask checkpoint persistence (task_108) integration → follow-up
- Long-poll / SSE для live execution updates → follow-up
- Dockerfile + docker-compose → follow-up

**Файлы:**
- `src/workflow-server/Hercules.WorkflowServer.csproj` (new)
- `src/workflow-server/Program.cs` (new)
- `src/workflow-server/appsettings.json` (new)
- `src/workflow-server/Config/WorkflowServerConfig.cs` (new)
- `src/workflow-server/Auth/ClientCredentialStore.cs` (new)
- `src/workflow-server/Auth/ClientAuthMiddleware.cs` (new)
- `src/workflow-server/Models/WorkflowDefinition.cs` (new)
- `src/workflow-server/Storage/IWorkflowDefinitionStore.cs` (new)
- `src/workflow-server/Storage/SqliteWorkflowDefinitionStore.cs` (new)
- `src/workflow-server/Controllers/WorkflowsController.cs` (new)
- `src/workflow-server/Controllers/TriggersController.cs` (new)
- `src/workflow-server/Controllers/HealthController.cs` (new)
- `tests/Hercules.Agent.Tests/WorkflowServer/ClientCredentialStoreTests.cs` (new)
- `tests/Hercules.Agent.Tests/WorkflowServer/ClientAuthMiddlewareTests.cs` (new)
- `tests/Hercules.Agent.Tests/WorkflowServer/SqliteWorkflowDefinitionStoreTests.cs` (new)
- `src/agent/Hercules.slnx` (modified — added project)
- `src/agent/Hercules.csproj` (modified — added DefaultItemExcludes)
- `tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` (modified — added project reference)
- `.gitignore` (modified — ignore workflow-server/data/)
- `docs/roadmap/tasks/task_104.md` (modified — sub-tasks, validation, completion note)

## Scope / Likely files
src/workflow-server/ (new project), src/workflow-server/Controllers/, src/workflow-server/Executor/ (stub), src/workflow-server/Auth/, src/workflow-server/Program.cs, src/workflow-server/appsettings.json
tests/Hercules.Agent.Tests/WorkflowServer/ (new test folder)
src/agent/Hercules.slnx (add project reference)
src/agent/Hercules.csproj (add to DefaultItemExcludes)

## Links
- ADR-0008: [../EPIC_Hercules_Studio/adr/0008-workflow-server-architecture.md](../EPIC_Hercules_Studio/adr/0008-workflow-server-architecture.md)
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)