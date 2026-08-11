# hercules-web

Web UI для самообучающегося микро-агента **Hercules**: чат, навыки, профиль памяти, конфигурация и статистика. Построен на [Astro](https://astro.build) 6 + [Tailwind CSS](https://tailwindcss.com) v4, общается с backend через JSON API.

## Требования

- Node.js **≥ 22.12**
- Запущенный [Hercules.WebApi](../agent/Hercules.WebApi) бэкенд (по умолчанию `http://localhost:5000`)

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
| `PUBLIC_API_BASE` | `http://localhost:5000`      | Базовый URL backend API                 |
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
│   └── StatsDashboard.astro# Дашборд метрик + рефлексия
├── pages/
│   ├── index.astro         # Чат
│   ├── skills.astro        # Список + создание/редактирование/улучшение
│   ├── profile.astro       # Профиль памяти
│   ├── config.astro        # Настройки
│   └── stats.astro         # Статистика
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

Все запросы отправляют заголовок `X-Api-Key: $PUBLIC_API_KEY`. Backend CORS по умолчанию разрешает `http://localhost:4321` и `http://127.0.0.1:4321` (см. `WebApi.AllowedCorsOrigins` в `appsettings.json`).

## Лицензия

См. корневой [LICENSE](../../LICENSE) репозитория.
