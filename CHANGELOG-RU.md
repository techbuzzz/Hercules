# История изменений

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
проект придерживается [семантического версионирования](https://semver.org/lang/ru/).

## [Unreleased]

В очереди пусто. Это первый тег, cut из этого репозитория — секция `1.0.0` ниже была
задокументирована, но никогда не тегировалась, поэтому сравнивать не с чем.

## [2.0.0] - 2026-10-07

Релиз web-first. Electron-оболочка удалена, агент сам раздаёт SPA Studio.
Это мажорный бамп относительно задокументированной `1.0.0`, потому что изменение
убирает рантайм-поверхность, от которой зависят существующие установки.

### Ломающие изменения
- **Electron удалён; web-first (ADR-0009).** Агент сам раздаёт SPA Studio на `/ui`.
  Отдельного процесса фронтенда и Electron-оболочки больше нет.
- **`src/hercules-web` (Astro) устарел и не портирован** — его панели дублировали эндпоинты,
  которые Studio уже использует. Карта замены: `src/hercules-web/DEPRECATED.md`.
- **Порт dev-сервера UI — 4330, а не 4321.** Dev whitelist CORS агента: `4322/4330/8421`.
- **Дефолтный порт агента 5000 → 8421** (`task_096`, ADR-0003).
  - `Hercules.WebApi` теперь слушает `http://localhost:8421` (Development) /
    `http://0.0.0.0:8421` (Production) вместо порта 5000.
  - `Mesh.Endpoint` по умолчанию в `appsettings.json` и
    `AppConfig.MeshConfig.Endpoint` заменён на `http://localhost:8421`.
  - Dev-fallback CORS origins: `http://localhost:5000` →
    `http://localhost:8421` (Studio продолжает сканировать 5000 как
    legacy-fallback — см. ADR-0003).
  - **Миграция:** в существующих установках задайте
    `ASPNETCORE_URLS=http://localhost:8421` (или `--urls …`) либо
    обновите `Mesh.Endpoint` в `appsettings.json` / `runtime-config.json`.
    Чтобы сохранить старый порт, укажите явно
    `ASPNETCORE_URLS=http://0.0.0.0:5000`.
  - Причина: 5000 конфликтует с Flask, Synology DSM, UPnP, Syncthing. См.
    [ADR-0003](docs/EPIC_Hercules_Studio/adr/0003-port-range-8421.md).
- **Ребрендинг**: проект переименован из `MicroHermes` / «Мини-Хермес» в **Hercules**.
  - Переименованы пространства имён (`MicroHermes.*` → `Hercules.*`), проекты
    (`MicroHermes.csproj` → `Hercules.csproj`, `MicroHermes.WebApi` → `Hercules.WebApi`),
    решение (`Hercules.slnx`) и фронтенд-каталог (`hermes-web` → `hercules-web`).
  - Префикс переменных окружения: `HERMES_` → `HERCULES_`.
  - `Skill.Meta.Triggers` → `Skill.Meta.PhraseReceivers` (human-friendly термин).
    Обратная совместимость для чтения legacy `triggers:` ключа в `skill.{id}.meta.json`.

### Добавлено

#### Studio (web-first)
- **SPA Studio** (`src/hercules-studio`, Vue 3 + Vite): Agents, Chat, Skills, Mesh, Tools,
  Config, Workflow, Decisions, Consensus, Context, LLM.
- **Навыки:** редактор Monaco, история промптов с **diff** ревизий (LCS), restore, создание
  из 5 шаблонов, push на несколько агентов с результатом по каждому, сборщик `.skillpkg` по
  спецификации `docs/skill-package-spec.md`.
- **Mesh Explorer:** канвас топологии на Vue Flow (цвет = здоровье, размер = число
  capabilities), панель деталей узла, контекстное меню из 6 действий, роутер с поиском по
  фразе, сортировкой и фильтрами, браузер общей памяти, панель предохранителей, дашборд
  трафика/heatmap навыков/оценок и автообновление раз в 30 с, пропускаемое в скрытой вкладке.
- **Консилиум:** параллельный fan-out с устойчивостью к частичным сбоям, ручной выбор или
  структурированный LLM-судья (`{best_index, rationale}`) с подсветкой победителя, фоновые
  уведомления и сохранённая история раундов.
- **Управление MCP:** добавление/изменение/удаление/перезагрузка через merge-patch конфига,
  плюс переключатель `enabled` для каждого сервера.
- **Config:** редактор LLM-провайдеров, квот, бюджета и дистилляции контекста, переключатель
  бэкенда хранилища сессий (с применением после рестарта).
- **Редактор ролей** (`/api/auth/keys`, только system). Ключи адресуются необратимым
  отпечатком; открытый текст не читается никогда, сгенерированный ключ возвращается ровно один раз.
- **Строгий TypeScript:** `strict` и `noUncheckedIndexedAccess`.

#### Бэкенд агента
- **Дистилляция контекста** (`task_102`): иерархическая компрессия
  (`recent` raw + `older` summaries + `ancient` key-facts) для экономии токенов
  в длинных сессиях.
  - `ContextConfig.Distillation`: `Mode` (Off/Auto/Manual), `Strategy`
    (hierarchical), `RecentRawCount`, `SummaryInterval`, `KeyFactsExtraction`,
    `MaxAncientFacts`, `SummaryTokenBudget`, `KeyFactsTokenBudget`, плюс
    `DistillationPreset` (Economy/Balanced/Full).
  - `IDistillationStore` + `SqliteDistillationStore` для persistence
    (таблицы `context_summaries`, `context_key_facts`, upsert по
    `(session_id, fact_text)`).
  - `ContextDistillationService` — детерминированная сводка (без LLM в hot
    path: word-frequency topics + sentence scoring; n-gram frequency + dedup
    для key-facts). `DistillAsync` и `GetSummaryAsync`.
  - `ContextBuilder` интегрирует distilled блок additively при
    `Distillation.Mode != Off` (legacy path facts+episodes+working сохранён).
  - Новые эндпоинты: `POST /api/context/distill` (запуск по запросу или
    per-session), `GET /api/context/summary?sessionId=…&maxTokens=…` (реальный
    markdown-блок вместо заглушки). `WithName` для Orval codegen.
  - `SqliteSessionStore.GetSessionInteractionsAsync(sessionId, limit, ct)` —
    хронологический список `InteractionLog` для сервиса.
  - См. [task_102.md](docs/roadmap/tasks/task_102.md) — implementation notes.
- **Mesh observability** (`task_065`): `IMeshObservabilityService` подключён к
  `CapabilityMeshRouter`, `ResilientTransport`, `IntentRouter`,
  `TaskLifecycleProtocol` и `FanOutOrchestrator`. Новые эндпоинты
  `GET /api/mesh/observability/status` и `/config`, плюс
  `RecordMeshMetric("retry_attempt", …)`.
- **Multi-role LLM routing.** `AppConfig.Roles` — словарь ролей (`main`,
  `code_writer`, `reflector`). `ILLMClient.CompleteAsync(role, messages, ct)` — overload
  с `role = "main"` по умолчанию. `ResilientLLMClient` маршрутизирует по роли через
  `RoleRouter`; fallback на main если роль не сконфигурирована. `ReflectionEngine` —
  роль `reflector`.
- **Песочница для выполнения кода.** `CodeExecution/ICodeExecutor` (C# file-based
  apps, `dotnet run --file`). 3 уровня защиты: regex pre-scan
  (`DangerousCodeScanner` — 25+ паттернов: `File.Delete`, `Process.Start`, `HttpClient`,
  `Socket`, `Assembly.LoadFile`, `DllImport`, `Registry`, `rm -rf`, `bash -c`, `eval`, …)
  → изолированная temp-директория → POSIX ulimit wrapper + `CancellationTokenSource`
  timeout. Сеть запрещена по умолчанию, 30 s timeout, 1024 file descriptors, 100 KB
  лимит кода. Escape hatch через `SandboxOptions.CustomAllowedNamespaces`
  (token-based: `"HttpClient"` разрешает `new HttpClient()`).
- **Экосистема инструментов.** `Tools/ITool` контракт + `ToolRegistry` для LLM prompt
  injection: `http` (`HttpTool`, allow-list доменов, 60/min, 10 s timeout, 256 KB cap),
  `execute_code` (`CodeExecutionTool`), `a2a` (`A2AClient`, JSON-RPC 2.0 согласно
  https://a2a-protocol.org/latest/), `mcp` (`McpClient`).
- **Tool-aware agent flow.** `AgentCore` распознаёт JSON actions в ответе LLM
  (`{"action": "tool", "arguments": {...}}`), выполняет tool, кладёт результат в
  transcript, вызывает LLM снова для финального ответа. Max 3 tool-итерации на ход;
  `mode = "tool"` в `AgentResponse` и `ChatResponseDto`.
- **Таблица аудита песочницы** `sandbox_executions` в SQLite. `ReflectionEngine`
  сообщает failure rate за последние 5 выполнений и предупреждает при `> 50 %`.
- **NuGet-пакет `Hercules.SkillSdk`** (`task_101`): whitelist-интерфейсы для file-based
  C#-скиллов (`IHttpClient`, `IMcpClient`, `ILlmClient`, `IMemoryClient`, `ISkillLogger`,
  `ISessionContext`, агрегат `IHerculesSkillContext`) с адаптерами, принудительно
  применяющими разрешённые домены, список MCP-инструментов, скоупы памяти и изоляцию
  сессии. Плюс `SkillSdkExecutor`, компилирующий такие скиллы в collectible
  `AssemblyLoadContext` через Roslyn.
- **Security Operations** (`task_055`): fleet-wide identity rotation, credential
  revocation, certificate renewal, package signing verification, vulnerability reporting,
  security audit export (`IFleetIdentityService`, `ICertificateService`,
  `IPackageSigningService`, `IVulnerabilityReporter`, `ISecurityAuditExporter`).

#### Инфраструктура
- **CI** (`.github/workflows/ci.yml`): сборка/тесты агента, typecheck/lint/unit/E2E Studio и
  проверка свежести OpenAPI, падающая при любом расхождении.
- Фирменные ассеты в `assets/branding/` (логотип, монограмма, favicon, PNG/ICO-экспорт).
- Полный комплект документации репозитория: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.editorconfig`, шаблоны Issue/PR.

### Изменено
- Обновлён фирменный стиль интерфейса и заголовки веб-приложения.

### Безопасность
- **`GET /api/config` больше не отдаёт учётные данные.** Раньше он возвращал живые ключи LLM,
  строку подключения Postgres (с паролем), токен Telegram-бота и signing key **любой**
  аутентифицированной сессии, включая роль `contribute`. Секреты маскируются по имени, а
  `PATCH /api/config` вырезает маркер перед merge, чтобы замаскированное значение не
  затёрло настоящий секрет при round-trip.

### Исправлено
- `McpClientService` игнорировал `Enabled`, поэтому выключенные серверы всё равно
  подключались, а их инструменты регистрировались.
- Список агентов mesh рендерился вечно пустым: эндпоинт отдаёт `{count, agents}`, а вью
  проверял `Array.isArray`.
- Авторизация Studio: ключи снимаются на каждый запрос, а не на старте процесса, поэтому
  смена роли применяется сразу; удаление или понижение ключа отзывает его живые сессии.
- Правка workflow создавала дубликаты — в запросе сохранения не было `Id`, поэтому каждое
  редактирование порождало новый definition.
- 314 битых относительных ссылок в ~104 файлах roadmap-задач.

### Тесты
- 2276 тестов агента проходят (`tests/Hercules.Agent.Tests`).
- 97 unit-тестов Studio, 30 E2E-тестов Studio (`Playwright`).
- Отдельные scenario-скрипты сохранены в `scripts/`: `test-phrase-receivers.cs`,
  `test-multi-role.cs`, `test-sandbox.cs`, `test-tools.cs`, `test-stage4.cs`.
- Проверки документации, перезапускаемые через `scripts/`: `check-doc-links.cjs`,
  `check-backlog-status.cjs`, `check-ci-workflow.cjs`, `count-stage-tasks.cjs`.

## [1.0.0] - 2026-06-18

> Никогда не тегировался — сохранено как задокументированное предыдущее состояние.

### Добавлено
- Ядро самообучающегося агента: `AgentCore`, `SkillRouter`, `SkillManager`,
  `ReflectionEngine`, `MemoryManager`.
- Слой LLM на `Microsoft.Extensions.AI`: YandexGPT (основной), Ollama Cloud / Local,
  LM Studio с автоматическим fallback (`ResilientLLMClient`).
- Гибридное хранилище: файлы Markdown/JSON для навыков и памяти + SQLite для логов и метрик.
- Интерфейсы: CLI (REPL на Spectre.Console) и Telegram-бот.
- Web API (ASP.NET Core Minimal API) с авторизацией по `X-Api-Key` и CORS.
- Веб-интерфейс на Astro + TailwindCSS (чат, навыки, профиль, статистика).