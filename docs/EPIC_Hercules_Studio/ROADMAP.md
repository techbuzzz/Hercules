# Hercules Studio — Roadmap

> Связанные документы: [README.md](README.md), [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md), [VERIFICATION.md](VERIFICATION.md), [tasks/](tasks/)

> # План-факт
>
> **Планом-фактом является раздел [Миграция на web-first](#миграция-на-web-first-2026-10-06)**,
> утверждённый владельцем 2026-10-06 (см. [ADR-0009](adr/0009-web-first-studio.md)).
>
> **Разделы Stage 0–9 ниже — SUPERSEDED как план-факт.** Они сохранены только как источник
> функциональных требований. Не планируйте и не отмечайте работу в них — переносите
> требования в web-first-план. Подробности отмены по каждому Stage — в блоке
> [⛔ Stage 0–9 — SUPERSEDED](#️-stage-09--superseded-как-план-факт).

> ⚠️ **Архитектура изменена 2026-10-06 — см. [ADR-0009](adr/0009-web-first-studio.md).**
> Ниже этапы описывают Stage 0–9 в прежней, Electron-ориентированной формулировке.
> Актуальное состояние миграции на web-first — в разделе
> [Миграция на web-first](#миграция-на-web-first-2026-10-06) ниже. Ключевые поправки:
>
> - Studio — **браузерное SPA**, не Electron-приложение. Агент раздаёт его на `/ui`.
> - Stage 0 больше не про «skeleton Electron + IPC», а про **платформенный слой**
>   (`renderer/src/platform/`) вместо `window.studioAPI`.
> - «Agent Scanner» = A2A discovery (`/agent.manifest.json`), а не скан портов и процессов.
> - Локальный терминал (xterm.js) заменён потоком вывода от агента.
> - Restart/PID-реестр переехали в отдельный процесс `Hercules.Supervisor`
>   (`src/hercules-supervisor`), а не в UI.
> - Этапы ниже сохраняются как источник функциональных требований (что строим),
>   но порядок работ определяется миграционными фазами.

## Обзор

9 этапов разработки Studio + backend prerequisites. Каждый этап = отдельный task-файл в [tasks/](tasks/).

```
Stage 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9
 └ skeleton  └ chat  └ mesh └ cfg └ cons─┬─ workflow └ pack
                                          └─ hercules-workflow-server (backend)
```

**Параллельно:** backend prerequisites (Phase 8 в [backlog.md](../roadmap/backlog.md)) выполняются до или вместе с соответствующими этапами Studio.

---

## Миграция на web-first (2026-10-06)

> ### ⚠️ Не путать `Phase` и `Stage`
>
> - **Stage 0–9** (раздел ниже, `tasks/stage_00`…`stage_09`) — **продуктовый** план
>   функциональностей. Он **далеко не выполнен**: Monaco и история/диффы (Stage 2),
>   Vue Flow (Stage 4), MCP CRUD (Stage 5), дистилляция контекста и Postgres
>   (Stage 6), консилиум-движок и BPMN-дизайнер (Stage 7–8), упаковка (Stage 9) —
>   всё это **не реализовано**.
> - **Phase 0–6** (этот раздел) — **миграционные** фазы переезда на web-first. Они
>   про то, *чем Studio является*, а не о том, *что Studio умеет*.
>
> ### ⚠️ Task-файлы Stage 0–9 устарели и не отражают сделанное
>
> Состояние чекбоксов на 2026-10-06:
>
> | Файл | done | open |
> |---|---|---|
> | `stage_00_skeleton` | 50 | 102 |
> | `stage_01` … `stage_09` | **0** | 243 |
>
> Это **не** означает, что ничего нет. Часть Stage 1–7 закрыта по факту миграции
> (сканирование агентов, чат, навыки, config, mesh, tools, консилиум реализованы
> во вью Studio), но соответствующие чекбоксы никогда не обновлялись.
> Числа выше — верхняя граница невыполненного, а не точный остаток.
>
> **Честный остаток** (что реально отсутствует в Studio):
> Monaco и история/диффы (Stage 2), Vue Flow (Stage 4), **MCP CRUD (Stage 5)**,
> дистилляция контекста и Postgres (Stage 6), движок консилиума и BPMN-дизайнер
> (Stage 7–8), упаковка (Stage 9). Полный список — в
> [`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).
>
> Синхронизация чекбоксов с фактическим состоянием — отдельная задача; здесь она
> намеренно не выполнена, чтобы не заявлять невыполненную работу.
>
> **Проверка:** результаты всех прогонов и статус критериев приёмки — в
> [VERIFICATION.md](VERIFICATION.md). Он существует, чтобы результаты можно было
> **проверить**, а не только принять на слово.

> **Граница цели — ⚠️ НЕ ПОДТВЕРЖДЕНА владельцем.** Вопрос «входит ли роадмап
> Stage 0–9 в цель?» был задан, но ответ пришёл **по таймауту с автовыбором
> рекомендованного варианта**, а не от владельца. Владелец его не подтвердил.
>
> Пока подтверждения нет, 243 открытые задачи Stage 0–9 формально остаются в области
> цели, и **завершённость цели не считается установленной**.
>
> Решения, которые владелец **подтвердил явно**: chat остаётся request-response;
> `hercules-web` объявляется deprecated, а не портируется (это закрывает критерий 9).
>
> Подробности и состояние работ — в [VERIFICATION.md](VERIFICATION.md#out-of-scope---
> -not-yet-confirmed-by-the-owner) и
> [`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).

**Происхождение:** этот раздел заменяет Stage 0–9 как план-факт миграции, утверждён
владельцем 2026-10-06 вместе с решением «chat остаётся request-response» и
«`hercules-web` не портируется, а объявляется deprecated». Решения зафиксированы в
[ADR-0009](adr/0009-web-first-studio.md) и воспроизведены ниже; сами ADR и этот раздел
написаны в рамках той же миграции, поэтому независимого документа плана до них не
существует.

Фактическое состояние миграции по фазам. Этапы Stage 0–9 ниже остаются источником
функциональных требований; ниже — порядок работ и прогресс.

| Фаза | Содержание | Статус |
|---|---|---|
| **Phase 0** | Починка сборки: реальные зависимости в `package.json`, удаление Electron-конфигов, порт 4330 (4322 занят `hercules-web`) | ✅ готово |
| **Phase 1** | Платформенный слой `renderer/src/platform/` (`capabilities.ts` / `web.ts` / `index.ts`) вместо `window.studioAPI`; `mock-api.ts` и `shared/protocol.ts` удалены | ✅ готово |
| **Phase 1** | Хранение состояния: версионированные ключи `hercules-studio.*.v1`, одноразовая миграция с legacy-блоба, API-ключ не попадает в localStorage | ✅ готово |
| **Phase 2a** | Сессионная аутентификация на агенте: `StudioSessionStore`, `X-Session-Token` в `ApiKeyMiddleware`, `POST /api/studio/session` | ✅ готово |
| **Phase 2a** | Раздача SPA агентом на `/ui` (`UseDefaultFiles` + `UseStaticFiles`), `base: "/ui/"` во Vite | ✅ готово |
| **Phase 2a** | CORS-whitelist агента дополнен портом 4330 | ✅ готово |
| **Phase 2b** | Запуск кода в sandbox + SSE-поток результата: `POST /api/code/run`, `GET /api/code/run/{runId}/stream`, `GET /api/code/run/{runId}` | ✅ готово |
| **Phase 2b** | SSE-поток для чата | ✅ закрыто решением владельца — стриминг не делаем |
| **Phase 3** | `Hercules.Supervisor` (`src/hercules-supervisor`): health-цикл, `restart-pending`, PID-реестр на `GET /supervisor/agents` | ✅ готово |
| **Phase 4** | Замена placeholder-видов на реальные | ✅ 8 из 8 |
| **Phase 5** | Порт панелей `hercules-web` в Studio | ✅ закрыто решением владельца — порт не делаем |
| **Phase 6** | PWA: manifest + service worker | ✅ готово |

### Что изменилось в смежных компонентах

Агент:
- `Auth/StudioSessionStore.cs` — in-memory реестр сессий (TTL из `WebApi:StudioSessionTtlMinutes`).
- `Controllers/StudioController.cs` — `POST/GET/DELETE /api/studio/session`.
- `Controllers/CodeRunController.cs` + `CodeRuns/SkillRunStore.cs` — запуск кода в sandbox
  с SSE-выводом и ограниченным буфером реплея (256 событий на прогон, TTL завершённых прогонов 15 мин).
- `Auth/ApiKeyMiddleware.cs` — приём `X-Session-Token` наравне с `X-Api-Key`.
- `Program.cs` — раздача `/ui`; CORS-порты 4330/8421.
- `Logging/FileLoggerProvider.cs` — `FileShare.ReadWrite` + проглатывание исключений логгера.
  Без этого рестарт под supervisor'ом ронял новый процесс необработанным `IOException`.
- `Mesh/A2A/AgentCardService.cs` — `AgentCard.Endpoint` задаётся как URL-путь
  (`/agent-card.json`), а `TrimStart('/')` превращал его в **относительный** путь ФС,
  и карточка писалась в рабочий каталог процесса. Это, во-первых, засоряло репозиторий
  (`src/agent/agent-card.json` при каждом старте), во-вторых, расходилось с читателем
  `GET /agent-card.json` (ищет в dataRoot) — из-за чего endpoint отдавал 404 при
  CWD ≠ dataRoot. Теперь относительные значения разрешаются от каталога манифеста,
  как и обещает док-комментарий конфига. Покрыто тестами
  `PublishAsync_ResolvesRelativeEndpointAgainstManifestDir` и
  `PublishAsync_HonoursAbsoluteEndpoint` (существующий тест использовал абсолютный
  `Endpoint` и баг не ловил — именно его форма и стоит в проде).
- Studio: `vue-i18n` поднят с 9 на **11** (v9/v10 больше не поддерживаются).
  Приложение уже работало в composition-режиме (`legacy: false`), поэтому миграция
  затронула только версию пакета; 12 E2E-тестов (включая «translations resolve») зелёные.

> **Переименование относительно плана.** План предполагал `POST /api/skills/{id}/run`.
> По факту `Skill` (`Storage/Models.cs`) — это `Meta + Description + Prompt`, поля с кодом
> у него нет: навык исполняет LLM, а не sandbox. Прогон нацелен на *кусок кода*
> (file-based C#-навык под тестом или сниппет из редактора), поэтому эндпоинт — `/api/code/run`.

Studio:
- `renderer/src/platform/` — контракт возможностей и браузерная реализация.
- Dev-порт 4330 (4322 занят `hercules-web`), `base: "/ui/"` — тот же путь, что и в проде.
- Диалог подключения вынесен на уровень приложения (`AddConnectionDialog`): пустое
  состояние предлагает «Add connection manually», а `AgentList` на нём не смонтирован.
- `ChatView` — диалог с агентом, инкрементальный «печатающийся» вывод, переход к
  навыку по предложению агента (через `stores/ui.ts`).
- `SkillsView` — список навыков, правка prompt, запуск кода в sandbox с выводом по SSE.
- `ConfigView` — правка живой конфигурации агента через merge `PATCH` (полная замена
  `PUT` намеренно не выведена: это system-операция). Перезапуск доступен только при
  system-роли сессии — Studio ставит флаг, рестарт делает supervisor.
- `MeshView` — реестр агентов с доверием, здоровьем и состоянием circuit breaker.
  Загрузка частичная: падение одного под-запроса (`/api/mesh/denials`) не гасит
  остальную таблицу.
- `ToolsView` — реестр инструментов с health, rate-limit и side-effect уровнем,
  переключатель enable/disable. Переключение оптимистичное, но **откатывается**,
  если агент отклонил изменение — иначе UI врал бы о состоянии инструмента.
- `ConsensusView` — очередь human-in-the-loop: согласования (`/api/approvals`) и
  эскалации (`/api/escalations`) с approve/deny, фильтром по severity и batch-approve.
  После любого действия очередь **перечитывается**, а не считается обновлённой локально.
  Списки грузятся независимо: недоступность эскалаций не скрывает согласования.
- `WorkflowView` — определения workflow с **отдельного** workflow-сервера, у которого
  своя аутентификация (`X-Client-Id`/`X-Client-Secret`, не сессия агента). Поэтому
  `requiresConnection: false` — вью работает без подключённого агента. Credentials
  хранятся только в памяти, по тем же основаниям, что и ключ агента. Кнопка Run
  помечена как заглушка: `run`/`executions` на бэкенде не реализованы (task_105).
- PWA — `manifest.webmanifest` + иконки + service worker. Кэш намеренно консервативный:
  навигация network-first с откатом на шелл, хэшированные ассеты cache-first,
  `/api/*` **никогда** не кэшируется (устаревший `/api/config` или повторный обмен
  сессией были бы откровенно вредны). Регистрация — `updateViaCache: "none"`, потому что
  агент отдаёт `sw.js` как иммутабельный ассет и без этого браузер год не подхватил бы
  обновление воркера.

### Ловушка проверки: typecheck не ловит ошибки разбора шаблонов

`npm run typecheck` (vue-tsc) прошёл для `ToolsView.vue`, а `vite build` упал на
`:placeholder="t("tools.search")"` — вложенные двойные кавычки обрывают значение
атрибута. Проверка `="[^"]*\bt\("` по шаблонам ловит это заранее.
**Перед тем как считать view готовым, запускайте `npm run build`, а не только typecheck.**
- Требует **system-role** API-ключ (`dev-system-key` в dev-конфиге). `contribute`-ключ
  получает 403 на `/api/system/restart` — это ожидаемо.
- Статус API: `GET /supervisor/agents`, `POST /supervisor/agents/{name}/{start|stop|restart}`.
- Рестарт выполняется **ровно один раз** на запрос; восстановление после неудачного
  старта — задача health-цикла (`AutoRestart`), а не повтор рестарта.

### Решение владельца: SSE для чата НЕ делаем (2026-10-06)

План предполагал, что чат можно перевести на SSE «просто»: request → поток токенов.
Проверка кода показала иное — **это не локальная правка эндпоинта**.

Что уже есть: `ILLMClient.StreamAsync` → `IAsyncEnumerable<string>`, с failover между
провайдерами и OTel-трейсом (`ResilientLLMClient.StreamAsync`). То есть на уровне LLM
потоковая генерация **реализована**.

Почему её нельзя просто поднять наверх:

1. **Стриминг живёт ниже слоя маршрутизации.** У `AgentCore` есть только `HandleAsync`
   (1243 строки, `HandleAsyncCore` — строки 288–727). `WebApiAdapter` умеет только
   `ChatAsync`. Прямой вызов `_llm.StreamAsync` из нового эндпоинта обошёл бы:
   выбор навыка (`route.IsSkill`), confidence/mode, запись использования навыка и
   `LatencyScorer`, предложения `proposeSkillForInput`/`proposeImproveSkill`,
   многослойную память, guardrails и списание бюджета.
2. **Ответ агента — не просто текст.** `RunWithToolsAsync` (строка 735) — это цикл
   tool-итераций: полный вывод LLM парсится на tool-вызов (`TryParseAction`), результат
   возвращается в transcript, LLM вызывается снова, до `MaxToolIterations`. Нельзя
   решить «посреди потока», это tool-вызов или финальный текст, — парсинг требует
   целого ответа. И заранее неизвестно, какая итерация финальная.

Варианты, и почему ни один не де-факто «правильный» без решения владельца:

| Вариант | Что получаем | Цена |
|---|---|---|
| Стримить только финальную итерацию | Токены «как есть» | Нужен полный рефакторинг `RunWithToolsAsync` + `HandleAsyncCore` (~440 строк) и новые тесты на routing/skill/memory/budget |
| Стримить только когда tools не сконфигурированы | Простой быстрый путь | Тихо теряет tool-итерации — регрессия против текущего поведения |
| Буферизовать и отдать постфактум | Ничего | Ровно то, что уже делает request-response |

Текущий `ChatView` поэтому показывает ответ целиком и честно помечает эффект печати
как презентационный. **Решение владельца: оставить request-response, стриминг не делать.**
Честный стриминг остаётся возможным, но требует отдельной задачи на рефакторинг
tool-цикла; выбранный «быстрый путь» отвергнут сознательно, потому что он тихо
убирал бы tool-вызовы.

### Принятые потери

Интерактивный PTY, просмотр таблицы процессов из UI, трей, установщик/автообновление.
Компенсации: PID-реестр supervisor'а, A2A discovery, лог-файлы supervisor'а, PWA в Phase 6.

---

# ⛔ Stage 0–9 — SUPERSEDED как план-факт

**Разделы Stage 0–9 ниже больше не являются план-фактом.** Планом-фактом является раздел
**[Миграция на web-first](#миграция-на-web-first-2026-10-06)** выше, утверждённый владельцем
2026-10-06.

Этапы сохранены **только как источник функциональных требований** — что в итоге должно
существовать в Studio. Их статус в чекбоксах устарел (см. блок «Task-файлы Stage 0–9
устарели» выше), и они описывают архитектуру, часть которой отменена:

| Stage | Что в нём отменено миграцией |
|---|---|
| Stage 0 | «Skeleton **Electron** + Vue + Vite + **IPC**» — Electron и IPC удалены (ADR-0009). API codegen pipeline остаётся актуальным и **не реализован** |
| Stage 1 | Сканер портов и процессов → A2A discovery (`/agent.manifest.json`) |
| Stage 2 | Локальный терминал (xterm.js) → вывод от агента; Monaco/история/диффы **не реализованы** |
| Stage 6 | «Restart из UI» → флаг + `Hercules.Supervisor` |
| Stage 9 | «Packaging» (NSIS/portable) — отменён: десктопной сборки нет, вместо неё PWA |

**Что делать с оставшимися требованиями:** переносить их в разделы web-first-плана, а не
отмечать в Stage-файлах. Актуальный список пробелов — в
[`src/hercules-web/DEPRECATED.md`](../../src/hercules-web/DEPRECATED.md).

---

## Stage 0 — Skeleton + API Codegen (1-2 недели)

**Цель:** Запускаемый skeleton Electron + Vue + Vite + IPC + layout. API codegen pipeline (openapi-typescript + Orval + Vue Query) настроен и генерирует TS client из OpenAPI документа агента.

**Зависимости от бэкенда:**
- `task_109` — AddOpenApi() в Program.cs (OpenAPI 3.1 producer)
- `task_110` — WithTags на все контроллеры (domain grouping для Orval tags-split)
- `task_111` — Produces\<T\>() + DTO рефакторинг (исключить анонимные типы)
- `task_112` — WithName на Marketplace + Template (operationId для Orval)

**Studio tasks:**
- `task_113` — openapi-typescript + openapi-fetch + Orval setup
- `task_114` — migrate stores to Vue Query hooks

**Результат:** Studio запускается, можно добавить агента по URL, виден manifest. License consent при first-run. Empty state с marketing carousel. API client auto-generated из openapi.json (types + Vue Query hooks + Zod + MSW mocks).

📄 [tasks/stage_00_skeleton.md](tasks/stage_00_skeleton.md)

---

## Stage 1 — Agent Scanner + Connection Manager + CheckIn/CheckOut (1-2 недели)

**Цель:** Multi-agent management + сканирование машины + checkin/checkout.

**Зависимости от бэкенда:**
- `task_096` — Port migration 5000 → 8421 (отдельный PR до старта)
- `task_097` — Dual API keys (contribute + system)
- `task_098` — CheckIn/CheckOut protocol

**Результат:** Studio находит агентов на машине (port scan + process scan), подключается, переключается между ними. CheckIn/CheckOut с heartbeat.

📄 [tasks/stage_01_agent_scanner.md](tasks/stage_01_agent_scanner.md)

---

## Stage 2 — Chat + Skill Editor (2-3 недели)

**Цель:** Паритет с hercules-web по чату + полноценный skill editor с Monaco.

**Зависимости от бэкенда:** нет (текущий API достаточен)

**Результат:** Чат с активным агентом, история сессий, skill editor (prompt.md, meta.json, description.md), version diff, lifecycle actions, C# basic syntax highlighting, "Test in chat".

📄 [tasks/stage_02_chat_skills.md](tasks/stage_02_chat_skills.md)

---

## Stage 3 — Skill Authoring + Push (1-2 недели)

**Цель:** Создание навыков в Studio и push на агента.

**Зависимости от бэкенда:** нет (текущий API: POST/PUT/skills/import)

**Результат:** Push skill (POST/PUT/import), cross-agent install, skill templates (3-5 базовых + file-based .NET examples), pre-check через manifest validate + DangerousCodeScanner.

📄 [tasks/stage_03_skill_push.md](tasks/stage_03_skill_push.md)

---

## Stage 4 — Mesh Explorer (2 недели)

**Цель:** Визуальная карта mesh + управление peer'ами.

**Зависимости от бэкенда:** нет (текущий mesh API)

**Результат:** Vue Flow graph topology (1-hop), node details, router explorer, shared memory browser, circuit breaker panel, auto-refresh 30s.

📄 [tasks/stage_04_mesh_explorer.md](tasks/stage_04_mesh_explorer.md)

---

## Stage 5 — Tool Registry + MCP Management (1-2 недели)

**Цель:** Управление инструментами и MCP-серверами на агенте.

**Зависимости от бэкенда:** нет (текущий tools/MCP API; MCP add/remove через PATCH config, warning restart до Stage 6)

**Результат:** Tools list (enable/disable/health), MCP servers (list/add/edit/delete/reload), pre-check MCP before push.

📄 [tasks/stage_05_tools_mcp.md](tasks/stage_05_tools_mcp.md)

---

## Stage 6 — Тонкая настройка + Restart + SkillSdk + Context Distillation + Postgres (2-3 недели)

**Цель:** Полная настройка агента + удалённый restart + C# sandbox integration + context compression + centralized storage.

**Зависимости от бэкенда:**
- `task_099` — `POST /api/system/restart` (supervisor protocol)
- `task_100` — `McpClientService : IConfigReload` (MCP hot-reload)
- `task_101` — SkillSdk (`Hercules.SkillSdk` NuGet + `HerculesSkillContext`)
- `task_102` — Context distillation (`/api/context/distill`, `/api/context/summary`)
- `task_103` — Postgres session store (`ISessionStore` abstraction)

**Результат:** LLM/roles/mesh/quotas/context budget editor, raw config editor с diff, restart button (supervisor protocol), MCP hot-reload (без restart), C# file-based apps test-run через sandbox, context distillation UI, Postgres centralized storage config.

📄 [tasks/stage_06_config_restart.md](tasks/stage_06_config_restart.md)

---

## Stage 7 — Консилиум (2-3 недели)

**Цель:** Параллельный chat с N агентами + агрегация.

**Зависимости от бэкенда:** нет (Studio orchestrates parallel `/api/chat`)

**Результат:** Multi-agent chat (columns side-by-side), LLM-judge (один агент-судья) + Manual pick. Voting + Merge = next gen. Notifications для async operations.

📄 [tasks/stage_07_consensus.md](tasks/stage_07_consensus.md)

---

## Stage 8 — BPMN Workflow Designer (3-4 недели)

**Цель:** Визуальное проектирование workflow между агентами.

**Зависимости от бэкенда:**
- `task_104` — hercules-workflow-server (отдельный .NET сервис, workflow engine, `/api/workflows/*`, clientId/clientSecret auth, webhook + cron triggers)
- `task_105` — Workflow graph model + executor
- `task_106` — DelegatedTask persistence (SQLite)
- `task_107` — Parent/child task relationships
- `task_108` — DurableTask checkpoint persistence

**MVP (Stage 8a):** Studio-orchestrator. Linear sequence + Conditional. Graph stored в SQLite.workflows. Studio executor дёргает `/api/mesh/intent`. Long-poll monitoring.

**Production (Stage 8b):** hercules-workflow-server. Полный BPMN (parallel/exclusive/inclusive gateways, timer/error/escalation events, sub-processes). Graph stored в workflow-server. Workflow templates (3-5 + 1-2 corporate). Human-in-the-loop (AwaitingInputContext).

📄 [tasks/stage_08_workflow.md](tasks/stage_08_workflow.md)

---

## Stage 9 — Packaging & Polish (1-2 недели)

**Цель:** Production-ready Windows installer + tray + auto-updater + tests.

**Результат:** NSIS installer + portable zip, tray icon, auto-updater (GitHub Releases), native notifications, CI (GitHub Actions Windows), Vitest unit tests, Playwright E2E, Biome lint, TS strict.

📄 [tasks/stage_09_packaging.md](tasks/stage_09_packaging.md)

---

## Сводная таблица

| Stage | Срок | Backend deps | Ключевой результат |
|---|---|---|---|
| 0 | 1-2 нед | task_109-112 (OpenAPI) + task_113-114 (Studio codegen) | Skeleton, license, empty state, API codegen pipeline |
| 1 | 1-2 нед | task_096-098 | Scanner, connections, checkin/checkout |
| 2 | 2-3 нед | — | Chat, skill editor (Monaco) |
| 3 | 1-2 нед | — | Skill push, cross-agent, templates |
| 4 | 2 нед | — | Mesh explorer (Vue Flow) |
| 5 | 1-2 нед | — | Tools + MCP management |
| 6 | 2-3 нед | task_099-103 | Config, restart, SkillSdk, distillation, Postgres |
| 7 | 2-3 нед | — | Consensus (multi-agent chat) |
| 8 | 3-4 нед | task_104-108 | BPMN workflow (MVP → production) |
| 9 | 1-2 нед | — | Packaging, tray, auto-update, tests |
| **Total** | **~16-26 нед** | **21 backend tasks** | **Hercules Studio 0.6.x** |

> **Параллельно с Studio:** task_115-116 (Web-UI codegen + migration) — тот же OpenAPI pipeline для hercules-web.

## Совместимость Studio ↔ Agent

| Studio | Agent version | Notes |
|---|---|---|
| 0.1.x (Stage 0-2) | + OpenAPI producer (task_109-112) + port migration (task_096) | Basic: chat, skills, config. API codegen pipeline active |
| 0.2.x (Stage 1-3) | + dual API keys (task_097) + CheckIn/CheckOut (task_098) | Scanner, multi-agent, push |
| 0.3.x (Stage 4-5) | текущая | Mesh, tools, MCP |
| 0.4.x (Stage 6) | + restart (task_099) + MCP reload (task_100) + SkillSdk (task_101) + distillation (task_102) + Postgres (task_103) | Full config, C# skills, restart |
| 0.5.x (Stage 7) | текущая | Consensus |
| 0.6.x (Stage 8-9) | + workflow-server (task_104-108) | BPMN workflows, packaging |

> **Web-UI** parallel migration: task_115-116 (openapi-typescript + Orval + Vue Query) — hercules-web переходит на generated API client одновременно со Studio.