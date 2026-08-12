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
- [Phase 5 — IoT/edge fleet and mesh operations (Q3 2027)](#phase-5--iotedge-fleet-and-mesh-operations-q3-2027)
- [Workflow](#workflow)
- [Roadmap cron](#roadmap-cron)

> **Initiative** — номер инициативы в [ROADMAP-EN.md](../ROADMAP-EN.md) / [ROADMAP-RU.md](../ROADMAP-RU.md). `—` означает, что задача — деталь реализации существующей инициативы (sub-task), без собственного номера в roadmap.

---

## Phase 1 — Single autonomous unit (now → Q3 2026)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 1 | 1 | [Базовый цикл агента](tasks/task_001.md) | `core-agent-loop` | pending |
| 2 | 2 | [Жизненный цикл навыков](tasks/task_002.md) | `skill-lifecycle` | pending |
| 3 | 3 | [Гибридное хранилище](tasks/task_003.md) | `hybrid-storage` | pending |
| 4 | 4 | [Мульти-провайдер LLM](tasks/task_004.md) | `multi-provider-llm` | pending |
| 5 | 5 | [Интерфейсы](tasks/task_005.md) | `interfaces` | done |
| 6 | 6 | [Тесты и бенчмарки](tasks/task_006.md) | `tests-and-benchmarks` | pending |
| 7 | — | [Типизированные контракты агента](tasks/task_007.md) | `typed-contracts` | pending |
| 8 | — | [Ограниченный цикл исполнения](tasks/task_008.md) | `bounded-execution` | done |
| 9 | — | [Граница инструментов и policy engine](tasks/task_009.md) | `tool-boundary-policy` | done |
| 10 | — | [Гейты подтверждения](tasks/task_010.md) | `approval-gates` | pending |
| 11 | — | [Слоистая память](tasks/task_011.md) | `layered-memory` | pending |
| 12 | — | [Бюджеты и guardrails](tasks/task_012.md) | `budget-guardrails` | pending |
| 13 | — | [Фундамент OpenTelemetry](tasks/task_013.md) | `opentelemetry` | pending |
| 14 | — | [Аудит и приватность](tasks/task_014.md) | `audit-privacy` | done |
| 15 | — | [Секреты и конфигурация](tasks/task_015.md) | `secrets-config` | pending |
| 16 | — | [Оценка навыков (eval harness)](tasks/task_016.md) | `eval-harness` | pending |
| 17 | — | [Безопасное самоулучшение](tasks/task_017.md) | `safe-self-improvement` | pending |
| 18 | — | [Устойчивый жизненный цикл задач](tasks/task_018.md) | `durable-task-lifecycle` | done |

## Phase 2 — Composable skills and tool use (Q4 2026)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 19 | 7 | [Формат пакета навыка](tasks/task_019.md) | `skill-package-format` | done |
| 20 | — | [Манифест навыка и совместимость](tasks/task_020.md) | `skill-manifest` | done |
| 21 | 8 | [Маркетплейс навыков](tasks/task_021.md) | `skill-marketplace` | done |
| 22 | 9 | [Семантическая маршрутизация](tasks/task_022.md) | `semantic-routing` | done |
| 23 | — | [Детерминированный fallback маршрутизатора](tasks/task_023.md) | `deterministic-router` | done |
| 24 | 10 | [Реестр инструментов](tasks/task_024.md) | `tool-registry` | pending |
| 25 | — | [MCP-адаптер](tasks/task_025.md) | `mcp-adapter` | pending |
| 26 | — | [Least-privilege grants](tasks/task_026.md) | `least-privilege-grants` | pending |
| 27 | — | [Сборка и сжатие контекста](tasks/task_027.md) | `context-assembly` | pending |
| 28 | — | [Кэширование](tasks/task_028.md) | `caching` | pending |
| 29 | — | [Score качества навыка](tasks/task_029.md) | `skill-quality-score` | pending |
| 30 | 11 | [Шаблоны агентов](tasks/task_030.md) | `agent-templates` | pending |
| 31 | — | [Симуляция шаблонов](tasks/task_031.md) | `template-simulation` | pending |

## Phase 3 — Inter-agent protocol (Q1 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 32 | 12 | [Манифест агента](tasks/task_032.md) | `agent-manifest` | pending |
| 33 | — | [A2A Agent Card совместимость](tasks/task_033.md) | `a2a-agent-card` | pending |
| 34 | 13 | [Capability registry](tasks/task_034.md) | `capability-registry` | pending |
| 35 | 14 | [Формат inter-agent сообщений](tasks/task_035.md) | `delegation-envelope` | pending |
| 36 | — | [Протокол жизненного цикла задач](tasks/task_036.md) | `task-lifecycle-protocol` | pending |
| 37 | 15 | [Опции транспорта](tasks/task_037.md) | `transports` | pending |
| 38 | 16 | [Механизмы discovery](tasks/task_038.md) | `discovery` | pending |
| 39 | — | [Идентичность и делегация](tasks/task_039.md) | `identity-delegation` | pending |
| 40 | — | [Trust и admission policy](tasks/task_040.md) | `trust-admission` | pending |
| 41 | — | [Inter-agent audit trail](tasks/task_041.md) | `inter-agent-audit` | pending |
| 42 | — | [Контрактные и chaos тесты](tasks/task_042.md) | `protocol-tests` | pending |

## Phase 4 — Mesh orchestration (Q2 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 43 | 17 | [Mesh router](tasks/task_043.md) | `mesh-router` | pending |
| 44 | 17 | [Сложность и стоимость](tasks/task_044.md) | `complexity-router` | pending |
| 45 | 18 | [Fan-out / fan-in](tasks/task_045.md) | `fan-out-in` | pending |
| 46 | 22 | [Verification pipeline](tasks/task_046.md) | `verification-pipeline` | pending |
| 47 | 19 | [Retry, timeout, circuit breaker](tasks/task_047.md) | `retry-timeout-breaker` | pending |
| 48 | 23 | [Границы делегации](tasks/task_048.md) | `delegation-boundaries` | pending |
| 49 | 24 | [Human-in-the-loop эскалация](tasks/task_049.md) | `human-escalation` | pending |
| 50 | 20 | [Distributed reflection](tasks/task_050.md) | `distributed-reflection` | pending |
| 51 | 21 | [Shared memory sync](tasks/task_051.md) | `shared-memory-sync` | pending |
| 52 | 31 | [Mesh evaluation suite](tasks/task_052.md) | `mesh-eval-suite` | pending |
| 65 | 25 | [Mesh observability](tasks/task_065.md) | `mesh-observability` | pending |
| 66 | 26 | [Абстракция mesh-бэкендов](tasks/task_066.md) | `mesh-backends-abstraction` | pending |
| 67 | 27 | [Redis/Valkey coordination backend](tasks/task_067.md) | `redis-coordination-backend` | pending |
| 68 | 28 | [NATS / JetStream transport option](tasks/task_068.md) | `nats-jetstream-transport` | pending |
| 69 | 29 | [PostgreSQL shared state backend](tasks/task_069.md) | `postgres-shared-state` | pending |
| 70 | 30 | [Backend-профили и деградация](tasks/task_070.md) | `backend-profiles-degradation` | pending |

## Phase 5 — IoT/edge fleet and mesh operations (Q3 2027)

| # | Initiative | Task | Slug | Status |
|---|------------|------|------|--------|
| 53 | 32 | [Mesh dashboard](tasks/task_053.md) | `mesh-dashboard` | pending |
| 54 | 33 | [Централизованные логи и трейсы](tasks/task_054.md) | `centralized-observability` | pending |
| 55 | 40 | [Security operations](tasks/task_055.md) | `security-ops` | pending |
| 56 | 35 | [Rate limits и квоты](tasks/task_056.md) | `rate-limits-quotas` | pending |
| 57 | 36 | [Управление жизненным циклом](tasks/task_057.md) | `lifecycle-management` | pending |
| 58 | 41 | [Configuration и policy rollout](tasks/task_058.md) | `config-policy-rollout` | pending |
| 59 | 37 | [Edge provisioning](tasks/task_059.md) | `edge-provisioning` | pending |
| 60 | 38 | [Offline resilience](tasks/task_060.md) | `offline-resilience` | pending |
| 61 | 42 | [Local-first degradation](tasks/task_061.md) | `local-degradation` | pending |
| 62 | 39 | [Fleet templates](tasks/task_062.md) | `fleet-templates` | pending |
| 63 | 43 | [Backup и recovery](tasks/task_063.md) | `backup-recovery` | pending |
| 64 | 44 | [Операционные SLO](tasks/task_064.md) | `operational-slos` | pending |

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


