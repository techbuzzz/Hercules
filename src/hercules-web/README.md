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
│   └── DiscoveryPanel.astro# Discovery-источники + discovered peer-агенты, Refresh + diff (task_090)
├── pages/
│   ├── index.astro         # Чат
│   ├── skills.astro        # Список + создание/редактирование/улучшение
│   ├── profile.astro       # Профиль памяти
│   ├── config.astro        # Настройки
│   ├── stats.astro         # Статистика
│   ├── memmesh.astro       # Mesh dashboard + router + escalation
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

Все запросы отправляют заголовок `X-Api-Key: $PUBLIC_API_KEY`. Backend CORS по умолчанию разрешает `http://localhost:4321` и `http://127.0.0.1:4321` (см. `WebApi.AllowedCorsOrigins` в `appsettings.json`).

## Лицензия

См. корневой [LICENSE](../../LICENSE) репозитория.
