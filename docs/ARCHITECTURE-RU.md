# Архитектура

Hercules состоит из переиспользуемого **ядра агента** и трёх **интерфейсов-фасадов**
(CLI, Telegram, Web API + фронтенд), которые не содержат собственной бизнес-логики.

## Обзор компонентов

```
                 ┌─────────────┐   ┌──────────────┐   ┌──────────────────────┐
   Пользователь  │   CLI (REPL)│   │ Telegram-бот │   │ Web API + Astro UI   │
                 └──────┬──────┘   └──────┬───────┘   └──────────┬───────────┘
                        │                 │                      │
                        └─────────────────┼──────────────────────┘
                                          ▼
                                 ┌──────────────────┐
                                 │     AgentCore    │  главный цикл обработки
                                 └───┬───────┬──────┘
              ┌──────────────────────┘       └──────────────────────┐
              ▼            ▼            ▼            ▼               ▼
        SkillRouter  SkillManager  MemoryManager  ReflectionEngine  ILLMClient
              │            │            │              │             │
              └──── FileSkillRepository / MemoryStore / SqliteSessionStore ──── ResilientLLMClient
                                                                                  │
                                                                 ┌────────────────┼────────────────┐
                                                              YandexGPT      Ollama Cloud     Ollama Local
                                                                 (через Microsoft.Extensions.AI / OpenAI)
```

## Слои

### 1. Интерфейсы (`CLI/`, `Telegram/`, `Hercules.WebApi/`, `hercules-web/`)
Принимают ввод пользователя, вызывают `AgentCore.HandleAsync()` и отображают результат.
Web API использует `Agent/WebApiAdapter.cs` (DTO + маппинг) как тонкий слой над ядром.

### 2. Ядро агента (`Agent/`)
- **`AgentCore`** — оркестрация: загрузка контекста → маршрутизация → вызов LLM → логирование →
  проверка порогов создания/улучшения навыков → сохранение памяти.
- **`SkillRouter`** — выбор навыка по триггерам или прямой ответ LLM.
- **`SkillManager`** — CRUD навыков, версионирование (`skill.{id}.v{N}.md`), LLM-генерация и улучшение.
- **`MemoryManager`** — модель пользователя, предпочтения, сущности, перенос контекста между сессиями.
- **`ReflectionEngine`** — самоанализ после сессии или каждые N команд, отчёты рефлексии.

### 3. Слой LLM (`LLM/`)
Единый интерфейс `ILLMClient` поверх `Microsoft.Extensions.AI` (`IChatClient`).
- `YandexGPTClient`, `LocalLLMClient` (Ollama/LM Studio) — конкретные провайдеры.
- `LlmClientFactory` — выбор клиента по имени провайдера.
- `ResilientLLMClient` — отказоустойчивость и fallback-цепочка из `Llm:Fallback`.

### 4. Хранилище (`Storage/`)
Гибридное:
- **Файлы** (`FileSkillRepository`, `MemoryStore`) — Markdown/JSON для навыков и памяти (прозрачность знаний).
- **SQLite** (`SqliteSessionStore`) — сессии, взаимодействия, метрики, счётчики повторов.

## Поток обработки запроса (self-improving цикл)
1. **Запрос** → загрузка профиля и контекста из памяти.
2. **Маршрутизация** → `SkillRouter` ищет навык по триггерам.
3. **Ответ LLM** → с навыком (skill-prompt) или напрямую (direct).
4. **Логирование** → input/output/confidence/mode в SQLite.
5. **Порог создания** → повтор `SkillCreationThreshold` раз → предложение создать навык (с подтверждением).
6. **Порог улучшения** → `success_rate < SkillImprovementThreshold` → предложение обновить навык.
7. **Память** → извлечение фактов о пользователе, сущностей, предпочтений.
8. **Рефлексия** → по завершении сессии или каждые N команд.

## Принципы проектирования
- **Never stop learning** — каждая сессия обогащает память или навыки.
- **Human-in-the-loop** — навыки создаются/изменяются только после подтверждения (в CLI).
- **Transparent & versioned** — старые версии навыков сохраняются, знания читаемы в Markdown.
- **Provider-agnostic** — смена LLM без изменения логики ядра.

## Данные времени выполнения (`data/`)
```
data/
├── Skills/   # skill.{id}.md / .prompt.md / .meta.json / .usage.json / .v{N}.md
├── Memory/   # user_profile.md, preferences.md, entities.md, context_{date}.md
└── sessions.db  # SQLite: сессии, взаимодействия, метрики
```
