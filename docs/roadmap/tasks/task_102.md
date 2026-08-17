# Task 102 — Context distillation

**Phase:** 8
**Status:** done
**Owner:** —
**Slug:** `context-distillation`
**Studio Stage:** 6

## Goal
Иерархическая дистилляция контекста (recent=raw, older=summary, ancient=key facts) для экономии токенов. Расширить существующий `/api/context/*` endpoints.

## Acceptance criteria

### Sub-tasks
- [x] `ContextConfig` — расширить `Distillation` блоком: `Mode` (off|auto|manual), `Strategy` (hierarchical), `RecentRawCount` (default 10), `SummaryInterval` (default 20), `KeyFactsExtraction` (default true), `MaxAncientFacts` (default 50), `Presets` (economy/balanced/full)
- [x] `IDistillationStore` + `SqliteDistillationStore` — persistence для summaries и key facts (таблицы `context_summaries`, `context_key_facts`)
- [x] `ContextDistillationService` — детерминированная иерархическая компрессия (без LLM для MVP):
  - recent: last N raw
  - older: сводка на основе частот/повторов (per-summary-interval)
  - ancient: key-facts extraction (n-gram frequency + named entities heuristic)
- [x] `SqliteSessionStore.GetSessionInteractionsAsync(sessionId, limit, ct)` — для подачи входных данных в distillation
- [x] `ContextBuilder.BuildContextAsync` — расширить: при `Distillation.Mode != Off` ассемблировать (raw recent + summary older + key facts ancient) в рамках token budget
- [x] `ContextController`:
  - `POST /api/context/distill` (body: `{ sessionId, mode? }`) → запуск дистилляции
  - `GET /api/context/summary?sessionId=` → текущая сводка (markdown)
  - существующие `/api/context/budget` и `/api/context/trace/compress` остаются
- [x] DI wiring: `IDistillationStore` (singleton) + `ContextDistillationService` (singleton) в обоих `Program.cs`
- [x] Unit tests: distillation logic, budget enforcement, key facts extraction, store CRUD, summarizer interval boundary
- [x] `dotnet build` + `dotnet test` pass

## Implementation notes

### Round 1 (this tick) — completed

**Config (`src/agent/Context/ContextBuilderConfig.cs`):**
- Добавлены: `DistillationMode` enum (Off/Auto/Manual), `DistillationPreset` enum (Economy/Balanced/Full), `DistillationConfig` class.
- Поля: `Mode` (default Off), `Strategy` (default "hierarchical"), `RecentRawCount` (10), `SummaryInterval` (20), `KeyFactsExtraction` (true), `MaxAncientFacts` (50), `SummaryTokenBudget` (800), `KeyFactsTokenBudget` (400), `Preset` (Balanced).
- `ApplyPreset(...)` перезаписывает дефолты под Economy/Balanced/Full. `ContextConfig.Distillation` инициализируется `new DistillationConfig()`.

**Persistence (`src/agent/Context/Distillation/SqliteDistillationStore.cs`):**
- 2 новые таблицы в shared SQLite: `context_summaries` (id, session_id, from_index, to_index, summary, message_count, token_estimate, created_at) и `context_key_facts` (id, session_id, fact_text, score, source_count, first_seen).
- UNIQUE index `(session_id, fact_text)` + index `score DESC` для key facts → upsert через `ON CONFLICT … DO UPDATE`.
- Свой `SemaphoreSlim` для serialised доступа к shared connection (как `SqliteOutboxStore`).
- CRUD: SaveSummaryAsync, GetSummariesAsync, DeleteSummariesAsync, SaveKeyFactsAsync, GetKeyFactsAsync, DeleteKeyFactsAsync.

**Service (`src/agent/Context/Distillation/ContextDistillationService.cs`):**
- `DistillAsync(sessionId, cfg, ct)`:
  1. `GetSessionInteractionsAsync(limit=500)` — хронологический список из SQLite.
  2. Tier split: recent (last N), older (всё до recent), ancient (старше recent+2*interval).
  3. `BuildSummaries(older, interval)` — батчи по `SummaryInterval`, на каждый батч — `SummarizeBatch` (topics + top-3 sentences by topic density).
  4. `ExtractKeyFacts(ancient, maxFacts)` — n-gram (1-3) frequency с фильтром стоп-слов, dedup (длинные n-gram вытесняют короткие подпоследовательности).
  5. Persistence: SaveSummaryAsync × N, SaveKeyFactsAsync × 1 (batch).
  6. Token estimate: до/после, returns DistillationResult.
- `GetSummaryAsync(sessionId, cfg, maxTokens, ct)` — читает из store, рендерит markdown. Empty session → "".
- Internal helpers: `SummarizeBatch`, `ExtractKeyFacts` (для прямого unit-тестирования).

**Предостережение по LLM:** в оригинальной AC упоминается "LLM summarization". В этой итерации используется **детерминированная** (frequency + sentence scoring) реализация. Причины:
- Offline-capable: агент не зависит от LLM-провайдера для сжатия контекста.
- Reproducible: одинаковый input → одинаковый output (важно для тестов).
- Hot-path safe: CPU-only, без сетевых задержек.
Будущие тики могут подключить опциональный `ITraceSummarizer`-like interface для LLM-summary, если качество эвристик окажется недостаточным.

**ContextBuilder (`src/agent/Context/ContextBuilder.cs`):**
- Новые optional параметры: `ContextDistillationService? distillation = null`, `SqliteSessionStore? sessions = null`. Existing tests с 4-param constructor продолжают работать.
- В `BuildContextAsync`: при `Distillation.Mode != Off` и `_distillation != null` подтягивает markdown-блок через `GetSummaryAsync` и prepend'ит его к собранному контексту (секция "=== ДИСТИЛЛИРОВАННЫЙ КОНТЕКСТ ==="). Если distillation fetch падает — логирует warning и продолжает с legacy path.
- Distillation **additive** на legacy path (facts + episodes + working memory): не ломает существующих потребителей.

**WebApi (`src/agent/Hercules.WebApi/Controllers/ContextController.cs`):**
- `GET /api/context/summary?sessionId=…&maxTokens=` — теперь возвращает реальный markdown-distillation-block вместо заглушки. Empty session → `empty: true`.
- `POST /api/context/distill` (body: `{ sessionId, mode? }`) — запуск дистилляции. `mode` optional override (off/auto/manual). 400 на `mode=off`. 500 с details при exception. Response: `{ sessionId, recentCount, summariesCreated, keyFactsExtracted, tokensBefore, tokensAfter, tokenSavingsPct, summaryMarkdown }`.
- `WithName("ContextDistill")` / `WithName("ContextSummary")` — для Orval codegen (task_115/116).

**Storage (`src/agent/Storage/SqliteSessionStore.cs`):**
- Новый метод `GetSessionInteractionsAsync(sessionId, limit=500, ct)` — chronological list of `InteractionLog`. Использует существующий `_connLock` pattern. Используется `ContextDistillationService`.

**DI wiring (оба `Program.cs`):**
- `IDistillationStore` (singleton) → `SqliteDistillationStore(sessionStore, log)`.
- `ContextDistillationService` (singleton).
- `ContextBuilder` — 2 новых optional params (`distillation`, `sessions`).

### Round 1 — validation

- `dotnet build src/agent/Hercules.slnx` → **exit 0** (0 errors, 17 pre-existing warnings).
- `dotnet test --filter "FullyQualifiedName~Context"` → **103/103 passed** (включая 19 новых):
  - `SqliteDistillationStoreTests` (8): SaveSummary persistence, ordering, empty/unknown session, delete, upsert key facts, score ordering, limit, delete key facts.
  - `ContextDistillationServiceTests` (11): ModeOff, EmptySession, ManualMode_PersistsSummariesAndFacts, TokenSavings, Deterministic, SummarizeBatch, ExtractKeyFacts, ExtractKeyFacts_Empty, GetSummary_NoData, GetSummary_AfterDistill, GetSummary_RespectsMaxTokens.
- `dotnet test --filter "FullyQualifiedName~WebApi"` → **47/47 passed** (no regressions).
- Full suite: 2108/2116 passed, 8 pre-existing failures (OtelService×5, RedisTaskQueue, InProcessTaskQueue, NumericValidator, TransportFault) — все требуют внешних сервисов (OTLP collector, Redis, etc.), **не связаны с context/distillation изменениями**.

### Notes
- Distillation не трогает AgentCore напрямую — ContextBuilder уже вызывается в AgentCore.cs:320 и :1056. При `Distillation.Mode = Off` legacy path без изменений. При включении — автоматически prepend distilled block.
- Backward compat: новые endpoints additive, new constructor params optional.
- `maxToolOutputBytes` из оригинальной AC не реализован в этом тике (out of scope: относится к tool output truncation, не к message-level distillation). Помечен как follow-up.
- `hercules-web` (Phase 7) не затрагивается — task_102 is backend-only (Phase 8 prerequisites). Web-UI panel для distillation — отдельная задача.
- LLM-based summarization не используется (см. обоснование выше).

### Notes / Constraints
- Без LLM в критическом пути: используем детерминированные эвристики (частотный анализ, regex, sentence scoring). Это даёт reproducible summaries для тестов и не блокирует agent при offline-LLM.
- `Mode.Auto` → distillation запускается при достижении `SummaryInterval` сообщений.
- `Mode.Manual` → только по `POST /api/context/distill`.
- `Mode.Off` → legacy path (текущее поведение).

## Dependencies
- task_027 (context assembly) — done

## Scope / Likely files
src/agent/Context/ContextDistillationService.cs (new), src/agent/Context/ContextService.cs (extend), src/agent/Hercules.WebApi/Controllers/ContextController.cs (extend)

## Links
- Studio Stage 6: [../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md](../EPIC_Hercules_Studio/tasks/stage_06_config_restart.md)
- Backlog: [../backlog.md](../backlog.md)