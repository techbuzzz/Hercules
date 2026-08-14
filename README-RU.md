<p align="center">
  <img src="assets/branding/logo.svg" alt="Hercules" width="560" />
</p>

<p align="center">
  <b>Самообучающийся ИИ-агент на C# / .NET 10</b><br/>
  Создаёт навыки из опыта · улучшает их в процессе использования · помнит контекст между сессиями · объединяет агенты в mesh-сеть
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-15-239120?logo=csharp&logoColor=white" />
  <img alt="Astro" src="https://img.shields.io/badge/Astro-Frontend-FF5D01?logo=astro&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/License-MIT-yellow.svg" />
  <img alt="Status" src="https://img.shields.io/badge/status-active-success.svg" />
</p>

---

**Hercules** — самообучающийся микроагент, воспроизводящий ключевые *self-improving* характеристики
агентов «hermes-стиля» (Nous Research) в запускаемом форм-факторе — и расширяющий их
песочницей для исполнения кода, экосистемой инструментов, mesh-сетью между агентами
и оперативной защитой для edge/IoT-развертываний.

Агент **создаёт навыки из опыта**, **улучшает их в процессе использования**, **сохраняет знания между сессиями**,
строит углубляющуюся модель пользователя, **безопасно исполняет код**, **вызывает внешние инструменты** и
**образует mesh-сети с агентами-партнёрами**. Поддерживает **YandexGPT**, **Ollama Cloud** и **Ollama Local**
через единый OpenAI-совместимый интерфейс (`Microsoft.Extensions.AI`).

---

## ✨ Возможности

| Подсистема                      | Что делает                                                                                                                       |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| **Self-Improving Skill System** | Автоматически предлагает создать навык при повторении запроса (>2 раз), версионирует навыки, улучшает их при низком success rate  |
| **Long-term Memory**            | Многослойная память (рабочая / эпизодическая / долговечные факты); профиль, предпочтения, сущности, контекст в Markdown          |
| **Reflection Engine**           | Самоанализ после сессии или каждые N команд — что хорошо/плохо/что улучшить; включает анализ трасс sandbox-выполнений           |
| **Skill Router**                | Семантическая маршрутизация: embedding-скорер, лексический скорер, совместимость по схеме, историческое качество, политика      |
| **Skill Marketplace**           | Упаковка, подпись и валидация навыков; управление жизненным циклом (создание → оценка → депрекация) с оценкой качества            |
| **Multi-Role LLM Routing**     | `main` / `code_writer` / `reflector` маршрутизируются в разные кортежи (провайдер, модель, температура)                          |
| **Sandboxed Code Execution**    | C# file-based приложения в 3-уровневом sandbox (regex pre-scan → изолированная temp-папка → POSIX `ulimit` wrapper); сеть запрещена |
| **Tool Ecosystem**              | LLM-вызываемые инструменты: `http` (allow-list доменов), `execute_code`, `a2a` (JSON-RPC 2.0), `mcp` (Model Context Protocol)   |
| **Agent Mesh**                  | Mesh-сеть между агентами: реестр возможностей, маршрутизация по интентам, circuit breaker, fan-out, верификационный пайплайн     |
| **HerculesBus**                 | Внутренняя шина событий с in-memory, HTTP и SQLite бэкендами                                                                     |
| **Гибридное хранилище**         | Файлы (Markdown + JSON) для навыков и памяти + SQLite для логов, метрик и sandbox-выполнений + Redis/NATS/PostgreSQL бэкенды     |
| **Наблюдаемость**               | Интеграция OpenTelemetry: трейсинг, метрики, структурные логи                                                                   |
| **Security Operations**         | Ротация идентичности флота, X.509 сертификаты, подпись пакетов (HMAC-SHA256), отчёт об уязвимостях, экспорт аудита безопасности   |
| **Операционная защита**          | Бюджетные ограничения, rate limiting и квоты, аудит-лог, маскирование секретов, редакция персональных данных, шифрованный бэкап   |
| **Edge-развертывание**           | Развертывание на Raspberry Pi с Docker (ARM64/Alpine), регистрация устройств, симуляция сенсоров, шаблоны флота                   |
| **Офлайн-устойчивость**          | Outbox-очередь, мониторинг сети, синхронизация при восстановлении, детерминированный fallback при недоступности LLM             |
| **Hot-Reload конфигурации**     | Смена LLM-провайдеров, системного промпта и порогов через Web UI или API без перезапуска сервера                               |
| **Мульти-провайдер LLM**        | YandexGPT (основной), Ollama Cloud / Local, LM Studio — через единый OpenAI-совместимый интерфейс с автоматическим fallback     |
| **Интерфейсы**                  | CLI (REPL, основной) + Telegram-бот (вторичный) + Web API (35 контроллеров) + Astro SPA                                         |

---

## 🏗️ Архитектура

```
src/agent/                         # Основной проект агента
├── Program.cs                      # Точка входа, DI и настройка конфигурации
├── appsettings.json                # Конфигурация провайдеров и порогов агента
├── Agent/                          # Ядро агента
│   ├── AgentCore.cs                # Главный цикл обработки запросов (tool-aware, multi-role)
│   ├── SkillRouter.cs              # Маршрутизация навыков (PhraseReceivers + семантический скорер)
│   ├── SkillManager.cs             # CRUD + версионирование навыков (через LLM)
│   ├── ReflectionEngine.cs         # Самоанализ (sandbox traces), отчёты рефлексии
│   ├── MemoryManager.cs            # Долгосрочная память, профиль пользователя
│   └── WebApiAdapter.cs            # Адаптер ядра для Web API + DTO
├── LLM/                            # LLM-слой (Microsoft.Extensions.AI)
│   ├── ILLMClient.cs               # Унифицированный интерфейс провайдера (multi-role)
│   ├── ChatClientLLMClient.cs      # База над IChatClient
│   ├── YandexGPTClient.cs          # YandexGPT (OpenAI-совместимый endpoint)
│   ├── LocalLLMClient.cs            # Ollama Cloud/Local, LM Studio
│   ├── LlmClientFactory.cs         # Фабрика клиентов по имени провайдера
│   ├── ResilientLLMClient.cs       # Отказоустойчивость + role routing
│   ├── RoleRouter.cs                # v2: role → ILLMClient резолвер
│   ├── ProviderHealthChecker.cs    # Мониторинг здоровья провайдеров
│   ├── ProviderCapabilityDetector.cs # Определение возможностей провайдеров
│   ├── JsonRepair/                 # Утилиты исправления JSON-ответов
│   └── Providers/                 # OpenAI-совместимый, LM Studio клиенты
├── Skills/                         # Жизненный цикл навыков и маркетплейс
│   ├── SkillMarketplace.cs         # Упаковка, подпись, валидация, распространение навыков
│   ├── SkillPackager.cs            # Движок упаковки навыков
│   ├── SkillLifecycleService.cs    # Создание → оценка → депрекация
│   ├── SkillEvaluationEngine.cs    # Оценка качества и тестирование
│   ├── SkillDeprecationManager.cs  # Грациозная депрекация навыков
│   ├── AgentTemplateManager.cs     # Управление IoT-шаблонами
│   ├── EmbeddingSkillRouter.cs    # Семантическая маршрутизация навыков
│   ├── Eval/ Quality/ Package/ Routing/  # Подмодули
├── CodeExecution/                  # Безопасное исполнение C# кода (v2 Stage 2)
│   ├── ICodeExecutor.cs            # Контракт
│   ├── DotnetFileBasedExecutor.cs  # `dotnet run --file` реализация
│   ├── DangerousCodeScanner.cs     # Pre-execution regex scan (25+ паттернов)
│   └── SandboxOptions.cs           # ulimit + таймауты + allow-list
├── Tools/                          # Экосистема инструментов (v2 Stage 3)
│   ├── ITool.cs                    # Контракт инструмента
│   ├── ToolRegistry.cs             # LLM prompt injection
│   ├── HttpTool.cs                 # GET/POST/PUT/DELETE + allow-list
│   ├── A2AClient.cs               # JSON-RPC 2.0 agent-to-agent
│   ├── McpClient.cs                # Model Context Protocol клиент
│   ├── CodeExecutionTool.cs        # Адаптер: ICodeExecutor → ITool
│   ├── WasmToolAdapter.cs         # WASM-мост для инструментов
│   └── Approval/ Grants/ Policy/ Registry/  # Управление инструментами
├── Mesh/                            # Mesh-сеть между агентами (36+ подмодулей)
│   ├── AgentManifest.cs            # Идентичность и возможности агента
│   ├── CapabilityRegistry.cs        # Отслеживание возможностей пиров
│   ├── MeshRouter.cs               # Маршрутизация по интентам в mesh
│   ├── IntentRouter.cs             # Делегирование с наблюдаемостью
│   ├── CircuitBreaker.cs           # Устойчивость вызовов к пирам
│   ├── SharedMemorySync.cs         # Межагентская синхронизация памяти
│   ├── DistributedReflection.cs    # Кросс-агентная рефлексия
│   ├── A2A/ Abstractions/ Aggregation/ Audit/ Auth/ Backend/
│   ├── Backends/ Budget/ Dashboard/ Discovery/ Escalation/
│   ├── Eval/ Observability/ Policy/ Profiles/ Resilience/
│   ├── Router/ Schema/ Transport/ Verification/
├── Mcp/                             # Model Context Protocol
│   ├── McpClientService.cs          # Управление подключениями MCP-клиента
│   ├── McpServerHost.cs            # Хостинг MCP-сервера
│   ├── HerculesMcpServerTool.cs    # Экспорт инструментов агента через MCP
│   └── McpToolAdapter.cs           # MCP ↔ ITool мост
├── WasmSandbox/                     # WASM-песочница
│   ├── IWasmSandbox.cs              # Контракт
│   ├── WasmtimeSandbox.cs           # Рантайм Wasmtime (NuGet: Wasmtime 44.0.0)
│   ├── WasmTool.cs                  # WASM → ITool адаптер
│   └── Compilation/                # C# и passthrough компиляторы
├── Security/                        # Операции безопасности флота
│   ├── Ротация идентичности флота, сертификаты, подпись пакетов,
│   │   отчёт об уязвимостях, экспорт аудита безопасности
├── Edge/                            # Развертывание на Raspberry Pi
├── Offline/                         # Офлайн-устойчивость (outbox, синхронизация)
├── Degradation/                     # Local-first деградация + детерминированный fallback
├── Backup/                          # Шифрованный бэкап и восстановление
├── Slo/                             # Операционные SLO
├── Quotas/                          # Rate limiting и квоты
├── Observability/                   # OpenTelemetry (трейсинг, метрики, логи)
├── Reflection/                      # Сервис самоулучшения + хранилище предложений
├── Context/                         # Сборка контекста + суммаризация трасс
├── Cache/                           # Единый кэш с уровнями чувствительности
├── Budget/                          # Бюджетные ограничения
├── Audit/                           # Аудит-лог + хеширование payload
├── Redaction/                        # Редакция персональных данных
├── Config/                          # Модели конфигурации + rollout
│   ├── AppConfig.cs                 # Полная конфигурация (Llm/Storage/Agent/CodeExecution/Http/Mcp/A2A/Roles/Mesh/...)
│   ├── SecurityOpsConfig.cs
│   ├── SecretMaskingService.cs
│   ├── RuntimeConfigStore.cs
│   └── Rollout/                     # Стейджированные конфигурационные бандлы
├── Contracts/                      # API/DTO контракты
├── Storage/                         # Гибридное хранилище
│   ├── FileSkillRepository.cs       # Skills/ — файлы навыков
│   ├── MemoryStore.cs              # Memory/ — Markdown-память
│   ├── SqliteSessionStore.cs        # SQLite: sessions, logs, metrics, sandbox_executions
│   ├── BudgetService.cs
│   └── AuditLogService.cs
├── Memory/                          # Многослойная система памяти
│   └── Layers/ (WorkingMemory, DurableFacts, Episodic)
├── HerculesBus/                     # Внутренняя шина событий
│   ├── Bus.cs                       # Pub/sub ядро
│   └── Core/ Http/ InMemory/ Sqlite/  # Множественные бэкенды
├── Tasks/                           # Жизненный цикл долговременных задач
├── Lifecycle/                       # Управление жизненным циклом агента
├── Simulation/                      # Симуляция шаблонов (сенсоры, сценарии отказов)
├── Fleet/                           # Управление шаблонами флота
├── Loop/                            # Утилиты циклов
├── CLI/
│   ├── ConsoleUI.cs                 # REPL-цикл (Spectre.Console)
│   └── BenchmarkRunner.cs
├── Telegram/
│   └── TelegramBot.cs              # Telegram-бот (long polling)
└── data/                            # Runtime-данные (git-ignored)
    ├── Skills/ Memory/ sessions.db  runtime-config.json
```

Дополнительные проекты:

```
src/agent/Hercules.WebApi/           # ASP.NET Core Minimal API (REST), порт :5000
├── Program.cs                         # DI + CORS + middleware, переиспользует ядро
├── Auth/                              # ApiKeyMiddleware, RateLimitMiddleware,
│                                      # RequestBodyLimitMiddleware, PeerAuthMiddleware
├── Config/                            # WebApiConfig + RuntimeConfigHostedService (hot-reload)
└── Controllers/                       # 35 контроллеров: Chat, Skills, SkillLifecycle,
                                       # SkillQuality, SkillHarness, SkillManifest,
                                       # Marketplace, Memory, Stats, Config, Rollout,
                                       # Mesh, MeshProfiles, MeshObservability, Lifecycle,
                                       # Budget, Quotas, Audit, Llm, A2A, Backups,
                                       # FleetTemplates, Grants, Simulation, SLOs,
                                       # Approvals, Escalations, Observability,
                                       # SelfImprovement, TaskProgress, Context, Cache,
                                       # ToolRegistry, MCP, Template

src/hercules-web/                    # Фронтенд на Astro + TailwindCSS, порт :4321
├── src/lib/api.ts                    # Типизированный клиент Web API (35+ эндпоинтов)
├── src/layouts/Layout.astro         # Базовый макет (тёмная тема, навигация)
├── src/components/                  # ChatBox, SkillCard, ProfileEditor, ConfigEditor,
│                                    # StatsDashboard, MeshDashboard, MeshRouterPanel,
│                                    # EscalationPanel
└── src/pages/                       # index / skills / profile / stats / config / memmesh
```

Развертывание:

```
deploy/raspberry-pi/                # Edge-развертывание для Raspberry Pi
├── Dockerfile                      # Многостадийная сборка ARM64 (Alpine, non-root)
├── docker-compose.yml              # Host-сеть, привилегированный режим (GPIO/Wi-Fi)
├── appsettings.edge.json           # Конфигурация, оптимизированная для edge
├── first-boot.sh                   # Скрипт первичной настройки
└── provision.env.template         # Шаблон окружения для учётных данных

templates/                          # IoT-шаблоны сценариев
├── greenhouse/                     # Мониторинг и управление теплицей
├── vending/                        # Управление сетью вендинговых автоматов
├── server-room/                    # Мониторинг серверной
└── cold-chain/                     # Мониторинг холодовой цепи
```

Все runtime-данные складываются в папку `data/`:

```
data/
├── Skills/                    # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/                    # user_profile.md, preferences.md, entities.md, context_{date}.md
├── Templates/                 # IoT-шаблоны агентов (.agenttemplate)
├── FleetTemplates/            # Шаблоны развёртывания флота (.fleettemplate)
├── runtime-config.json        # Конфигурация с hot-reload (через /config API)
└── sessions.db                # SQLite: сессии, взаимодействия, метрики, sandbox-выполнения
```

---

## 🚀 Установка и запуск

### Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 22+](https://nodejs.org/) (для веб-фронтенда)

### Сборка

```bash
dotnet restore
dotnet build
```

### Публикация (для развёртывания конечному пользователю)

```bash
# Бэкенд
dotnet publish src/agent/Hercules.WebApi -c Release -o ./dist/webapi

# Фронтенд
cd src/hercules-web
npm install
npm run build   # статика попадает в src/hercules-web/dist
```

После публикации достаточно запустить `dist/webapi/Hercules.WebApi` и раздать статику
фронтенда любым статическим сервером, например `npx serve src/hercules-web/dist -p 4321`.
Все пользовательские данные (навыки, память, БД и runtime-конфигурация) живут в папке `data/`,
которую легко держать вне репозитория.

### Запуск CLI (основной режим)

```bash
dotnet run --project src/agent/Hercules
```

### Запуск Telegram-бота

```bash
dotnet run --project src/agent/Hercules -- --telegram
```

(предварительно укажите `Telegram:BotToken` в `appsettings.json`)

### Запуск Web API (REST-сервер)

```bash
dotnet run --project src/agent/Hercules.WebApi
```

Сервер поднимается на `http://localhost:5000`. Ядро агента (`AgentCore`) переиспользуется
через адаптер `WebApiAdapter` — отдельной логики агента в Web API нет.

### Запуск CLI через основной проект

```bash
dotnet run --project src/agent/Hercules -- --cli   # REPL-режим
dotnet run --project src/agent/Hercules            # то же самое (CLI по умолчанию)
```

---

## 🌐 Web API

ASP.NET Core Minimal API с 35 контроллерами. Все ответы — JSON (UTF-8, camelCase). Защита — заголовок
`X-Api-Key` (значение из `WebApi:ApiKey`, по умолчанию `dev-local-key`). CORS открыт для
локального фронтенда (`http://localhost:4321`, `http://localhost:3000`). Каждое взаимодействие
логируется в SQLite (`data/sessions.db`).

### Основные эндпоинты

| Метод  | Маршрут                    | Описание                                                     |
| ------ | -------------------------- | ------------------------------------------------------------ |
| `GET`  | `/api/health`              | Проверка живости (без ключа)                                 |
| `POST` | `/api/chat`                | Отправить сообщение агенту → ответ + режим/уверенность/навык |
| `GET`  | `/api/skills`              | Список навыков                                               |
| `POST` | `/api/skills`              | Создать навык вручную; `?ai=true` — сгенерировать через LLM  |
| `GET`  | `/api/skills/{id}`         | Детали навыка (метаданные + промпт)                          |
| `PUT`  | `/api/skills/{id}`         | Обновить навык (триггеры/промпт/описание) → новая версия     |
| `POST` | `/api/skills/{id}/improve` | Улучшить навык через LLM → новая версия                      |
| `GET`  | `/api/memory/profile`      | Профиль долговременной памяти (Markdown)                     |
| `PUT`  | `/api/memory/profile`      | Перезаписать профиль памяти                                  |
| `POST` | `/api/memory/reset`        | Сбросить долговременную память                               |
| `GET`  | `/api/reflect`             | Запустить рефлексию → Markdown-отчёт                         |
| `GET`  | `/api/stats`               | Метрики: всего, навык/прямой, успешность, по дням            |
| `GET`  | `/api/config`              | Текущая runtime-конфигурация                                 |
| `PUT`  | `/api/config`              | Полная замена конфигурации                                   |
| `PATCH`| `/api/config`              | Частичное обновление конфигурации (merge patch)              |

### Расширенные эндпоинты

| Метод  | Маршрут                                    | Описание                                            |
| ------ | ------------------------------------------ | --------------------------------------------------- |
| `GET`  | `/api/skills/{id}/lifecycle`               | Статус жизненного цикла навыка                      |
| `POST` | `/api/skills/{id}/lifecycle/deprecate`     | Депрекация навыка                                   |
| `GET`  | `/api/skills/{id}/quality`                 | Оценка качества навыка                              |
| `GET`  | `/api/marketplace`                          | Обзор маркетплейса навыков                          |
| `POST` | `/api/marketplace/publish`                 | Опубликовать навык в маркетплейсе                    |
| `GET`  | `/api/mesh/status`                         | Статус mesh-сети                                    |
| `GET`  | `/api/mesh/peers`                          | Список подключённых пиров                            |
| `POST` | `/api/mesh/delegate`                      | Делегировать задачу пиру                            |
| `GET`  | `/api/mesh/observability/status`           | Конфигурация наблюдаемости mesh                     |
| `GET`  | `/api/budget`                              | Текущий статус бюджета                               |
| `GET`  | `/api/quotas`                              | Использование квот                                  |
| `GET`  | `/api/audit`                               | Записи аудита                                        |
| `GET`  | `/api/backups`                              | Список бэкапов                                      |
| `POST` | `/api/backups`                              | Создать бэкап                                       |
| `POST` | `/api/backups/{id}/restore`                | Восстановить из бэкапа                               |
| `GET`  | `/api/slos`                                | Статус соблюдения SLO                                |
| `GET`  | `/api/lifecycle`                            | Статус жизненного цикла агента                       |
| `GET`  | `/api/tasks/{id}/progress`                 | Прогресс долговременной задачи                      |
| `GET`  | `/api/context`                              | Текущая сборка контекста                             |
| `DELETE`|`/api/cache`                                | Очистить кэш                                         |
| `GET`  | `/api/tools`                                | Список доступных инструментов                        |
| `POST` | `/api/tools/{name}/execute`                | Выполнить инструмент                                 |
| `GET`  | `/api/a2a/status`                          | Статус A2A (agent-to-agent)                          |
| `GET`  | `/api/rollout/status`                      | Статус rollout конфигурации                          |
| `GET`  | `/api/self-improvement/proposals`          | Предложения самоулучшения                           |
| `POST` | `/api/simulations/{template}`              | Запустить симуляцию шаблона                          |

Пример:

```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"message":"какая погода в Москве?"}'
```

Конфигурация Web API (`src/agent/Hercules.WebApi/appsettings.json`):

```jsonc
"WebApi": {
  "ApiKey": "dev-local-key",                  // пустая строка → доступ без ключа
  "AllowedCorsOrigins": [ "http://localhost:4321", "http://localhost:3000" ]
}
```

---

## 🎨 Веб-интерфейс (Astro)

Минималистичный SPA на **Astro + TailwindCSS** (тёмная тема, моноширинные блоки кода).
Лежит в каталоге `src/hercules-web/`.

| Страница   | Назначение                                                                               |
| ---------- | ---------------------------------------------------------------------------------------- |
| `/`        | Чат с агентом (бейджи режима/уверенности/провайдера, эффект печати, подсказки о навыках) |
| `/skills`  | Список навыков, создание и улучшение через ИИ, редактирование, жизненный цикл и качество |
| `/profile` | Редактор профиля долговременной памяти + сброс                                           |
| `/config`  | **Редактор конфигурации агента** — LLM-провайдеры, системный промпт, пороги, инструменты |
| `/stats`   | Дашборд метрик, соотношение навык/прямой, активность по дням, рефлексия                  |
| `/memmesh` | Дашборд mesh-сети — пиры, маршрутизация, наблюдаемость                                    |

Компоненты: `ChatBox`, `SkillCard`, `ProfileEditor`, `ConfigEditor`, `StatsDashboard`,
`MeshDashboard`, `MeshRouterPanel`, `EscalationPanel`. Клиент API — `src/lib/api.ts`.

### Запуск фронтенда

```bash
cd src/hercules-web
npm install
npm run dev        # dev-сервер на http://localhost:4321
```

Адрес бэкенда и ключ настраиваются через переменные окружения (файл `src/hercules-web/.env`):

```bash
PUBLIC_API_BASE=http://localhost:5000
PUBLIC_API_KEY=dev-local-key
```

> **Hot-reload конфигурация:** не обязательно править `appsettings.json` до запуска.
> Откройте страницу `/config`, вставьте ключи LLM-провайдера и сохраните — настройки
> применятся сразу, без перезагрузки сервера, и сохранятся в `data/runtime-config.json`.

### Полный локальный запуск (два терминала)

```bash
# Терминал 1 — бэкенд
dotnet run --project src/agent/Hercules.WebApi      # → :5000

# Терминал 2 — фронтенд
cd src/hercules-web && npm run dev                  # → :4321
```

Откройте `http://localhost:4321`.

---

## ⚙️ Конфигурация (`appsettings.json`)

```jsonc
{
  "Llm": {
    "Provider": "yandexgpt",                 // активный провайдер
    "Fallback": ["ollama-cloud", "ollama-local"], // порядок fallback
    "Roles": {                               // v2: мульти-ролевая маршрутизация
      "main": { "Provider": "yandexgpt", "Model": "yandexgpt", "Temperature": 0.6 },
      "code_writer": { "Provider": "ollama-local", "Model": "codellama", "Temperature": 0.2 },
      "reflector": { "Provider": "ollama-local", "Model": "llama3.1", "Temperature": 0.8 }
    },
    "YandexGpt": {
      "Endpoint": "https://llm.api.cloud.yandex.net/v1",
      "ApiKey": "<IAM или API-ключ>",
      "FolderId": "<folder id Yandex Cloud>",
      "Model": "yandexgpt",                  // станет gpt://{folderId}/yandexgpt/latest
      "Temperature": 0.6,
      "MaxTokens": 2000
    },
    "OllamaCloud": {
      "Endpoint": "https://ollama.com/v1",
      "ApiKey": "<ключ Ollama Cloud>",
      "Model": "gpt-oss:120b"
    },
    "OllamaLocal": {
      "Endpoint": "http://localhost:11434/v1",
      "ApiKey": "",                          // локально ключ не нужен
      "Model": "llama3.1"
    }
  },
  "Agent": {
    "SkillCreationThreshold": 3,             // повторов до предложения навыка
    "SkillImprovementThreshold": 0.6,        // порог success_rate для улучшения
    "SkillEvaluationWindow": 5,              // окно оценки навыка
    "ReflectionEveryNCommands": 10           // авто-рефлексия каждые N команд
  },
  "CodeExecution": {                         // v2: безопасное исполнение кода
    "Enabled": true,
    "TimeoutSeconds": 30,
    "MaxFileSizeBytes": 10485760,             // 10 МБ
    "AllowNetwork": false,
    "MaxCodeSizeBytes": 102400                // 100 КБ
  },
  "Http": {                                  // v2: конфигурация HTTP-инструмента
    "AllowedDomains": ["*"],
    "RequestsPerMinute": 60,
    "TimeoutSeconds": 10
  },
  "Mesh": {                                  // mesh-сеть между агентами
    "Enabled": false,
    "Transport": "http",                      // http | grpc | nats
    "Discovery": "manual",                     // manual | mdns | consul
    "PeerAuth": { "Enabled": false }
  },
  "Telegram": { "Enabled": false, "BotToken": "" }
}
```

> Любой параметр можно переопределить переменными окружения с префиксом `HERCULES_`,
> например: `HERCULES_Llm__Provider=ollama-local`.
>
> В Web-режиме конфигурацию также можно менять через UI (`/config`) или API
> (`PUT`/`PATCH /api/config`) — изменения сохраняются в `data/runtime-config.json`
> и применяются без перезагрузки сервера.

### Провайдеры LLM

Все провайдеры работают через **OpenAI-совместимый интерфейс** и абстракцию
`Microsoft.Extensions.AI` (`IChatClient`). Поддерживаются:

- **YandexGPT** — основной (РФ). Модель передаётся как `gpt://{folderId}/{model}/latest`.
- **Ollama Cloud** — облачный fallback (`https://ollama.com/v1`).
- **Ollama Local / LM Studio** — локальный fallback (`http://localhost:11434/v1`).

Если основной провайдер недоступен, `ResilientLLMClient` автоматически переключается
на следующий из списка `Fallback`. Каждая роль (`main`, `code_writer`, `reflector`)
может использовать отдельный провайдер и модель.

### Бэкенды хранилища

Помимо файлового + SQLite хранилища по умолчанию, Hercules поддерживает:

- **SQLite** — сессии, логи, метрики, sandbox-выполнения, аудит, задачи
- **Redis / Valkey** — бэкенд HerculesBus, кэширование (`StackExchange.Redis`)
- **NATS + JetStream** — mesh-транспорт и шина событий (`NATS.Client`)
- **PostgreSQL** — альтернативный персистентный бэкенд (`Npgsql`)
- **gRPC** — mesh-транспорт (`Grpc.Net.Client`)

---

## 💻 Команды CLI

| Команда                     | Описание                                             |
| --------------------------- | ---------------------------------------------------- |
| `> текст`                   | Прямой запрос к LLM с контекстом профиля             |
| `/skills`                   | Показать все навыки (таблица)                        |
| `/skills create "название"` | Создать навык вручную                                |
| `/skills improve {id}`      | Улучшить навык (новая версия)                        |
| `/memory show`              | Показать профиль пользователя                        |
| `/memory reset`             | Сбросить память                                      |
| `/reflect`                  | Запустить рефлексию вручную                          |
| `/help`                     | Справка                                              |
| `/exit`                     | Выход с сохранением контекста и финальной рефлексией |

## 🤖 Команды Telegram

- `/start` — инициализация
- `/skills` — список навыков
- `/profile` — что агент знает о пользователе
- `/reset` — сброс памяти
- обычный текст — ответ агента

---

## 🔄 Как работает self-improving цикл

1. **Запрос** → загрузка профиля и контекста из памяти
2. **Маршрутизация** → поиск подходящего навыка по PhraseReceivers + семантический скорер (`SkillRouter`)
3. **Ответ LLM** → с активным навыком (skill-prompt) или напрямую (direct)
4. **Исполнение инструментов** → если LLM-ответ содержит action инструмента, выполнить и вернуть результат (до 3 итераций)
5. **Логирование** → input/output/confidence/mode в SQLite
6. **Порог навыка** → если запрос повторился `SkillCreationThreshold` раз → предложение создать навык (с подтверждением)
7. **Порог улучшения** → если `success_rate < SkillImprovementThreshold` → предложение обновить навык
8. **Сохранение памяти** → факты о пользователе, сущности, предпочтения
9. **Рефлексия** → по завершении сессии или каждые N команд; включает анализ трасс sandbox-выполнений

### Принципы

- **Never stop learning** — каждая сессия обогащает память или навыки
- **Explicit improvement loop** — агент сам предлагает исправления
- **Transparent** — пользователь видит все создания/улучшения
- **Human-in-the-loop** — навыки создаются только после подтверждения
- **Versioned** — старые версии навыков не удаляются (`skill.{id}.v{N}.md`)
- **Safe by default** — исполнение кода в песочнице; инструменты требуют allow-list и подтверждение
- **Observable** — трейсы OpenTelemetry, метрики и структурные логи повсюду

---

## 🧪 Тестирование

```bash
dotnet test                          # Запустить все тесты
dotnet test --filter "Phase2Tests"   # Тесты возможностей v2
dotnet test --filter "Phase3Tests"   # Тесты возможностей v3
dotnet test --filter "Phase4Tests"   # Тесты mesh-наблюдаемости
```

Тестовый проект: `tests/Hercules.Agent.Tests/` (xUnit + Moq, 34 каталога тестов).

### Проверка критериев приёмки

| Критерий                      | Как проверить                                        |
| ----------------------------- | ---------------------------------------------------- |
| Навык создаётся автоматически | Повторите один запрос 3 раза → агент предложит навык |
| Навык используется            | После создания — запрос идёт через `навык: ...`      |
| Навык улучшается              | После серии плохих ответов → предложение обновить    |
| Профиль сохраняется           | Перезапуск → `/memory show` помнит факты             |
| Контекст переносится          | Сессия 1: факт → Сессия 2: агент помнит              |
| Reflection запускается        | После `/exit` — вывод Reflection Engine              |
| Код исполняется безопасно     | `execute_code` инструмент → sandbox блокирует опасные паттерны |
| Инструменты вызываются        | `http` инструмент → allow-listed HTTP-запрос         |
| Mesh делегирует задачи        | Настройте пиры → `POST /api/mesh/delegate`           |
| Конфигурация hot-reload      | Изменение через `/config` → применяется без перезапуска |

---

## 🐳 Docker (Raspberry Pi Edge)

```bash
cd deploy/raspberry-pi
cp provision.env.template provision.env   # заполнить учётные данные
docker compose up -d
```

См. `deploy/raspberry-pi/` — Dockerfile (ARM64/Alpine, non-root пользователь), edge-оптимизированная конфигурация и скрипт первичной настройки.

---

## 📦 Зависимости (NuGet)

| Пакет | Версия | Назначение |
| ----- | ------ | ---------- |
| `Microsoft.Extensions.AI` + `OpenAI` | 10.9.0 | AI-абстракции + OpenAI-совместимый SDK |
| `Microsoft.Extensions.Hosting` | 10.0.11 | DI, хостинг, конфигурация |
| `Microsoft.Data.Sqlite` | 10.0.11 | SQLite для сессий, метрик, аудита |
| `Wasmtime` | 44.0.0 | Рантайм WASM-песочницы |
| `ModelContextProtocol` | 2.2.0 | MCP сервер/клиент |
| `YamlDotNet` | 18.1.0 | Парсинг YAML front-matter |
| `Spectre.Console` | 0.57.2 | CLI-интерфейс |
| `Telegram.Bot` | 22.10.2.1 | Telegram-интерфейс |
| `Grpc.Net.Client` | 2.83.0 | gRPC mesh-транспорт |
| `StackExchange.Redis` | 3.1.13 | Redis/Valkey бэкенд |
| `NATS.Client` | 3.1.0 | NATS JetStream + KV бэкенд |
| `Npgsql` | 10.0.3 | PostgreSQL бэкенд |
| `OpenTelemetry` | 1.17.0 | Наблюдаемость (трейсинг + метрики) |

Фронтенд: **Astro 6.4+**, **TailwindCSS 4.3+**, **Node.js 22.12+**

---

## 📚 Документация

> Все документы доступны на двух языках. По умолчанию ссылки ведут на русскую версию.

| Документ                                       | Описание                         |
| ---------------------------------------------- | -------------------------------- |
| [docs/QUICKSTART-RU.md](docs/QUICKSTART-RU.md) · [EN](docs/QUICKSTART-EN.md) | Быстрый старт за несколько минут |
| [docs/ARCHITECTURE-RU.md](docs/ARCHITECTURE-RU.md) · [EN](docs/ARCHITECTURE-EN.md) | Архитектура ядра и интерфейсов |
| [docs/AGENT-MESH-RU.md](docs/AGENT-MESH-RU.md) · [EN](docs/AGENT-MESH-EN.md) | Концепция mesh микро-агентов |
| [docs/IOT-SCENARIOS-RU.md](docs/IOT-SCENARIOS-RU.md) · [EN](docs/IOT-SCENARIOS-EN.md) | B2C/B2B сценарии IoT-развёртываний |
| [docs/ROADMAP-RU.md](docs/ROADMAP-RU.md) · [EN](docs/ROADMAP-EN.md) | План развития и milestone'ы |
| [docs/CONFIGURATION-RU.md](docs/CONFIGURATION-RU.md) · [EN](docs/CONFIGURATION-EN.md) | Полный справочник настроек |
| [docs/API-RU.md](docs/API-RU.md) · [EN](docs/API-EN.md) | Справочник REST Web API |
| [CONTRIBUTING-RU.md](CONTRIBUTING-RU.md) · [EN](CONTRIBUTING-EN.md) | Как внести вклад |
| [CHANGELOG-RU.md](CHANGELOG-RU.md) · [EN](CHANGELOG-EN.md) | История изменений |
| [SECURITY.md](SECURITY.md) | Политика безопасности |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) | Кодекс поведения |

---

## 🤝 Вклад

PR и Issue приветствуются! Перед началом ознакомьтесь с [CONTRIBUTING-RU.md](CONTRIBUTING-RU.md)
и [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Об уязвимостях сообщайте по [SECURITY.md](SECURITY.md).

---

## 📝 Лицензия

[MIT](LICENSE) © 2026 Victor Buzin.