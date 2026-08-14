# Task 104 — hercules-workflow-server

**Phase:** 8
**Status:** pending
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

## Scope / Likely files
src/workflow-server/ (new project), src/workflow-server/Controllers/, src/workflow-server/Executor/, src/workflow-server/Auth/, src/workflow-server/Program.cs, src/workflow-server/appsettings.json

## Links
- ADR-0008: [../EPIC_Hercules_Studio/adr/0008-workflow-server-architecture.md](../EPIC_Hercules_Studio/adr/0008-workflow-server-architecture.md)
- Studio Stage 8: [../EPIC_Hercules_Studio/tasks/stage_08_workflow.md](../EPIC_Hercules_Studio/tasks/stage_08_workflow.md)
- Backlog: [../backlog.md](../backlog.md)