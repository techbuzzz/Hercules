# hercules-web

Web UI для самообучающегося микро-агента **Hercules**: чат, навыки, профиль памяти, конфигурация и статистика. Построен на [Astro](https://astro.build) 6 + [Tailwind CSS](https://tailwindcss.com) v4, общается с backend через JSON API.

## Требования

- Node.js **≥ 22.12**
- Запущенный [Hercules.WebApi](../agent/Hercules.WebApi) бэкенд (по умолчанию `http://localhost:8421`)

## Установка

```bash
cd src/hercules-web
npm install
cp .env.example .env   # при необходимости отредактируйте PUBLIC_API_BASE / PUBLIC_API_KEY
```

## Команды

| Команда              | Назначение                                              |
| -------------------- | ------------------------------------------------------- |
| `npm run dev`        | Dev-сервер на `http://localhost:4321` с HMR             |
| `npm run build`      | Production-сборка в `./dist/` (статический сайт)        |
| `npm run preview`    | Локальный preview собранного билда                      |
| `npm run check`      | `astro check` — TypeScript + Astro-диагностика          |

## Переменные окружения

| Имя               | Дефолн                       | Назначение                              |
| ----------------- | ---------------------------- | --------------------------------------- |
| `PUBLIC_API_BASE` | `http://localhost:8421`      | Базовый URL backend API                 |
| `PUBLIC_API_KEY`  | `dev-local-key`              | Ключ `X-Api-Key`, должен совпадать с `WebApi.ApiKey` в backend `appsettings.json` |

`PUBLIC_*` — клиентские префиксы Astro: значения встраиваются в JS-бандл и попадают в браузер. Не кладите сюда серверные секреты.

## Стек

- **Astro 6** — статический рендер, минимум JS на странице, скрипты инициализируются по необходимости.
- **Tailwind CSS 4** через Vite-плагин (`@tailwindcss/vite`).
- **TypeScript** в `strict` режиме.
- **Vanilla DOM API** в `<script>` секциях компонентов — без фреймворков, без зависимостей.

## Структура

```
src/
├── layouts/
│   └── Layout.astro        # Базовый макет: header с навигацией, main, footer
├── components/
│   ├── ChatBox.astro       # Чат с эффектом «печати» и notices
│   ├── ConfigEditor.astro  # JSON-редактор конфигурации (PATCH/PUT)
│   ├── ProfileEditor.astro # Markdown-редактор профиля памяти
│   ├── SkillCard.astro     # Карточка навыка (статическая, для SSR)
│   ├── StatsDashboard.astro# Дашборд метрик + рефлексия
│   ├── MeshDashboard.astro # Mesh topology/traffic/health (task_053)
│   ├── MeshRouterPanel.astro# Mesh router (task_043)
│   ├── EscalationPanel.astro# HITL эскалация (task_049)
│   ├── AgentCardPanel.astro# A2A Agent Card view/import/discover (task_088)
│   ├── CapabilityRegistryPanel.astro# Peer-агенты: trust/health/capabilities, Touch/Remove (task_089)
│   ├── DiscoveryPanel.astro# Discovery-источники + discovered peer-агенты, Refresh + diff (task_090)
│   ├── TrustAdmissionPanel.astro# Trust admission policy status + dry-run (task_091)
│   └── MeshProfilePanel.astro# Active profile, effective backends, live backend health, degradation policy (task_092)
│   └── MeshObservabilityPanel.astro# Mesh counters + recent traces/logs (task_093)
│   └── BackupPanel.astro# Backup create / verify / restore / delete (task_094)
│   └── SloPanel.astro# SLO summary + per-vertical objectives, violations, ack (task_094)
│   └── QuotasPanel.astro# Quota status per scope (Agent/Skill/User/Tenant) + counters + limits (task_095)
│   └── RolloutPanel.astro# Config/policy rollout state + history + apply/promote/rollback (task_095)
│   └── SecurityOpsPanel.astro# Vulnerabilities + security events + compliance reports (task_095)
├── pages/
│   ├── index.astro         # Чат
│   ├── skills.astro        # Список + создание/редактирование/улучшение
│   ├── profile.astro       # Профиль памяти
│   ├── config.astro        # Настройки
│   ├── stats.astro         # Статистика
│   ├── memmesh.astro       # Mesh dashboard + router + escalation
│   ├── observability.astro # Mesh counters + recent traces/logs (task_093)
│   ├── ops.astro           # Backup + SLO операционная панель (task_094)
│   └── a2a.astro           # A2A Agent Card (локальная карта + import/discover)
├── lib/
│   └── api.ts              # Клиент Hercules Web API + DTO
└── styles/
    └── global.css          # Tailwind v4 entry, тёмная тема
```

## Backend API

UI дёргает следующие эндпоинты (см. `src/lib/api.ts`):

| Метод  | Путь                            | Назначение                       |
| ------ | ------------------------------- | -------------------------------- |
| POST   | `/api/chat`                     | Отправка сообщения агенту        |
| GET    | `/api/skills`                   | Список навыков                   |
| GET    | `/api/skills/{id}`              | Детали навыка                    |
| POST   | `/api/skills`                   | Создание навыка                  |
| PUT    | `/api/skills/{id}`              | Обновление навыка                |
| POST   | `/api/skills/{id}/improve`      | Запуск улучшения ИИ              |
| GET    | `/api/memory/profile`           | Чтение профиля памяти            |
| PUT    | `/api/memory/profile`           | Запись профиля памяти            |
| POST   | `/api/memory/reset`             | Сброс памяти                     |
| GET    | `/api/stats`                    | Метрики и активность по дням     |
| GET    | `/api/reflect`                  | Запуск рефлексии                 |
| GET    | `/api/config`                   | Чтение конфигурации              |
| PUT    | `/api/config`                   | Полная замена конфигурации       |
| PATCH  | `/api/config`                   | Частичное обновление             |
| GET    | `/api/mesh/router/routes`       | Кандидаты mesh-роутера           |
| GET    | `/api/mesh/router/health`       | Health peer-агентов              |
| GET    | `/api/mesh/circuits`            | Состояние circuit-breaker'ов     |
| GET    | `/api/mesh/dashboard`           | Сводный mesh dashboard           |
| GET    | `/api/mesh/topology`            | Топология mesh                   |
| GET    | `/api/mesh/health`              | Здоровье mesh-агентов            |
| GET    | `/api/mesh/denials`             | Policy denials                   |
| GET    | `/api/mesh/skills/heatmap`      | Heatmap использования навыков    |
| GET    | `/api/mesh/eval/summary`        | Сводка mesh eval-прогонов        |
| GET    | `/api/mesh/agents`              | Список всех peer-агентов          |
| GET    | `/api/mesh/agents/{id}`         | Полная запись peer-агента         |
| GET    | `/api/mesh/agents/{id}/health`  | Health/trust/cost/latency агента  |
| POST   | `/api/mesh/agents/{id}/touch`   | Heartbeat (обновить last_seen)    |
| DELETE | `/api/mesh/agents/{id}`         | Удалить агента из реестра         |
| GET    | `/api/mesh/capabilities?agentId=X` | Capabilities конкретного агента |
| GET    | `/api/mesh/capabilities/{name}` | Агенты с заданной capability      |
| GET    | `/api/mesh/capabilities/search` | Семантический поиск по phrase     |
| GET    | `/api/mesh/discovery/sources`   | Список discovery-источников       |
| GET    | `/api/mesh/discovery/agents`    | Найденные peer-агенты (с кэшем)   |
| POST   | `/api/mesh/discovery/refresh`   | Принудительный refresh discovery  |
| GET    | `/api/a2a/agent-card`           | Локальная A2A Agent Card          |
| GET    | `/api/a2a/agent-card/from`      | Import remote Agent Card по URL  |
| POST   | `/api/a2a/discover`             | Batch discover по списку URL     |
| POST   | `/api/a2a/agent-card/publish`   | Принудительная публикация        |
| GET    | `/api/mesh/policy/status`       | Текущая trust admission policy    |
| POST   | `/api/mesh/policy/dry-run`      | Dry-run evaluation без отправки   |
| GET    | `/api/mesh/profiles`            | Список mesh-профилей + активный   |
| GET    | `/api/mesh/profiles/{name}`     | Полное определение профиля        |
| GET    | `/api/mesh/profiles/{name}/backends` | Effective backends профиля    |
| GET    | `/api/mesh/backend-status`      | Live health всех mesh-бэкендов    |
| GET    | `/api/mesh/backend-status/{role}` | Live health конкретного бэкенда |
| GET    | `/api/mesh/observability/status`   | Mesh observability status + counters |
| GET    | `/api/mesh/observability/config`   | Mesh observability config (enabled) |
| GET    | `/api/mesh/observability/counters` | Только counters: totals + byCapability + byPeer |
| GET    | `/api/mesh/observability/traces?limit=N` | Список последних завершённых traces (task_093) |
| GET    | `/api/mesh/observability/logs?limit=N&level=info` | Список последних log-entries (task_093) |
| GET    | `/api/backups`                       | Список backup-архивов (task_094) |
| POST   | `/api/backups`                       | Создать новый backup (task_094) |
| POST   | `/api/backups/{id}/restore`          | Восстановить из архива (task_094) |
| GET    | `/api/backups/{id}/verify`           | Проверить целостность архива (task_094) |
| DELETE | `/api/backups/{id}`                  | Удалить backup-архив (task_094) |
| GET    | `/api/slos`                          | Сводка по SLO verticals (task_094) |
| GET    | `/api/slos/{vertical}/definition`    | SLO-определение вертикали (task_094) |
| GET    | `/api/slos/{vertical}`               | Текущий SLO-статус вертикали (task_094) |
| GET    | `/api/slos/{vertical}/report`        | Полный SLO-отчёт + compliance (task_094) |
| POST   | `/api/slos/{vertical}/ack/{violationId}` | Ack конкретного нарушения (task_094) |
| POST   | `/api/slos/{vertical}/ack`           | Ack всех нарушений вертикали (task_094) |
| GET    | `/api/quotas?scope=Agent&scopeId=…`  | Quota status + counters по scope (task_095) |
| GET    | `/api/quotas/{scope}/{scopeId}`      | Quota status + counters по scope+id (task_095) |
| GET    | `/api/quotas/{scope}/{scopeId}/{type}` | Single quota status (task_095) |
| GET    | `/api/quotas/rate-limit`             | Rate limit info для HTTP headers (task_095) |
| GET    | `/api/rollout/state`                 | Текущее состояние rollout (task_095) |
| GET    | `/api/rollout/bundle/{id}`           | Bundle по ID (task_095) |
| POST   | `/api/rollout/apply`                 | Применить config/policy bundle (task_095) |
| POST   | `/api/rollout/promote`               | Продвинуть bundle на следующую стадию (task_095) |
| POST   | `/api/rollout/rollback`              | Откатиться к last-known-good (task_095) |
| GET    | `/api/security/vulnerabilities`      | Список уязвимостей с фильтрами (task_095) |
| GET    | `/api/security/vulnerabilities/summary` | Сводка по уязвимостям (task_095) |
| GET    | `/api/security/vulnerabilities/{id}` | Конкретная уязвимость (task_095) |
| PATCH  | `/api/security/vulnerabilities/{id}/status` | Обновить статус уязвимости (task_095) |
| GET    | `/api/security/events`               | Security-relevant audit events (task_095) |
| GET    | `/api/security/compliance/{standard}`| Compliance report (SOC2/ISO27001/GDPR/HIPAA) (task_095) |

Все запросы отправляют заголовок `X-Api-Key: $PUBLIC_API_KEY`. Backend CORS по умолчанию разрешает `http://localhost:4321` и `http://127.0.0.1:4321` (см. `WebApi.AllowedCorsOrigins` в `appsettings.json`).

## Лицензия

См. корневой [LICENSE](../../LICENSE) репозитория.
