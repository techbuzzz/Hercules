<p align="center">
  <img src="assets/branding/logo.svg" alt="Hercules" width="560" />
</p>

<p align="center">
  <b>Самообучающийся ИИ-агент на C# / .NET 10</b><br/>
  Создаёт навыки из опыта · улучшает их в процессе использования · помнит контекст между сессиями
</p>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" />
  <img alt="C#" src="https://img.shields.io/badge/C%23-15-239120?logo=csharp&logoColor=white" />
  <img alt="Astro" src="https://img.shields.io/badge/Astro-Frontend-FF5D01?logo=astro&logoColor=white" />
  <img alt="License" src="https://img.shields.io/badge/License-MIT-yellow.svg" />
  <img alt="Status" src="https://img.shields.io/badge/status-active-success.svg" />
</p>

---

**Hercules** — компактный самообучающийся микроагент, воспроизводящий ключевые *self-improving* характеристики
агентов «hermes-стиля» (Nous Research) в запускаемом форм-факторе.

Агент **создаёт навыки из опыта**, **улучшает их в процессе использования**, **сохраняет знания между сессиями**
и строит углубляющуюся модель пользователя. Поддерживает **YandexGPT**, **Ollama Cloud** и **Ollama Local**
через единый OpenAI-совместимый интерфейс (`Microsoft.Extensions.AI`).

---

## ✨ Возможности

| Подсистема                      | Что делает                                                                                                                       |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| **Self-Improving Skill System** | Автоматически предлагает создать навык при повторении запроса (>2 раз), версионирует навыки, улучшает их при низком success rate |
| **Long-term Memory**            | Хранит профиль пользователя, предпочтения, сущности и контекст сессий в Markdown; переносит контекст между запусками             |
| **Reflection Engine**           | Самоанализ после сессии или каждые N команд — что хорошо/плохо/что улучшить                                                      |
| **Skill Router**                | Маршрутизация запроса: навык по фразам-приёмникам (phrase_receivers) или прямой ответ LLM                                       |
| **Гибридное хранилище**         | Файлы (Markdown + JSON) для навыков и памяти + SQLite для логов, метрик и аудита sandbox-выполнений                             |
| **Мульти-провайдер LLM**        | YandexGPT (основной), Ollama Cloud / Local, LM Studio — через единый OpenAI-совместимый интерфейс с автоматическим fallback      |
| **Интерфейсы**                  | CLI (REPL, основной) + Telegram-бот (вторичный) + Web API                                                                        |
| **v2 Multi-Role Routing**       | `AppConfig.Roles` маршрутизирует `main` / `code_writer` / `reflector` в разные (provider, model, temperature) кортежи            |
| **v2 Sandboxed Code Execution** | C# file-based apps в 3-уровневом sandbox (regex pre-scan → изолированная temp-папка → POSIX `ulimit` wrapper); сеть запрещена    |
| **v2 Tool Ecosystem**           | LLM-вызываемые tools: `http` (allow-list доменов), `execute_code`, `a2a` (JSON-RPC 2.0), `mcp` (stub — client SDK ETA 2026)      |

---

## 🏗️ Архитектура

```
Hercules/
├── Program.cs                 # Точка входа, DI и настройка конфигурации
├── appsettings.json           # Конфигурация провайдеров и порогов агента
├── Config/
│   └── AppConfig.cs           # Модели конфигурации (Llm/Storage/Agent/CodeExecution/Http/Mcp/A2A/Roles)
├── Agent/                     # Ядро агента
│   ├── AgentCore.cs           # Главный цикл обработки запросов (tool-aware, Stage 4)
│   ├── SkillRouter.cs         # Маршрутизация навыков (PhraseReceivers)
│   ├── SkillManager.cs        # CRUD + версионирование навыков (через LLM)
│   ├── ReflectionEngine.cs    # Самоанализ (sandbox traces), отчёты рефлексии
│   ├── MemoryManager.cs       # Долгосрочная память, профиль пользователя
│   └── WebApiAdapter.cs       # Адаптер ядра для Web API + DTO
├── LLM/                       # LLM-слой (Microsoft.Extensions.AI)
│   ├── ILLMClient.cs          # Унифицированный интерфейс провайдера (multi-role)
│   ├── ChatClientLLMClient.cs # База над IChatClient
│   ├── YandexGPTClient.cs     # YandexGPT (OpenAI-совместимый endpoint)
│   ├── LocalLLMClient.cs      # Ollama Cloud/Local, LM Studio
│   ├── LlmClientFactory.cs    # Фабрика клиентов по имени провайдера
│   ├── ResilientLLMClient.cs  # Отказоустойчивость + role routing
│   └── RoleRouter.cs          # v2: role → ILLMClient резолвер
├── CodeExecution/             # v2: безопасное исполнение C# кода
│   ├── ICodeExecutor.cs       # Контракт
│   ├── SandboxOptions.cs      # ulimit + таймауты + allow-list
│   ├── DangerousCodeScanner.cs# Pre-execution regex scan
│   ├── DotnetFileBasedExecutor.cs # `dotnet run --file` реализация
│   └── skill.code-execution.v1.md  # Preset skill
├── Tools/                     # v2: экосистема tool'ов (HTTP / MCP / A2A)
│   ├── ITool.cs               # Контракт tool'а
│   ├── ToolRegistry.cs        # LLM prompt injection
│   ├── HttpTool.cs            # GET/POST/PUT/DELETE + allow-list
│   ├── A2AClient.cs           # JSON-RPC 2.0 agent-to-agent
│   ├── McpClient.cs           # MCP stub (SDK только server-side)
│   ├── CodeExecutionTool.cs   # Адаптер: ICodeExecutor → ITool
│   └── skill.*.v1.md          # Preset скиллы
├── Storage/                   # Хранилища
│   ├── FileSkillRepository.cs # Skills/ — файлы навыков
│   ├── MemoryStore.cs         # Memory/ — Markdown-память
│   ├── SqliteSessionStore.cs  # SQLite: sessions, logs, metrics, counters, sandbox_executions
│   └── Models.cs              # Доменные модели (Skill, SkillMeta с PhraseReceivers, ...)
├── CLI/
│   └── ConsoleUI.cs           # REPL-цикл (Spectre.Console)
├── Telegram/
│   └── TelegramBot.cs         # Telegram-бот (long polling)
└── Hercules.WebApi/
    ├── Program.cs             # DI + CORS + middleware, переиспользует ядро
    ├── Auth/ApiKeyMiddleware.cs
    ├── Config/WebApiConfig.cs
    └── Controllers/           # Chat / Skills / Memory / Stats
```

Дополнительно (отдельные проекты-«фасады» поверх ядра):

```
Hercules.WebApi/            # ASP.NET Core Minimal API (REST), порт :5000
├── Program.cs                 # DI + CORS + middleware, переиспользует ядро
├── Auth/ApiKeyMiddleware.cs   # Проверка заголовка X-Api-Key
├── Config/WebApiConfig.cs     # Ключ API + разрешённые CORS-источники
└── Controllers/               # Chat / Skills / Memory / Stats (Minimal API)

hercules-web/                    # Фронтенд на Astro + TailwindCSS, порт :4321
├── src/lib/api.ts             # Клиент Web API
├── src/layouts/Layout.astro   # Базовый макет (тёмная тема, навигация)
├── src/components/            # ChatBox, SkillCard, ProfileEditor, StatsDashboard
└── src/pages/                 # index / skills / profile / stats
```

Все runtime-данные складываются в папку `data/`:

```
data/
├── Skills/                    # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/                    # user_profile.md, preferences.md, entities.md, context_{date}.md
└── sessions.db                # SQLite: сессии, взаимодействия, метрики
```

---

## 🚀 Установка и запуск

### Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Сборка

```bash
cd Hercules
dotnet restore
dotnet build
```

### Запуск CLI (основной режим)

```bash
dotnet run
```

### Запуск Telegram-бота

```bash
dotnet run -- --telegram
```

(предварительно укажите `Telegram:BotToken` в `appsettings.json`)

### Запуск Web API (REST-сервер)

```bash
# из корня репозитория
dotnet run --project Hercules.WebApi
```

Сервер поднимается на `http://localhost:5000`. Ядро агента (`AgentCore`) переиспользуется
через адаптер `WebApiAdapter` — отдельной логики агента в Web API нет.

### Запуск CLI через основной проект

```bash
dotnet run --project Hercules -- --cli   # REPL-режим
dotnet run --project Hercules            # то же самое (CLI по умолчанию)
```

---

## 🌐 Web API

ASP.NET Core Minimal API. Все ответы — JSON (UTF-8, camelCase). Защита — заголовок
`X-Api-Key` (значение из `WebApi:ApiKey`, по умолчанию `dev-local-key`). CORS открыт для
локального фронтенда (`http://localhost:4321`, `http://localhost:3000`). Каждое взаимодействие
логируется в SQLite (`data/sessions.db`).

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

Пример:

```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"message":"какая погода в Москве?"}'
```

Конфигурация Web API (`Hercules.WebApi/appsettings.json`):

```jsonc
"WebApi": {
  "ApiKey": "dev-local-key",                  // пустая строка → доступ без ключа
  "AllowedCorsOrigins": [ "http://localhost:4321", "http://localhost:3000" ]
}
```

---

## 🎨 Веб-интерфейс (Astro)

Минималистичный SPA на **Astro + TailwindCSS** (тёмная тема, моноширинные блоки кода).
Лежит в каталоге `hercules-web/`.

| Страница   | Назначение                                                                               |
| ---------- | ---------------------------------------------------------------------------------------- |
| `/`        | Чат с агентом (бейджи режима/уверенности/провайдера, эффект печати, подсказки о навыках) |
| `/skills`  | Список навыков, создание вручную и улучшение через ИИ, редактирование                    |
| `/profile` | Редактор профиля долговременной памяти + сброс                                           |
| `/stats`   | Дашборд метрик, соотношение навык/прямой, активность по дням, рефлексия                  |

Компоненты: `ChatBox`, `SkillCard`, `ProfileEditor`, `StatsDashboard`. Клиент API — `src/lib/api.ts`.

### Запуск фронтенда

```bash
cd hercules-web
npm install
npm run dev        # dev-сервер на http://localhost:4321
```

Адрес бэкенда и ключ настраиваются через переменные окружения (файл `hercules-web/.env`):

```bash
PUBLIC_API_BASE=http://localhost:5000
PUBLIC_API_KEY=dev-local-key
```

### Полный локальный запуск (два терминала)

```bash
# Терминал 1 — бэкенд
dotnet run --project Hercules.WebApi      # → :5000

# Терминал 2 — фронтенд
cd hercules-web && npm run dev                  # → :4321
```

Откройте `http://localhost:4321`.

---

## ⚙️ Конфигурация (`appsettings.json`)

```jsonc
{
  "Llm": {
    "Provider": "yandexgpt",                 // активный провайдер
    "Fallback": ["ollama-cloud", "ollama-local"], // порядок fallback
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
  "Telegram": { "Enabled": false, "BotToken": "" }
}
```

> Любой параметр можно переопределить переменными окружения с префиксом `HERCULES_`,
> например: `HERCULES_Llm__Provider=ollama-local`.

### Провайдеры LLM

Все провайдеры работают через **OpenAI-совместимый интерфейс** и абстракцию
`Microsoft.Extensions.AI` (`IChatClient`). Поддерживаются:

- **YandexGPT** — основной (РФ). Модель передаётся как `gpt://{folderId}/{model}/latest`.
- **Ollama Cloud** — облачный fallback (`https://ollama.com/v1`).
- **Ollama Local / LM Studio** — локальный fallback (`http://localhost:11434/v1`).

Если основной провайдер недоступен, `ResilientLLMClient` автоматически переключается
на следующий из списка `Fallback`.

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
2. **Маршрутизация** → поиск подходящего навыка по триггерам (`SkillRouter`)
3. **Ответ LLM** → с активным навыком (skill-prompt) или напрямую (direct)
4. **Логирование** → input/output/confidence/mode в SQLite
5. **Порог навыка** → если запрос повторился `SkillCreationThreshold` раз → предложение создать навык (с подтверждением)
6. **Порог улучшения** → если `success_rate < SkillImprovementThreshold` → предложение обновить навык
7. **Сохранение памяти** → факты о пользователе, сущности, предпочтения
8. **Рефлексия** → по завершении сессии или каждые N команд

### Принципы

- **Never stop learning** — каждая сессия обогащает память или навыки
- **Explicit improvement loop** — агент сам предлагает исправления
- **Transparent** — пользователь видит все создания/улучшения
- **Human-in-the-loop** — навыки создаются только после подтверждения
- **Versioned** — старые версии навыков не удаляются (`skill.{id}.v{N}.md`)

---

## 🧪 Проверка критериев приёмки

| Критерий                      | Как проверить                                        |
| ----------------------------- | ---------------------------------------------------- |
| Навык создаётся автоматически | Повторите один запрос 3 раза → агент предложит навык |
| Навык используется            | После создания — запрос идёт через `навык: ...`      |
| Навык улучшается              | После серии плохих ответов → предложение обновить    |
| Профиль сохраняется           | Перезапуск → `/memory show` помнит факты             |
| Контекст переносится          | Сессия 1: факт → Сессия 2: агент помнит              |
| Reflection запускается        | После `/exit` — вывод Reflection Engine              |

---

## 📦 Зависимости (NuGet)

- `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI` — AI-абстракции
- `OpenAI` — OpenAI-совместимый SDK (YandexGPT, Ollama)
- `Microsoft.Data.Sqlite` — SQLite
- `YamlDotNet` — парсинг метаданных
- `Spectre.Console` — улучшенный CLI
- `Telegram.Bot` — Telegram-интерфейс
- `Microsoft.Extensions.Hosting` / `Configuration.Json` — DI и конфигурация

---

## 📚 Документация

| Документ                                       | Описание                         |
| ---------------------------------------------- | -------------------------------- |
| [docs/QUICKSTART.md](docs/QUICKSTART.md)       | Быстрый старт за несколько минут |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)   | Архитектура ядра и интерфейсов   |
| [docs/CONFIGURATION.md](docs/CONFIGURATION.md) | Полный справочник настроек       |
| [docs/API.md](docs/API.md)                     | Справочник REST Web API          |
| [docs/BRANDING.md](docs/BRANDING.md)           | Логотип, палитра, правила бренда |
| [CONTRIBUTING.md](CONTRIBUTING.md)             | Как внести вклад                 |
| [CHANGELOG.md](CHANGELOG.md)                   | История изменений                |
| [SECURITY.md](SECURITY.md)                     | Политика безопасности            |
| [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)       | Кодекс поведения                 |

---

## 🤝 Вклад

PR и Issue приветствуются! Перед началом ознакомьтесь с [CONTRIBUTING.md](CONTRIBUTING.md)
и [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Об уязвимостях сообщайте по [SECURITY.md](SECURITY.md).

---

## 📝 Лицензия

[MIT](LICENSE) © 2026 Victor Buzin.
