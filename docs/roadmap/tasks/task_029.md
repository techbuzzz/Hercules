# Task 29 — Score качества навыка

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `skill-quality-score`

## Goal
Per-version метрики: acceptance rate, test score, user correction rate, fallback rate, latency, cost, safety denials. Promotion и routing используют score без монополии.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `SkillQualityConfig` секцию: `Enabled` (default true), `ScoreWeights` (Dictionary: acceptanceRate=0.25, testScore=0.30, userCorrectionRate=0.20, fallbackRate=0.15, latency=0.05, cost=0.05), `MinSampleSize` (default 10 — мин. вызовов для достоверного score), `ScoreDecayPerVersion` (default 0.95), `MinScoreForPromotion` (default 0.40)
- [x] `Skills/Quality/Models.cs` — `SkillQualityMetrics` class, `SkillQualityScore` record, `SkillQualityEvent` enum
- [x] `Skills/Quality/SkillQualityStore.cs` — SQLite-backed: таблица `skill_quality_metrics` + методы SaveMetricsAsync, LoadMetricsAsync, GetHistoryAsync, IncrementUsageAsync
- [x] `Skills/Quality/SkillQualityService.cs` — `ISkillQualityService`: RecordFallbackAsync, RecordUserCorrectionAsync, RecordSafetyDenialAsync, RecordLatencySampleAsync, RecordSuccessAsync, RecordTestScoreAsync, ComputeScoreAsync, GetMetricsAsync, GetHistoryAsync, UpdateSkillMetaAsync
- [x] `Skills/Quality/SkillQualityScorer.cs` — `ISkillScorer` implementation: reads from ISkillQualityService, returns ComponentScore("quality") with composite score
- [x] `SkillScoringEngine` — добавить `SkillQualityScorer` в список scorers; привязать к Phase2Config.SkillScoringWeights["quality"]
- [x] `SkillLifecycleService` — EvalHarnessAsync: проверяет quality score против MinScoreForPromotion перед promotion
- [x] `SkillHarnessController` — при запуске eval: RecordTestScoreAsync обновляет TestScore в SkillQualityStore
- [x] `WebApi/Controllers/SkillQualityController.cs` — endpoints: GET /quality, GET /quality/history, POST /quality/record, GET /quality/score
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать SkillQualityConfig, SkillQualityStore, ISkillQualityService, SkillQualityScorer в DI
- [x] Unit-тесты: `SkillQualityServiceTests.cs` — 13 тестов: record events, compute score with weights, MinSampleSize gating, GetHistory
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (837/845; 8 pre-existing failures in OtelService, BudgetGuard, WasmTool, AgentCoreBoundedExecution)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Config/AppConfig.cs`** — `SkillQualityConfig`:
- `Enabled` (default true)
- `ScoreWeights` (Dictionary: acceptanceRate=0.25, testScore=0.30, userCorrectionRate=0.20, fallbackRate=0.15, latency=0.05, cost=0.05)
- `MinSampleSize` (default 10)
- `ScoreDecayPerVersion` (default 0.95)
- `MinScoreForPromotion` (default 0.40)

**`src/agent/Skills/Quality/Models.cs`** — 3 типа:
- `SkillQualityEvent` enum (ToolFallback, UserCorrection, SafetyDenial, LatencySample, CostSample)
- `SkillQualityMetrics` class: AcceptanceRate, TestScore, UserCorrectionRate, FallbackRate, AvgLatencyMs, AvgCostUsd, SafetyDenialCount, TotalCalls, Version, UpdatedAt + internal counters
- `SkillQualityScore` record: SkillId, Version, CompositeScore, IsReliable, Reason, all metric fields

**`src/agent/Skills/Quality/SkillQualityStore.cs`** — SQLite-backed:
- Таблица `skill_quality_metrics` (skill_id, version, + 11 metric columns)
- Upsert via `SaveMetricsAsync`, `LoadMetricsAsync`, `GetHistoryAsync`, `IncrementUsageAsync`
- Graceful no-op when no data

**`src/agent/Skills/Quality/SkillQualityService.cs`** — `ISkillQualityService`:
- RecordFallbackAsync, RecordUserCorrectionAsync, RecordSafetyDenialAsync, RecordLatencySampleAsync, RecordSuccessAsync, RecordTestScoreAsync
- ComputeScoreAsync: weighted composite score (accRate, testScore, userCorr, fallback, latency, cost)
- MinSampleSize gating: < MinSampleSize → IsReliable=false, CompositeScore=1.0 (neutral)
- Always returns current metrics in SkillQualityScore regardless of reliability
- UpdateSkillMetaAsync: syncs CompositeScore → SkillMeta.SuccessRate

**`src/agent/Skills/Quality/SkillQualityScorer.cs`** — `ISkillScorer`:
- ComponentName = "quality"
- Weight from Phase2Config.SkillScoringWeights["quality"] (default 0.15)
- Returns null (skip) on exception for graceful degradation

**`SkillScoringEngine`** — SkillQualityScorer added to scorers list with weight assignment

**`SkillLifecycleService`** — EvalHarnessAsync: quality score check before promotion; blocks if score < MinScoreForPromotion

**`SkillHarnessController`** — RunSkillHarness endpoint records test score via ISkillQualityService.RecordTestScoreAsync

**`Hercules.WebApi/Controllers/SkillQualityController.cs`** — 4 endpoints:
- GET /api/skills/{id}/quality — metrics + score
- GET /api/skills/{id}/quality/history — all versions
- POST /api/skills/{id}/quality/record — record event (fallback/usercorrection/safetydenial/latencysample)
- GET /api/skills/{id}/quality/score — composite score only

**DI (CLI + WebAPI):** SkillQualityConfig singleton, SkillQualityStore singleton, ISkillQualityService → SkillQualityService, SkillQualityScorer singleton

**Tests:** `tests/.../Skills/Quality/SkillQualityServiceTests.cs` — 13 tests:
- RecordFallback_IncrementsCountAndRate, RecordFallback_10Falls_50Percent, RecordUserCorrection_TracksRate, RecordSafetyDenial_IncrementsCount, RecordSuccess_TracksLatencyAndCost, ComputeScore_NoData_ReturnsNeutral, ComputeScore_BelowMinSampleSize_ReturnsNeutral, ComputeScore_AtMinSampleSize_IsReliable, ComputeScore_AllPerfect_ReturnsOne, ComputeScore_AllFailing_ReturnsReflectsFallbacks, RecordTestScore_UpdatesMetrics, GetHistory_MultipleVersions_ReturnsAll, Service_WithNullWeights_DefaultsToZeroWeight

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test` — 837/845 passed (8 pre-existing failures: OtelService × 5, BudgetGuard × 1, WasmTool × 1, AgentCoreBoundedExecution × 1)

## Scope / Likely files
src/agent/Skills/Quality/

## Dependencies
- блокирует / опирается на: [task_014 — audit-privacy](task_014.md)
- блокирует / опирается на: [task_016 — eval-harness](task_016.md)

## Risks / Rollback
Goodhart law; score — не единственный критерий promotion.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
