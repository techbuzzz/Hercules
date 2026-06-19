# История изменений

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
проект придерживается [семантического версионирования](https://semver.org/lang/ru/).

## [Unreleased]

### Изменено
- **Ребрендинг**: проект переименован из `MicroHermes` / «Мини-Хермес» в **Hercules**.
  - Переименованы пространства имён (`MicroHermes.*` → `Hercules.*`), проекты
    (`MicroHermes.csproj` → `Hercules.csproj`, `MicroHermes.WebApi` → `Hercules.WebApi`),
    решение (`Hercules.slnx`) и фронтенд-каталог (`hermes-web` → `hercules-web`).
  - Префикс переменных окружения: `HERMES_` → `HERCULES_`.
  - `Skill.Meta.Triggers` → `Skill.Meta.PhraseReceivers` (human-friendly термин).
    Обратная совместимость для чтения legacy `triggers:` ключа в `skill.{id}.meta.json`.
- Обновлён фирменный стиль интерфейса и заголовки веб-приложения.

### Добавлено (v2 — Code Execution + Multi-Role Routing + Tools)
- **Stage 1: Multi-role LLM routing**. `AppConfig.Roles` — словарь ролей (`main`,
  `code_writer`, `reflector`). `ILLMClient.CompleteAsync(role, messages, ct)` — overload
  с `role = "main"` по умолчанию. `ResilientLLMClient` маршрутизирует по роли через
  `RoleRouter`; fallback на main если роль не сконфигурирована. `ReflectionEngine` —
  роль `reflector`.
- **Stage 2: Sandboxed code execution**. `CodeExecution/ICodeExecutor` (C# file-based
  apps, `dotnet run --file`). 3 уровня защиты: regex pre-scan
  (`DangerousCodeScanner` — 25+ паттернов: `File.Delete`, `Process.Start`, `HttpClient`,
  `Socket`, `Assembly.LoadFile`, `DllImport`, `Registry`, `rm -rf`, `bash -c`, `eval`, …)
  → изолированная temp-директория → POSIX ulimit wrapper + `CancellationTokenSource`
  timeout. Сеть запрещена по умолчанию, 30 s timeout, 1024 file descriptors, 100 KB
  лимит кода. Escape hatch через `SandboxOptions.CustomAllowedNamespaces`
  (token-based: `"HttpClient"` разрешает `new HttpClient()`).
- **Stage 3: Tool ecosystem**. `Tools/ITool` контракт + `ToolRegistry` для LLM prompt
  injection. Три встроенных tool:
  - `http` — `HttpTool`: GET/POST/PUT/DELETE с allow-list доменов
    (wildcard `*.example.com`), rate limit (60/min), 10 s timeout, 256 KB max response.
  - `execute_code` — `CodeExecutionTool`: адаптер `ICodeExecutor` к tool-протоколу.
  - `a2a` — `A2AClient`: минимальный JSON-RPC 2.0 клиент для agent-to-agent
    delegation (spec: https://a2a-protocol.org/latest/).
  - `mcp` — `McpClient`: stub-интерфейс для `ModelContextProtocol` NuGet
    (0.3.0-preview — только server-side; client SDK ETA Q1-Q2 2026).
- **Stage 4: Tool-aware agent flow**. `AgentCore` распознаёт JSON actions в ответе LLM
  (`{"action": "tool", "arguments": {...}}`), выполняет tool, кладёт результат в
  transcript, вызывает LLM снова для финального ответа. Max 3 tool-итерации на ход.
  `mode = "tool"` в `AgentResponse` и `ChatResponseDto`.
- **Sandbox audit table** `sandbox_executions` в SQLite: `id`, `session_id`,
  `code_hash`, `language`, `exit_code`, `status`, `duration_ms`, `blocked_patterns`,
  `created_at`. `ReflectionEngine` сообщает failure rate за последние 5 выполнений
  и предупреждает при `> 50 %`.

### Тесты
- `scripts/test-phrase-receivers.cs` — 8/8 миграция.
- `scripts/test-multi-role.cs` — 10/10 проверок ролей.
- `scripts/test-sandbox.cs` — 23/23 scanner + executor.
- `scripts/test-tools.cs` — 20/20 registry + HttpTool + A2AClient + CodeExecutionTool.
- `scripts/test-stage4.cs` — 16/16 TryParseAction + sandbox audit + tool injection.

### Добавлено
- Фирменные ассеты в `assets/branding/` (логотип, монограмма, favicon, PNG/ICO-экспорт).
- Полный комплект документации репозитория: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `SECURITY.md`, `CHANGELOG.md`, `.editorconfig`, шаблоны Issue/PR, CI-workflow.
- Каталог `docs/` с подробными гайдами: быстрый старт, архитектура, конфигурация, API, бренд.

## [1.0.0] — 2026-06-18

### Добавлено
- Ядро самообучающегося агента: `AgentCore`, `SkillRouter`, `SkillManager`,
  `ReflectionEngine`, `MemoryManager`.
- Слой LLM на `Microsoft.Extensions.AI`: YandexGPT (основной), Ollama Cloud / Local,
  LM Studio с автоматическим fallback (`ResilientLLMClient`).
- Гибридное хранилище: файлы Markdown/JSON для навыков и памяти + SQLite для логов и метрик.
- Интерфейсы: CLI (REPL на Spectre.Console) и Telegram-бот.
- Web API (ASP.NET Core Minimal API) с авторизацией по `X-Api-Key` и CORS.
- Веб-интерфейс на Astro + TailwindCSS (чат, навыки, профиль, статистика).
