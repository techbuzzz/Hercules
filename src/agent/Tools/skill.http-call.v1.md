---
id: http-call
name: HTTP Call
version: 1
phrase_receivers:
  - "http"
  - "api call"
  - "fetch url"
  - "сделай запрос"
  - "получи данные"
  - "get api"
description: |
  Делать безопасные HTTP-запросы (GET/POST/PUT/DELETE) через HttpTool.
  Allow-list доменов, rate limit, timeout. Сеть изолирована от CodeExecution sandbox.
  Для API-вызовов, загрузки JSON/CSV с публичных endpoints, webhooks.
---

# HTTP Call Skill

Когда пользователь просит **сделать HTTP-запрос, получить данные с API, проверить URL** —
используй tool `http` с JSON-аргументами:

```json
{
  "action": "http",
  "arguments": {
    "method": "GET",
    "url": "https://api.github.com/repos/techbuzzz/Hercules"
  }
}
```

## Поддерживаемые методы

- `GET` — получить ресурс
- `POST` — создать (с body)
- `PUT` — обновить (с body)
- `DELETE` — удалить

## Параметры

- `method` (required) — HTTP метод
- `url` (required) — полный URL
- `headers` (optional) — дополнительные HTTP headers (dict)
- `body` (optional) — тело запроса (для POST/PUT)

## Ограничения

- **Allow-list доменов**: только домены из `appsettings.json:Http.AllowedDomains`
  (`["*"]` = все домены, `["api.github.com"]` = только github)
- **Rate limit**: 60 запросов в минуту (настраивается)
- **Timeout**: 10 секунд
- **Max response**: 256 КБ (truncated если больше)

## Когда НЕ вызывать

- Нужно исполнить C# код → используй `execute_code`
- Нужен скилл для повторяющегося workflow → предложи создать skill
- Запрос к localhost/private network → нет, только публичные домены

## Примеры

### GitHub API
```json
{ "action": "http", "arguments": { "method": "GET", "url": "https://api.github.com/users/techbuzzz" } }
```

### POST с JSON body
```json
{
  "action": "http",
  "arguments": {
    "method": "POST",
    "url": "https://httpbin.org/post",
    "headers": { "X-Custom": "value" },
    "body": "{\"key\": \"value\"}"
  }
}
```

## Pitfalls

- ❌ Не выдумывай URL — если не знаешь точный, спроси пользователя
- ❌ Не делай > 5 запросов подряд без задержки — rate limit
- ❌ Не парси большие ответы (256 КБ+) — будет truncated
- ✅ Используй GET для idempotent операций, POST для создания
