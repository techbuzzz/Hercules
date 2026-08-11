# Roadmap Cron — инструкция

`roadmap-driver` — ежедневный self-reminder, который двигает Hercules
по [docs/roadmap/backlog.md](../roadmap/backlog.md).

## Поведение

При каждом тике cron отправляет в текущую сессию Mavis следующий prompt:

> **Roadmap tick.** Просканируй `docs/roadmap/tasks/`. Найди первую задачу со
> статусом `pending`, у которой все зависимости (см. раздел **Dependencies**)
> в статусе `done`. Переведи её в `in_progress`, декомпозируй раздел
> **Acceptance criteria** в конкретные sub-tasks и приступай. Когда закончишь —
> переведи в `done` и закоммить. Если все задачи `done` (или явный `blocked`
> без прогресса) — выведи итоговый отчёт и попроси удалить этот cron.

## Что cron НЕ делает

- **Не модифицирует `ROADMAP-EN.md` / `ROADMAP-RU.md`** — это вышестоящий документ.
- **Не открывает PR** — только коммиты в текущей ветке; PR — решение оператора.
- **Не пушит в main** — только feature-ветки или прямой коммит в рабочую ветку.

## Создание

```bash
# Из корня репозитория
mavis cron create --agent mavis \
  --name roadmap-driver \
  --schedule "0 9 * * *" \
  --prompt "Roadmap tick: просканируй docs/roadmap/tasks/, выбери первую задачу со статусом pending, у которой все зависимости в статусе done. Переведи её в in_progress, декомпозируй Acceptance criteria в sub-tasks и приступай. Если все задачи done или blocked — выведи отчёт и попроси удалить этот cron. Ссылка: docs/roadmap/backlog.md" \
  --timezone "Europe/Berlin"
```

Параметры под себя:
- `--schedule` — cron-выражение; `0 9 * * *` = каждый день в 09:00.
- `--timezone` — IANA TZ; `Europe/Berlin` для пользователя в CEST.
- `--prompt` — текст prompt'а, который уйдёт агенту при тике.

## Управление

```bash
# Список
mavis cron list --agent mavis

# Включить/выключить
mavis cron update --cron-id <ID> --enabled false

# Удалить
mavis cron delete --cron-id <ID>
```

## Workflow при тике

1. Агент читает `docs/roadmap/backlog.md` и список `docs/roadmap/tasks/`.
2. Находит первую `pending` задачу с выполненными `Deps` (все указанные задачи в `done`).
3. Открывает `tasks/task_NNN.md`, заполняет `Acceptance criteria` конкретными sub-tasks.
4. Меняет `Status: pending` → `Status: in_progress` (коммит).
5. Работает по чекбоксам, по мере выполнения отмечает `[x]`.
6. По завершении — `Status: done` + короткое описание реализованного в конец файла.
7. Если застрял — `Status: blocked` + описание блокера.

## Идемпотентность

- Cron не стартует новый тик, пока предыдущий не завершён (single-flight).
- Если задача остаётся `in_progress` дольше суток — следующий тик увидит
  это и напомнит про статус (а не возьмёт новую).
- Если все задачи `done` или явный `blocked` — cron должен быть удалён
  вручную или попросить удалить в prompt'е.

## Когда удалить cron

- Roadmap доведён до конца — задачи в `done`.
- Долгое время ничего не двигается — нужна пауза на ретроспективу.
- Меняется ownership проекта или формат backlog.
