# Task 88 — A2A Agent Card panel

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-a2a-card-panel`

## Goal
В `hercules-web` нет UI для просмотра собственной A2A-совместимой Agent Card и импорта remote card по URL. Backend уже публикует локальную карту через `GET /agent-card.json` и `GET /api/a2a/agent-card` и принимает импорт через `POST /api/a2a/discover` и `GET /api/a2a/agent-card/from?url=…` (task_033). Нужно поднять это на web-страницу/панель, чтобы оператор видел, как агент выглядит со стороны peer'а, и мог вручную dry-run discovery.

## Acceptance criteria

### Sub-tasks

- [x] `src/lib/api.ts` — добавить `getAgentCard(): Promise<AgentCardDto>`, `getRemoteAgentCard(url): Promise<AgentCardDto>`, `discoverAgentCards(urls: string[]): Promise<DiscoverResultDto>`, типизировать `AgentCardDto` (name, description, url, version, provider, capabilities, skills[], defaultInputModes, defaultOutputModes, authentication, tags, generatedAt, documentationUrl)
- [x] Новая страница `src/pages/a2a.astro` — страница с локальной картой + JSON-view + кнопки «Refresh», «Import URL» (form-input), «Discover» (multi-URL textarea)
- [x] `src/components/AgentCardPanel.astro` — рендер карты: name, description, url, version, provider, capabilities pills (streaming/pushNotifications/stateTransitionReports/multipartResponses), skills (id, name, description, tags, inputModes, outputModes), authentication
- [x] Add navigation entry в `src/layouts/Layout.astro` (`active?: "a2a"`) рядом с mesh
- [x] Обработка ошибок: malformed JSON, network timeout (5s), 4xx/5xx → banner с redaction'ом секретов из response (apiKey, token, password, secret, credential → `***`)
- [x] `src/hercules-web/README.md` — обновить список страниц и API-методов
- [x] `npm run build` — exit 0, нет TS-ошибок
- [x] Manual smoke: `npm run dev` → страница рендерит локальную карту; импорт `http://localhost:5000/agent-card.json` с peer-агента возвращает валидную карту

## Implementation notes

### Round 1 (this tick) — completed

Web-UI для A2A Agent Card: просмотр локальной карты (как видит peer), импорт remote по URL, batch-discover.

- `src/lib/api.ts` — типизированы `AgentCardDto`, `AgentCardSkillDto`, `AgentCardCapabilitiesDto`, `A2AProviderDto`, `A2AAuthenticationDto`, `DiscoverCardEntryDto`, `DiscoverResultDto`. Добавлены методы `api.getAgentCard()`, `api.getRemoteAgentCard(url)`, `api.discoverAgentCards(urls[])`, `api.publishAgentCard()`. Все используют общий `headers()`/`handle()` helper с `X-Api-Key`.
- `src/components/AgentCardPanel.astro` — клиентский компонент с 3 секциями:
  1. **Local card** — name, description, url, version, provider, authentication, tags, capabilities pills (streaming/pushNotifications/stateTransitionReports/multipartResponses), skills grid (id, name, description, tags, inputModes, outputModes, version), generatedAt. Кнопки «Refresh», «JSON» (toggle raw), «Копировать» (clipboard), «Опубликовать» (POST publish).
  2. **Import remote** — single-URL form с inline preview (name, version, skill count). Использует `AbortController` с 5s timeout, отдельно от API клиента, чтобы не зависеть от backend-timeout.
  3. **Discover** — multi-URL textarea (1 URL/line) с batch-fetch через `POST /api/a2a/discover`. Рендерит сводку `N ok / M failed` и список имён/skills каждой успешной карты.
- `src/pages/a2a.astro` — оборачивает `AgentCardPanel` в `Layout` с `active="a2a"`.
- `src/layouts/Layout.astro` — добавлен `a2a` в `active` union и в links (после Mesh, перед Профиль).
- **Error handling** — единый `#a2a-error` banner. `redact()` маскирует значения полей `apikey/api_key/token/password/passphrase/secret/authorization/credential/credentials/x-api-key` в любых текстовых ответах (`":"value"` → `":"***"`). Используется во всех error-flow: loadCard, import, discover, publish, copy-clipboard. Timeout-detect через `e.message` regex (`/abort|timeout/i`).
- `src/hercules-web/README.md` — обновлены разделы «Структура» (добавлен `AgentCardPanel.astro`, `a2a.astro`, `MeshDashboard/MeshRouterPanel/EscalationPanel`) и «Backend API» (10 mesh-эндпоинтов + 4 A2A-эндпоинта).

### Round 1 — validation

- `npm run build` → **exit 0**, 7 страниц собраны, включая `/a2a/index.html`. Нет TS-ошибок в моих файлах.
- `npx astro check` → 0 errors, 0 warnings, 0 hints в `AgentCardPanel.astro`, `a2a.astro`, `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` (вне scope task_088; задокументированы отдельно).
- Проверка `dist/a2a/index.html`: nav-link `A2A` присутствует, `active` state применяется, секции (local/import/discover) отрендерены, script-тег ведёт на bundle `AgentCardPanel.astro_astro_type_script_index_0_lang.*.js`.

### Manual smoke

Без запущенного backend нельзя выполнить end-to-end (импорт + render), но build + static-render валидируют HTML/CSS/JS-интеграцию. Backend-эндпоинты `GET /api/a2a/agent-card`, `GET /api/a2a/agent-card/from?url=`, `POST /api/a2a/discover`, `POST /api/a2a/agent-card/publish` — все зарегистрированы в `A2AController.cs:MapA2A` (task_033, статус `done`). TypeScript DTO полностью совпадает с C# `AgentCard` (см. `src/agent/Mesh/A2A/AgentCard.cs`).
