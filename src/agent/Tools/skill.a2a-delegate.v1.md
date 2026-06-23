---
id: a2a-delegate
name: A2A Delegate
version: 1
phrase_receivers:
  - "delegate"
  - "ask agent"
  - "a2a"
  - "делегируй"
  - "попроси агента"
description: |
  Делегировать задачу другому агенту через A2A-протокол (JSON-RPC 2.0).
  Endpoints задаются в appsettings.json:A2A.Endpoints (имя → URL).
  Для multi-agent workflows, распределённых задач, микросервисной архитектуры агентов.
---

# A2A Delegate Skill

Когда пользователь хочет **делегировать задачу другому агенту** —
используй tool `a2a`:

```json
{
  "action": "a2a",
  "arguments": {
    "agent": "researcher",
    "task": "Найди последние статьи по multi-agent LLM systems за июнь 2026"
  }
}
```

## Параметры

- `agent` (required) — имя агента из `appsettings.json:A2A.Endpoints`
- `task` (required) — текст задачи
- `context` (optional) — контекст для удалённого агента

## Конфигурация

```json
"A2A": {
  "Endpoints": {
    "researcher": "https://researcher-agent.example.com/a2a",
    "translator": "https://translator-agent.example.com/a2a"
  },
  "TimeoutSeconds": 30
}
```

## Когда использовать

- Multi-agent workflows (orchestration)
- Распределённые задачи (один агент = один домен экспертизы)
- Микросервисная архитектура (разные агенты на разных серверах)
- Специализированные sub-agents (summarizer, fact-checker, translator)

## Pitfalls

- ❌ Не знаешь URL агента — спроси пользователя или посмотри в appsettings
- ❌ Не делегируй без контекста — другой агент не знает что было раньше
- ❌ Не делегируй > 3 вложенных вызовов (infinite delegation)
- ✅ Передавай `context` если нужна история разговора
- ✅ Проверяй что A2A endpoint доступен (CORS / network) перед делегированием
