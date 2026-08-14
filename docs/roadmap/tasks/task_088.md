# Task 88 — A2A Agent Card panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-a2a-card-panel`

## Goal
В `hercules-web` нет UI для просмотра собственной A2A-совместимой Agent Card и импорта remote card по URL. Backend уже публикует локальную карту через `GET /agent-card.json` и `GET /api/a2a/agent-card` и принимает импорт через `POST /api/a2a/discover` и `GET /api/a2a/agent-card/from?url=…` (task_033). Нужно поднять это на web-страницу/панель, чтобы оператор видел, как агент выглядит со стороны peer'а, и мог вручную dry-run discovery.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `getAgentCard(): Promise<AgentCardDto>`, `getRemoteAgentCard(url): Promise<AgentCardDto>`, `discoverAgentCards(urls: string[]): Promise<AgentCardDto[]>`, типизировать `AgentCardDto` (name, description, url, version, provider, capabilities, skills[], defaultInputModes, defaultOutputModes, authentication, tags)
- [ ] Новая страница `src/pages/a2a.astro` или секция в `memmesh.astro` — карточка с метаданными + JSON-view + кнопки «Refresh», «Import URL» (form-input), «Discover» (multi-URL textarea)
- [ ] `src/components/AgentCardPanel.astro` — рендер карты: name, description, url, version, provider, capabilities pills (streaming/pushNotifications/stateTransitionReports), skills (id, name, description, tags, inputModes, outputModes), authentication
- [ ] Add navigation entry в `src/layouts/Layout.astro` (`active?: "a2a"`) рядом с mesh
- [ ] Обработка ошибок: malformed JSON, network timeout (5s), 4xx/5xx → toast/banner с redaction'ом секретов из response
- [ ] `src/hercules-web/README.md` — обновить список страниц и API-методов
- [ ] `npm run build` — exit 0, нет TS-ошибок
- [ ] Manual smoke: `npm run dev` → страница рендерит локальную карту; импорт `http://localhost:5000/agent-card.json` с peer-агента возвращает валидную карту
