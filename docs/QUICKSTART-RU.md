# Быстрый старт

Это руководство поможет запустить **Hercules** локально за несколько минут.

## 1. Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) — `dotnet --version` должен показать `10.x`
- [Node.js 20+](https://nodejs.org/) — только если нужен веб-интерфейс
- Доступ хотя бы к одному LLM-провайдеру:
  - **YandexGPT** (ключ + folderId Yandex Cloud), **или**
  - **Ollama Cloud** (API-ключ), **или**
  - **Ollama Local** (`ollama serve` на `localhost:11434`)

## 2. Клонирование и сборка
```bash
git clone https://github.com/<owner>/hercules.git
cd hercules
dotnet restore
dotnet build Hercules.slnx
```

## 3. Настройка провайдера
Самый быстрый способ — через веб-интерфейс после запуска Web API (см. шаг 5).
Если вы запускаете только CLI, откройте `appsettings.json` и укажите активный провайдер и ключи.
Пример для локального Ollama:
```jsonc
{
  "Llm": {
    "Provider": "ollama-local",
    "OllamaLocal": { "Endpoint": "http://localhost:11434/v1", "Model": "llama3.1" }
  }
}
```
Секреты лучше передавать через переменные окружения:
```bash
export HERCULES_Llm__Provider=yandexgpt
export HERCULES_Llm__YandexGpt__ApiKey=*** 
export HERCULES_Llm__YandexGpt__FolderId=***
```

## 4. Запуск CLI (основной режим)
```bash
dotnet run --project Hercules
```
Введите запрос в REPL. Повторите один и тот же запрос 3 раза — агент предложит создать навык.

## 5. Запуск Web API + фронтенда (plug-and-play конфигурация)
```bash
# Терминал 1 — бэкенд (порт :8421)
dotnet run --project Hercules.WebApi

# Терминал 2 — фронтенд (порт :4321)
cd hercules-web
npm install
cp .env.example .env   # при необходимости поправьте PUBLIC_API_BASE / PUBLIC_API_KEY
npm run dev
```
Откройте `http://localhost:4321` и перейдите в раздел **Настройки**.
Там можно изменить LLM-провайдера, ключи, системный промпт и другие параметры —
изменения применяются сразу, без перезагрузки сервера, и сохраняются в `data/runtime-config.json`.

## 6. Запуск Telegram-бота (опционально)
```bash
export HERCULES_Telegram__Enabled=true
export HERCULES_Telegram__BotToken=<токен от @BotFather>
dotnet run --project Hercules -- --telegram
```

## 7. Упаковка и установка для конечного пользователя
Hercules распространяется как папка с приложением (`publish`) + `data/` для runtime-данных.

### Публикация
```bash
# Публикация бэкенда (self-contained или framework-dependent)
dotnet publish src/agent/Hercules.WebApi -c Release -o ./dist/webapi

# Публикация фронтенда
cd src/hercules-web
npm install
npm run build
# статика окажется в src/hercules-web/dist
```

### Запуск из папки
```bash
# Бэкенд
./dist/webapi/Hercules.WebApi

# Фронтенд — можно раздать любым статическим сервером
npx serve src/hercules-web/dist -p 4321
```

### Рекомендуемая структура для пользователя
```
Hercules/
├── webapi/                 # публикация .NET
├── web/                    # статика Astro
├── data/                   # навыки, память, БД и runtime-config.json
└── start.bat / start.sh    # удобные скрипты запуска
```

> Важно: папка `data/` не должна попадать в git. В репозитории она уже в `.gitignore`.

## Что дальше
- [Архитектура](ARCHITECTURE.md) — как устроен агент
- [Конфигурация](CONFIGURATION.md) — все параметры
- [API](API.md) — справочник REST-эндпоинтов
