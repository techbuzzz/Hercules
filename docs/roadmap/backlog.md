# Hercules — Roadmap Backlog

Это рабочий backlog по [ROADMAP-EN.md](../ROADMAP-EN.md) / [ROADMAP-RU.md](../ROADMAP-RU.md).
Каждой initiative соответствует отдельный `task_NNN.md` в [tasks/](tasks/).

> **Использование:** выбираем следующую `pending` задачу, переводим в `in_progress`,
> декомпозируем в sub-tasks, выполняем, переводим в `done`. См. раздел [Workflow](#workflow).

## Содержание

- [Phase 1 — Single autonomous unit (now → Q3 2026)](#phase-1--single-autonomous-unit-now--q3-2026)
- [Phase 2 — Composable skills and tool use (Q4 2026)](#phase-2--composable-skills-and-tool-use-q4-2026)
- [Phase 3 — Inter-agent protocol (Q1 2027)](#phase-3--inter-agent-protocol-q1-2027)
- [Phase 4 — Mesh orchestration (Q2 2027)](#phase-4--mesh-orchestration-q2-2027)
- [Phase 5 — Performance and high availability hardening (Q4 2027)](#phase-5--performance-and-high-availability-hardening-q4-2027)
- [Phase 6 — IoT/edge fleet and mesh operations (Q3 2027)](#phase-6--iotedge-fleet-and-mesh-operations-q3-2027)
- [Phase 7 — Web UI follow-ups (cross-cutting)](#phase-7--web-ui-follow-ups-cross-cutting)
- [Workflow](#workflow)
- [Roadmap cron](#roadmap-cron)

> **Initiative** — номер инициативы в [ROADMAP-EN.md](../ROADMAP-EN.md) / [ROADMAP-RU.md](../ROADMAP-RU.md). `—` означает, что задача — деталь реализации существующей инициативы (sub-task), без собственного номера в roadmap.

---

## Phase 1 — Single autonomous unit (now → Q3 2026)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 1 | 1 | [Базовый цикл агента](tasks/completed/task_001.md) | `core-agent-loop` | done |
| 2 | 2 | [Жизненный цикл навыков](tasks/completed/task_002.md) | `skill-lifecycle` | done |
| 3 | 3 | [Гибридное хранилище](tasks/completed/task_003.md) | `hybrid-storage` | done |
| 4 | 4 | [Мульти-провайдер LLM](tasks/completed/task_004.md) | `multi-provider-llm` | done |
| 5 | 5 | [Интерфейсы](tasks/completed/task_005.md) | `interfaces` | done |
| 6 | 6 | [Тесты и бенчмарки](tasks/completed/task_006.md) | `tests-and-benchmarks` | done |
| 7 | — | [Типизированные контракты агента](tasks/completed/task_007.md) | `typed-contracts` | done |
| 8 | — | [Ограниченный цикл исполнения](tasks/completed/task_008.md) | `bounded-execution` | done |
| 9 | — | [Граница инструментов и policy engine](tasks/completed/task_009.md) | `tool-boundary-policy` | done |
| 10 | — | [Гейты подтверждения](tasks/completed/task_010.md) | `approval-gates` | done |
| 11 | — | [Слоистая память](tasks/completed/task_011.md) | `layered-memory` | done |
| 12 | — | [Бюджеты и guardrails](tasks/completed/task_012.md) | `budget-guardrails` | done |
| 13 | — | [Фундамент OpenTelemetry](tasks/completed/task_013.md) | `opentelemetry` | done |
| 14 | — | [Аудит и приватность](tasks/completed/task_014.md) | `audit-privacy` | done |
| 15 | — | [Секреты и конфигурация](tasks/completed/task_015.md) | `secrets-config` | done |
| 16 | — | [Оценка навыков (eval harness)](tasks/completed/task_016.md) | `eval-harness` | done |
| 17 | — | [Безопасное самоулучшение](tasks/completed/task_017.md) | `safe-self-improvement` | done |
| 18 | — | [Устойчивый жизненный цикл задач](tasks/completed/task_018.md) | `durable-task-lifecycle` | done |

## Phase 2 — Composable skills and tool use (Q4 2026)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 19 | 7 | [Формат пакета навыка](tasks/completed/task_019.md) | `skill-package-format` | done |
| 20 | — | [Манифест навыка и совместимость](tasks/completed/task_020.md) | `skill-manifest` | done |
| 21 | 8 | [Маркетплейс навыков](tasks/completed/task_021.md) | `skill-marketplace` | done |
| 22 | 9 | [Семантическая маршрутизация](tasks/completed/task_022.md) | `semantic-routing` | done |
| 23 | — | [Детерминированный fallback маршрутизатора](tasks/completed/task_023.md) | `deterministic-router` | done |
| 24 | 10 | [Реестр инструментов](tasks/completed/task_024.md) | `tool-registry` | done |
| 25 | — | [MCP-адаптер](tasks/completed/task_025.md) | `mcp-adapter` | done |
| 26 | — | [Least-privilege grants](tasks/completed/task_026.md) | `least-privilege-grants` | done |
| 27 | — | [Сборка и сжатие контекста](tasks/completed/task_027.md) | `context-assembly` | done |
| 28 | — | [Кэширование](tasks/completed/task_028.md) | `caching` | done |
| 29 | — | [Score качества навыка](tasks/completed/task_029.md) | `skill-quality-score` | done |
| 30 | 11 | [Шаблоны агентов](tasks/completed/task_030.md) | `agent-templates` | done |
| 31 | — | [Симуляция шаблонов](tasks/completed/task_031.md) | `template-simulation` | done |

## Phase 3 — Inter-agent protocol (Q1 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 32 | 12 | [Манифест агента](tasks/completed/task_032.md) | `agent-manifest` | done |
| 33 | — | [A2A Agent Card совместимость](tasks/completed/task_033.md) | `a2a-agent-card` | done |
| 34 | 13 | [Capability registry](tasks/completed/task_034.md) | `capability-registry` | done |
| 35 | 14 | [Формат inter-agent сообщений](tasks/completed/task_035.md) | `delegation-envelope` | done |
| 36 | — | [Протокол жизненного цикла задач](tasks/completed/task_036.md) | `task-lifecycle-protocol` | done |
| 37 | 15 | [Опции транспорта](tasks/completed/task_037.md) | `transports` | done |
| 38 | 16 | [Механизмы discovery](tasks/completed/task_038.md) | `discovery` | done |
| 39 | — | [Идентичность и делегация](tasks/completed/task_039.md) | `identity-delegation` | done |
| 40 | — | [Trust и admission policy](tasks/completed/task_040.md) | `trust-admission` | done |
| 41 | — | [Inter-agent audit trail](tasks/completed/task_041.md) | `inter-agent-audit` | done |
| 42 | — | [Контрактные и chaos тесты](tasks/completed/task_042.md) | `protocol-tests` | done |

## Phase 4 — Mesh orchestration (Q2 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 43 | 17 | [Mesh router](tasks/completed/task_043.md) | `mesh-router` | done |
| 44 | 17 | [Сложность и стоимость](tasks/completed/task_044.md) | `complexity-router` | done |
| 45 | 18 | [Fan-out / fan-in](tasks/completed/task_045.md) | `fan-out-in` | done |
| 46 | 22 | [Verification pipeline](tasks/completed/task_046.md) | `verification-pipeline` | done |
| 47 | 19 | [Retry, timeout, circuit breaker](tasks/completed/task_047.md) | `retry-timeout-breaker` | done |
| 48 | 23 | [Границы делегации](tasks/completed/task_048.md) | `delegation-boundaries` | done |
| 49 | 24 | [Human-in-the-loop эскалация](tasks/completed/task_049.md) | `human-escalation` | done |
| 50 | 20 | [Distributed reflection](tasks/completed/task_050.md) | `distributed-reflection` | done |
| 51 | 21 | [Shared memory sync](tasks/completed/task_051.md) | `shared-memory-sync` | done |
| 52 | 31 | [Mesh evaluation suite](tasks/completed/task_052.md) | `mesh-eval-suite` | done |
| 65 | 25 | [Mesh observability](tasks/completed/task_065.md) | `mesh-observability` | done |
| 66 | 26 | [Абстракция mesh-бэкендов](tasks/completed/task_066.md) | `mesh-backends-abstraction` | done |
| 67 | 27 | [Redis/Valkey coordination backend](tasks/completed/task_067.md) | `redis-coordination-backend` | done |
| 68 | 28 | [NATS / JetStream transport option](tasks/completed/task_068.md) | `nats-jetstream-transport` | done |
| 69 | 29 | [PostgreSQL shared state backend](tasks/completed/task_069.md) | `postgres-shared-state` | done |
| 70 | 30 | [Backend-профили и деградация](tasks/completed/task_070.md) | `backend-profiles-degradation` | done |

## Phase 5 — Performance and high availability hardening (Q4 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 71 | 45 | [SQLite thread-safety and sync-over-async removal](tasks/completed/task_071.md) | `sqlite-thread-safety` | done |
| 72 | 45 | [QuotaService cleanup fix and distributed quotas](tasks/completed/task_072.md) | `quota-cleanup-fix` | done |
| 73 | 45 | [Outbox synced-state and bounded-queue prune fix](tasks/completed/task_073.md) | `outbox-synced-fix` | done |
| 74 | 45 | [NATS JetStream ack/fail implementation](tasks/completed/task_074.md) | `nats-jetstream-ack` | done |
| 75 | 46 | [DI lifetime fixes: captive dependency and AgentCore singleton](tasks/completed/task_075.md) | `di-lifetime-fixes` | done |
| 76 | 46 | [ResilientLLMClient per-call provider/model](tasks/completed/task_076.md) | `resilient-llm-per-call-provider` | done |
| 77 | 46 | [Sync-over-async sweep: ToolPolicyEngine, SloService, Redis/Postgres timers, RolloutController](tasks/completed/task_077.md) | `sync-over-async-sweep` | done |
| 78 | 46 | [IHttpClientFactory adoption and standard resilience handlers](tasks/completed/task_078.md) | `httpclient-factory-adoption` | done |
| 79 | 46 | [Real health checks infrastructure](tasks/completed/task_079.md) | `health-checks-infrastructure` | done |
| 80 | 46 | [Graceful shutdown and drain](tasks/completed/task_080.md) | `graceful-shutdown-drain` | done |
| 81 | 46 | [Kestrel tuning, framework rate limiter, compression, output cache](tasks/completed/task_081.md) | `kestrel-rate-limiter-compression` | done |
| 82 | 46 | [RouterHealthTracker, Redis CAS, Postgres reconnect, BuildServiceProvider](tasks/completed/task_082.md) | `router-redis-postgres-di-fixes` | done |
| 83 | 47 | [CacheService stampede, eviction, and sliding expiration](tasks/completed/task_083.md) | `cache-stampede-eviction` | done |
| 84 | 47 | [ProposalStore caching, async I/O, and hot-path allocations](tasks/completed/task_084.md) | `proposal-store-hotpath-alloc` | done |
| 85 | 47 | [OpenTelemetry polish: console gating, process instrumentation, histogram buckets, async logging](tasks/completed/task_085.md) | `otel-logging-polish` | done |
| 86 | 47 | [Backpressure, bounded channels, and DLQ/requeue fixes](tasks/completed/task_086.md) | `backpressure-bounded-channels-dlq` | done |
| 87 | 47 | [Misc hardening: DelegationBoundary TTL, ResilientTransport trim, CORS, backup passphrase, SLO real metrics](tasks/completed/task_087.md) | `misc-hardening` | done |

## Phase 6 — IoT/edge fleet and mesh operations (Q3 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 53 | 32 | [Mesh dashboard](tasks/completed/task_053.md) | `mesh-dashboard` | done |
| 54 | 33 | [Централизованные логи и трейсы](tasks/completed/task_054.md) | `centralized-observability` | done |
| 55 | 40 | [Security operations](tasks/completed/task_055.md) | `security-ops` | done |
| 56 | 35 | [Rate limits и квоты](tasks/completed/task_056.md) | `rate-limits-quotas` | done |
| 57 | 36 | [Управление жизненным циклом](tasks/completed/task_057.md) | `lifecycle-management` | done |
| 58 | 41 | [Configuration и policy rollout](tasks/completed/task_058.md) | `config-policy-rollout` | done |
| 59 | 37 | [Edge provisioning](tasks/completed/task_059.md) | `edge-provisioning` | done |
| 60 | 38 | [Offline resilience](tasks/completed/task_060.md) | `offline-resilience` | done |
| 61 | 42 | [Local-first degradation](tasks/completed/task_061.md) | `local-degradation` | done |
| 62 | 39 | [Fleet templates](tasks/completed/task_062.md) | `fleet-templates` | done |
| 63 | 43 | [Backup и recovery](tasks/completed/task_063.md) | `backup-recovery` | done |
| 64 | 44 | [Операционные SLO](tasks/completed/task_064.md) | `operational-slos` | done |

## Phase 7 — Web UI follow-ups (cross-cutting)

Инициатива 48 — доведение `src/hercules-web` (Astro 6.4) до полного покрытия уже готового backend API. Все backend-задачи, к которым эти web-задачи привязаны, должны быть в `done` до старта соответствующего web-таска.

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 88 | 48 | [A2A Agent Card panel](tasks/completed/task_088.md) | `web-a2a-card-panel` | done |
| 89 | 48 | [Capability registry panel](tasks/completed/task_089.md) | `web-capability-registry-panel` | pending |
| 90 | 48 | [Discovery panel](tasks/completed/task_090.md) | `web-discovery-panel` | done |
| 91 | 48 | [Trust admission policy panel](tasks/completed/task_091.md) | `web-trust-admission-panel` | done |
| 92 | 48 | [Mesh profiles & backend health](tasks/task_092.md) | `web-mesh-profiles-health` | pending |
| 93 | 48 | [Mesh observability & centralized logs](tasks/completed/task_093.md) | `web-mesh-observability` | done |
| 94 | 48 | [Backup & SLO panel](tasks/task_094.md) | `web-backup-slo-panel` | pending |
| 95 | 48 | [Security, quotas & rollout panel](tasks/task_095.md) | `web-security-quotas-rollout` | done |

## Phase 8 — Hercules Studio backend prerequisites

Доработки Hercules.WebApi и новых сервисов, необходимые для [Hercules Studio Epic](../EPIC_Hercules_Studio/README.md). Каждая задача должна быть в `done` до старта соответствующего Studio stage.

| # | Studio Stage | Task | Slug | Status |
|---|---|------|------|--------|
| 96 | 1 (pre-PR) | [Port migration 5000 → 8421](tasks/completed/task_096.md) | `port-migration-8421` | done |
| 97 | 1 | [Dual API keys (contribute + system)](tasks/task_097.md) | `dual-api-keys` | pending |
| 98 | 1 | [CheckIn/CheckOut protocol](tasks/task_098.md) | `checkin-checkout-protocol` | pending |
| 99 | 6 | [System restart protocol](tasks/task_099.md) | `system-restart-protocol` | pending |
| 100 | 6 | [MCP hot-reload (IConfigReload)](tasks/task_100.md) | `mcp-hot-reload` | pending |
| 101 | 6 | [SkillSdk (Hercules.SkillSdk NuGet)](tasks/task_101.md) | `skill-sdk` | pending |
| 102 | 6 | [Context distillation](tasks/task_102.md) | `context-distillation` | pending |
| 103 | 6 | [PostgreSQL session store](tasks/task_103.md) | `postgres-session-store` | pending |
| 104 | 8 | [hercules-workflow-server](tasks/task_104.md) | `workflow-server` | pending |
| 105 | 8 | [Workflow graph model + executor](tasks/task_105.md) | `workflow-graph-executor` | pending |
| 106 | 8 | [DelegatedTask persistence](tasks/task_106.md) | `delegated-task-persistence` | pending |
| 107 | 8 | [Parent/child task relationships](tasks/task_107.md) | `parent-child-tasks` | pending |
| 108 | 8 | [DurableTask checkpoint persistence](tasks/task_108.md) | `checkpoint-persistence` | pending |
| 109 | 0 (pre-req) | [AddOpenApi() в Program.cs](tasks/task_109.md) | `add-openapi-producer` | pending |
| 110 | 0 (pre-req) | [WithTags на все контроллеры](tasks/task_110.md) | `withtags-all-controllers` | pending |
| 111 | 0 (pre-req) | [Produces\<T\>() + DTO рефакторинг](tasks/task_111.md) | `produces-dto-refactor` | pending |
| 112 | 0 (pre-req) | [WithName на Marketplace + Template](tasks/task_112.md) | `withname-marketplace-template` | pending |
| 113 | 0 (Studio) | [Studio: openapi-typescript + Orval setup](tasks/task_113.md) | `studio-openapi-codegen-setup` | pending |
| 114 | 0-2 (Studio) | [Studio: migrate stores to Vue Query](tasks/task_114.md) | `studio-migrate-vue-query` | pending |
| 115 | 0 (Web-UI) | [Web-UI: openapi-typescript + Orval setup](tasks/task_115.md) | `webui-openapi-codegen-setup` | pending |
| 116 | 0 (Web-UI) | [Web-UI: migrate api.ts to Vue Query](tasks/task_116.md) | `webui-migrate-vue-query` | pending |

## Workflow

1. **Выбор следующей задачи:** из `pending` берём с наименьшим N, у которой выполнены зависимости.
2. **Декомпозиция:** открываем соответствующий `tasks/task_NNN.md`, заполняем раздел Acceptance criteria конкретными sub-tasks.
3. **Status flip:** в `tasks/task_NNN.md` правим `Status: pending` → `Status: in_progress` (commit).
4. **Работа:** реализуем, обновляем `Acceptance criteria` по мере выполнения (чекбоксы).
5. **Готово:** `Status: done` + ссылка на PR/commit + краткое описание реализованного поведения в конце файла.
6. **Блокер:** если застряли — `Status: blocked` + описание блокера в разделе Notes.

Правила:
- Не меняем Roadmap (вышестоящий документ) без обсуждения; backlog — рабочий артефакт.
- Соблюдаем [Design constraints](../ROADMAP-EN.md#design-constraints-for-a-micro-agent).
- Любая LLM-автономия ограничена [policy engine](../ROADMAP-EN.md#non-goals) и human-gate.
- Перед PR — `dotnet test` + ручной smoke + апдейт CHANGELOG.

## Roadmap cron

См. `scripts/roadmap-cron.md` — ежедневный self-reminder, который:

- сканирует `docs/roadmap/tasks/`,
- находит первую `pending` задачу с выполненными зависимостями,
- отправляет в текущую сессию prompt на декомпозицию и старт работы,
- удаляется сам, если все задачи в `done` или явный `blocked`.

Создаётся через `mavis cron create` (см. секцию Setup ниже).

### Setup

```
# Из корня репозитория
mavis cron create --name roadmap-driver --schedule "0 9 * * *" --prompt "Roadmap tick: просканируй docs/roadmap/tasks/, выбери первую pending задачу с выполненными зависимостями, переведи её в in_progress, декомпозируй в sub-tasks и приступай. Если все done — выведи отчёт и попроси удалить cron. Ссылка: docs/roadmap/backlog.md" --timezone "Europe/Berlin"
```


