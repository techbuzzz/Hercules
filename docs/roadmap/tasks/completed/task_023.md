# Task 23 — Детерминированный fallback маршрутизатора

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `deterministic-router`

## Goal
No-embedding режим: tags, keyword triggers, declared input types; edge deployments остаются работоспособными offline и на ограниченных ресурсах.

## Acceptance criteria

### Sub-tasks

- [x] `Storage/Models.cs` — `SkillMeta`: добавить `InputTypes` (List<string>, e.g. "code", "writing", "qa", "analysis", "translation") и `Tags` (List<string>, e.g. "python", "api", "debug")
- [x] `Config/Phase2Config` — добавить `DeterministicRoutingConfig`: `FallbackMode` enum ("Never", "OnNoEmbedding", "Always"), `EnableTagMatching` (default true), `EnableInputTypeMatching` (default true)
- [x] `Skills/Routing/Deterministic/IDeterministicRouter.cs` — интерфейс: `DeterministicRouteResult RouteDeterministic(input)` и `bool IsAvailable`
- [x] `Skills/Routing/Deterministic/DeterministicRouter.cs` — реализация: `KeywordScorer` (phrase-receivers count), `TagScorer` (tag intersection), `InputTypeScorer` (type match); weighted combination; returns top skill or null
- [x] `Skills/Routing/Deterministic/DeterministicRouteResult.cs` — record: `Skill?`, `Score`, `Methods` (list of matched methods: "keyword"|"tag"|"type")
- [x] `EmbeddingSkillRouter` — интеграция: когда `FallbackMode=Always` или `FallbackMode=OnNoEmbedding` + semantic unavailable → вызывает `DeterministicRouter.RouteDeterministic`; передаёт `DeterministicRoutingConfig` в конструктор
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать `DeterministicRouter` в DI
- [x] `Phase2Tests/DeterministicRouterTests.cs` — unit-тесты: keyword-only routing, tag matching, input type matching, combined scoring, no-match returns null, offline mode (Always)
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (DeterministicRouterTests: 20/20; pre-existing failures in OtelService + BudgetGuard + WasmTool — unchanged)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Storage/Models.cs` — SkillMeta:**
- `InputTypes` (List<string>, JSON: "input_types") — declared input types: "code", "writing", "qa", "analysis", etc.
- `Tags` (List<string>, JSON: "tags") — tags: "python", "api", "debug", etc.

**`Config/AppConfig.cs` — Phase2Config + DeterministicRoutingConfig:**
- `DeterministicFallbackMode` enum: Never / OnNoEmbedding / Always
- `DeterministicRoutingConfig`: FallbackMode (default OnNoEmbedding), EnableTagMatching, EnableInputTypeMatching, ScoringWeights dict

**`src/agent/Skills/Routing/Deterministic/` — 3 новых файла:**
- `DeterministicRouteResult.cs` — record: Skill?, Score, MatchedMethods list; IsSkill helper, None static
- `IDeterministicRouter.cs` — интерфейс: Route(input) → DeterministicRouteResult, IsAvailable = true
- `DeterministicRouter.cs` — реализация:
  - Keyword scoring: raw count of matched phrase-receivers (like legacy SkillRouter)
  - Tag scoring: normalized intersection |input_tags ∩ skill_tags| / max(|input_tags|, 1)
  - Type scoring: normalized intersection of inferred input types from keywords
  - Input type inference: maps 70+ tech keywords to types ("debug" → "code", "write" → "writing", etc.)
  - Tag extraction: 80+ known technology tags from input words
  - Scoring: keyword raw-count × 0.60 + tag_ratio × 0.25 + type_ratio × 0.15
  - Tiebreaker: SuccessRate (higher wins)

**`Skills/EmbeddingSkillRouter.cs` — обновлён:**
- Добавлен `IDeterministicRouter?` в конструктор (nullable, backward-compatible)
- Fallback chain: Semantic → Deterministic (OnNoEmbedding/Always) → Legacy keyword (Never/useKeywordFallback)
- `TryDeterministicRouting()` — внутренний метод, проверяет FallbackMode и вызывает router
- `ScoreAllAsync` — deterministic path returns single-item SkillScoreResult list

**DI (CLI + WebAPI Program.cs):**
- `IDeterministicRouter` → `DeterministicRouter(SkillManager, DeterministicRoutingConfig)`
- `EmbeddingSkillRouter` получает `sp.GetService<IDeterministicRouter>()` (nullable)

**`tests/Phase2Tests/DeterministicRouterTests.cs` — 20 новых тестов:**
- Availability: IsAvailable always true
- Empty/whitespace: returns None
- Keyword-only: single/multiple keywords, more-matches-wins, success-rate tiebreaker
- Tag matching: with keyword, without keyword, disabled tag matching
- Input type: code, writing, no-types-defined
- Combined: keyword+tag+type, all methods present
- Fallback modes: Never, OnNoEmbedding, Always
- Score: positive for matching skill

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test --filter DeterministicRouter` — 20/20 passed
- `dotnet test` — 709/716 (7 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1 — unchanged from previous runs)

## Scope / Likely files
src/agent/Skills/Routing/KeywordRouter.cs

## Dependencies
- блокирует / опирается на: [task_022 — semantic-routing](task_022.md)

## Risks / Rollback
Снижение качества маршрутизации; пользовательский override обязателен.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
