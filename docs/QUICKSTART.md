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
Откройте `appsettings.json` и укажите активный провайдер и ключи. Пример для локального Ollama:
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

## 5. Запуск Web API + фронтенда (опционально)
```bash
# Терминал 1 — бэкенд (порт :5000)
dotnet run --project Hercules.WebApi

# Терминал 2 — фронтенд (порт :4321)
cd hercules-web
npm install
cp .env.example .env   # при необходимости поправьте PUBLIC_API_BASE / PUBLIC_API_KEY
npm run dev
```
Откройте `http://localhost:4321`.

## 6. Запуск Telegram-бота (опционально)
```bash
export HERCULES_Telegram__Enabled=true
export HERCULES_Telegram__BotToken=<токен от @BotFather>
dotnet run --project Hercules -- --telegram
```

## Что дальше
- [Архитектура](ARCHITECTURE.md) — как устроен агент
- [Конфигурация](CONFIGURATION.md) — все параметры
- [API](API.md) — справочник REST-эндпоинтов
