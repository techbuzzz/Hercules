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
- Обновлён фирменный стиль интерфейса и заголовки веб-приложения.

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
