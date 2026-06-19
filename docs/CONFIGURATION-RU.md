# Конфигурация

Все настройки задаются в `appsettings.json` (ядро) и `Hercules.WebApi/appsettings.json` (Web API),
а также переопределяются переменными окружения с префиксом `HERCULES_`.

## Переопределение через окружение
Вложенные ключи разделяются двойным подчёркиванием `__`:
```bash
HERCULES_Llm__Provider=ollama-local
HERCULES_Llm__YandexGpt__ApiKey=***
HERCULES_Agent__SkillCreationThreshold=5
HERCULES_Telegram__Enabled=true
```

## Секция `Llm`
| Ключ | Описание | По умолчанию |
|------|----------|--------------|
| `Provider` | Активный провайдер: `yandexgpt` / `ollama-cloud` / `ollama-local` | `yandexgpt` |
| `Fallback` | Порядок переключения при недоступности | `["ollama-cloud","ollama-local"]` |

### `Llm:YandexGpt`
| Ключ | Описание |
|------|----------|
| `Endpoint` | `https://llm.api.cloud.yandex.net/v1` |
| `ApiKey` | IAM- или API-ключ Yandex Cloud |
| `FolderId` | ID каталога Yandex Cloud |
| `Model` | Имя модели; преобразуется в `gpt://{FolderId}/{Model}/latest` |
| `Temperature` | Температура генерации (напр. `0.6`) |
| `MaxTokens` | Максимум токенов ответа (напр. `2000`) |

### `Llm:OllamaCloud`
| Ключ | Описание |
|------|----------|
| `Endpoint` | `https://ollama.com/v1` |
| `ApiKey` | API-ключ Ollama Cloud |
| `Model` | Напр. `gpt-oss:120b` |

### `Llm:OllamaLocal`
| Ключ | Описание |
|------|----------|
| `Endpoint` | `http://localhost:11434/v1` |
| `ApiKey` | Не требуется (пустая строка) |
| `Model` | Напр. `llama3.1` |

> Все провайдеры работают через OpenAI-совместимый интерфейс и абстракцию
> `Microsoft.Extensions.AI`. При сбое активного провайдера `ResilientLLMClient`
> переключается на следующий из `Fallback`.

## Секция `Storage`
| Ключ | Описание | По умолчанию |
|------|----------|--------------|
| `DataRoot` | Корень runtime-данных | `data` |
| `SkillsDir` | Каталог навыков | `Skills` |
| `MemoryDir` | Каталог памяти | `Memory` |
| `SqliteFile` | Файл БД сессий | `sessions.db` |

## Секция `Agent`
| Ключ | Описание | По умолчанию |
|------|----------|--------------|
| `SystemPrompt` | Базовый системный промпт агента | — |
| `SkillCreationThreshold` | Повторов запроса до предложения навыка | `3` |
| `SkillImprovementThreshold` | Порог `success_rate` для улучшения | `0.6` |
| `SkillEvaluationWindow` | Окно оценки навыка | `5` |
| `ReflectionEveryNCommands` | Авто-рефлексия каждые N команд | `10` |

## Секция `Telegram`
| Ключ | Описание | По умолчанию |
|------|----------|--------------|
| `Enabled` | Запускать ли бота | `false` |
| `BotToken` | Токен от @BotFather | `""` |

## Секция `WebApi` (`Hercules.WebApi/appsettings.json`)
| Ключ | Описание | По умолчанию |
|------|----------|--------------|
| `ApiKey` | Ключ для заголовка `X-Api-Key`; пустая строка отключает авторизацию | `dev-local-key` |
| `AllowedCorsOrigins` | Разрешённые источники CORS | `["http://localhost:4321","http://localhost:3000"]` |

## Фронтенд (`hercules-web/.env`)
| Переменная | Описание |
|------------|----------|
| `PUBLIC_API_BASE` | Базовый URL Web API (напр. `http://localhost:5000`) |
| `PUBLIC_API_KEY` | Значение `X-Api-Key` для запросов |

> ⚠️ Не коммитьте реальные ключи. Используйте переменные окружения и держите `data/` вне репозитория.
