# Справочник Web API

ASP.NET Core Minimal API. База: `http://localhost:5000`. Все ответы — JSON (UTF-8, camelCase).

## Авторизация
Все эндпоинты, кроме `/api/health`, требуют заголовок:
```
X-Api-Key: <значение WebApi:ApiKey>
```
Если `WebApi:ApiKey` пуст — авторизация отключена (только для локальной разработки).

## Эндпоинты

| Метод | Маршрут | Описание |
|-------|---------|----------|
| `GET`  | `/api/health` | Проверка живости (без ключа) |
| `POST` | `/api/chat` | Сообщение агенту → ответ + режим/уверенность/навык |
| `GET`  | `/api/skills` | Список навыков |
| `POST` | `/api/skills` | Создать навык; `?ai=true` — сгенерировать через LLM |
| `GET`  | `/api/skills/{id}` | Детали навыка (метаданные + промпт) |
| `PUT`  | `/api/skills/{id}` | Обновить навык → новая версия |
| `POST` | `/api/skills/{id}/improve` | Улучшить навык через LLM → новая версия |
| `GET`  | `/api/memory/profile` | Профиль памяти (Markdown) |
| `PUT`  | `/api/memory/profile` | Перезаписать профиль памяти |
| `POST` | `/api/memory/reset` | Сбросить долговременную память |
| `GET`  | `/api/reflect` | Запустить рефлексию → Markdown-отчёт |
| `GET`  | `/api/stats` | Метрики: всего, навык/прямой, успешность, по дням |

## Примеры

### Чат
```bash
curl -X POST http://localhost:5000/api/chat \
  -H "X-Api-Key: dev-local-key" \
  -H "Content-Type: application/json" \
  -d '{"message":"какая погода в Москве?"}'
```
Ответ:
```json
{
  "answer": "...",
  "mode": "direct",
  "confidence": "high",
  "provider": "yandexgpt",
  "skill": null,
  "proposeSkillForInput": false,
  "proposeImproveSkillId": null,
  "proposeImproveSkillName": null
}
```

### Создание навыка через ИИ
```bash
curl -X POST "http://localhost:5000/api/skills?ai=true" \
  -H "X-Api-Key: dev-local-key" -H "Content-Type: application/json" \
  -d '{"name":"Перевод текста","description":"Переводит текст между языками"}'
```

### Улучшение навыка
```bash
curl -X POST http://localhost:5000/api/skills/translate/improve \
  -H "X-Api-Key: dev-local-key"
```

### Статистика
```bash
curl http://localhost:5000/api/stats -H "X-Api-Key: dev-local-key"
```

## Коды ответов
| Код | Значение |
|-----|----------|
| `200` | Успех |
| `400` | Некорректный запрос (валидация тела) |
| `401` | Отсутствует/неверный `X-Api-Key` |
| `404` | Навык/ресурс не найден |
| `500` | Внутренняя ошибка (см. логи сервера) |
