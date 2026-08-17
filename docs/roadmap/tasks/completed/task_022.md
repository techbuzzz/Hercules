# Task 22 — Семантическая маршрутизация

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `semantic-routing`

## Goal
SkillRouter ранжирует применимые навыки по embedding similarity, lexical match, input-schema compatibility, историческому качеству, latency и policy eligibility.

## Acceptance criteria

### Sub-tasks

- [x] `Skills/Routing/ISkillScorer.cs` — interface with `Score(input, skill) → ComponentScore` and `ComponentName`
- [x] `Skills/Routing/SkillScoreResult.cs` — `SkillScoreResult` record: `Skill`, `OverallScore`, `IsEligible`, `ComponentScores` dictionary, `PrimaryMethod`
- [x] `Skills/Routing/ScoringComponents/EmbeddingScorer.cs` — cosine similarity using `IEmbeddingProvider` (replaces inline logic in `EmbeddingSkillRouter`); configurable weight
- [x] `Skills/Routing/ScoringComponents/LexicalScorer.cs` — phrase-receiver exact match; configurable weight
- [x] `Skills/Routing/ScoringComponents/SchemaCompatibilityScorer.cs` — checks `SkillMeta.InputSchemaVersion` declared tools; returns 0 (incompatible) or 1 (compatible); configurable weight
- [x] `Skills/Routing/ScoringComponents/HistoricalQualityScorer.cs` — weights by `SkillMeta.SuccessRate` × `SkillMeta.LastEvaluationScore`; configurable weight
- [x] `Skills/Routing/ScoringComponents/LatencyScorer.cs` — weights by historical avg latency from `SqliteSessionStore`; configurable weight
- [x] `Skills/Routing/ScoringComponents/PolicyEligibilityScorer.cs` — checks `ToolPolicyEngine` for skill's declared tools; ineligible skills return `IsEligible=false`; configurable weight
- [x] `Skills/Routing/SkillScoringEngine.cs` — `ISkillScoringEngine`: combines all enabled scorers with weights → final `SkillScoreResult`; `ScoreAsync(input)` → ranked list; uses `EmbeddingSkillRouter` for embedding-only path when only embedding scorer enabled
- [x] `Config/Phase2Config` — add `SkillScoringWeights` section with weights per component (defaults: embedding=0.40, lexical=0.20, schema=0.15, quality=0.15, latency=0.05, policy=0.05); `SemanticRoutingEnabled` must be `true`
- [x] `EmbeddingSkillRouter` — refactor to use `ISkillScoringEngine`; expose `ScoreAsync(input)` returning ranked list; `RouteAsync` returns top result with method breakdown
- [x] `Program.cs` (CLI + WebAPI) — register all scorers and `ISkillScoringEngine` in DI; register `EmbeddingSkillRouter` with DI
- [x] `AgentCore.cs` — inject `EmbeddingSkillRouter`; use it when `Phase2Config.SemanticRoutingEnabled == true`, otherwise use legacy `SkillRouter`
- [x] `SkillRouterTests.cs` — add tests for legacy routing when semantic disabled
- [x] `SkillScoringEngineTests.cs` — unit tests: each scorer, combined scoring, ineligible skill handling, tie-break
- [x] `EmbeddingSkillRouterTests.cs` — expand: scoring engine integration, component scores returned
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит

## Scope / Likely files
src/agent/Skills/Routing/SkillRouter.cs, src/agent/Skills/Routing/Scoring/

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Skills/Routing/`** — новая папка:
- `ISkillScorer.cs` — интерфейс: `ScoreAsync(input, skill)` → `ComponentScore?`; `ComponentScore` record: ComponentName, Value [0..1], IsEligible, Details
- `SkillScoreResult.cs` — результат scoring engine: Skill, OverallScore, IsEligible, ComponentScores dict, PrimaryMethod, IneligibilityReason
- `ScoringComponents/EmbeddingScorer.cs` — cosine similarity via `IEmbeddingProvider`; normalized score maps threshold→0, 1.0→1.0; caches skill embeddings
- `ScoringComponents/LexicalScorer.cs` — phrase-receiver exact match; score = matched/total
- `ScoringComponents/SchemaCompatibilityScorer.cs` — checks declared tools: required but unregistered → IsEligible=false; configurable weight
- `ScoringComponents/HistoricalQualityScorer.cs` — SuccessRate × LastEvaluationScore blend (60/40)
- `ScoringComponents/LatencyScorer.cs` — sigmoid-like mapping: score = 1/(1+(latency/refLatency)²); neutral 1.0 when no history
- `ScoringComponents/PolicyEligibilityScorer.cs` — checks permissions vs allowed set, denied tools vs DeniedTools list; ineligible → score=0, IsEligible=false
- `SkillScoringEngine.cs` — `ISkillScoringEngine`: runs all scorers in parallel, combines weighted scores, ranks eligible skills

**`Config/AppConfig.cs`** — `Phase2Config`:
- Добавлен `SkillScoringWeights: Dictionary<string, double>` с весами: embedding=0.40, lexical=0.20, schema=0.15, quality=0.15, latency=0.05, policy=0.05

**`Skills/EmbeddingSkillRouter.cs`** — refactored:
- Конструктор теперь принимает `(SkillManager, Phase2Config, SkillRouter, ISkillScoringEngine?)`
- `SimilarityThreshold` и `UseKeywordFallback` — из `Phase2Config` (read-only)
- `RouteAsync` использует `SkillScoringEngine` когда `SemanticRoutingEnabled=true`, иначе legacy keyword routing
- `ScoreAllAsync` — возвращает ranked list через engine или legacy single result

**`AgentCore.cs`** — интеграция:
- Добавлен `EmbeddingSkillRouter? _embeddingRouter` и `Phase2Config? _phase2Config` в конструктор
- `Reload()` обновляет `_phase2Config`
- `HandleAsyncCore`: когда `_embeddingRouter` доступен и `SemanticRoutingEnabled=true` → async routing; иначе legacy sync routing
- `RecordUsage` теперь включает `LatencyMs` (int handleSw.ElapsedMilliseconds)

**`Storage/Models.cs`** — `SkillUsage`:
- Добавлено поле `LatencyMs` (int, default=0) — backward compatible

**`Agent/SkillManager.cs`** — новые методы:
- `RecordUsage(id, success, confidence, latencyMs)` overload
- `GetAverageLatency(id)` — средняя latency из usage history

**DI (CLI + WebAPI Program.cs)**:
- Все scorers зарегистрированы как singletons
- `ISkillScoringEngine` → `SkillScoringEngine`
- `EmbeddingSkillRouter` обновлён с передачей engine

**Tests** — 29 routing-related tests:
- EmbeddingSkillRouterTests: 9 тестов (existing ones fixed + new routing integration tests)
- SkillScoringEngineTests: 8 тестов (all scorers + engine integration)

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 690/696 passed (6 pre-existing failures: OtelService + BudgetGuard + WASM)

## Risks / Rollback
Зависимость от embedding-провайдера; нужен deterministic fallback (см. задачу 23).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
