# Task 16 — Оценка навыков (eval harness)

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `eval-harness`

## Goal
Каждый навык имеет deterministic fixtures и опциональные LLM-judge кейсы. Baseline записывается до promotion, регрессии блокируют автоматический rollout.

## Acceptance criteria

### Sub-tasks

- [x] `Storage/Models.cs` — добавить `SkillTestSuite` и `SkillTestCase` record'ы с полями: `SkillTestCase` (Name, Input, ExpectedContains, MinConfidence, ExpectedMode, JudgedBy="deterministic"|"llm"|null, JudgePrompt)
- [x] `Skills/Eval/IEvalHarnessService.cs` — интерфейс: `RunHarnessAsync(skillId)`, `RecordBaselineAsync(skillId, testSuiteId)`, `CompareWithBaseline(skillId, newResult)`, `GetBaseline(skillId)`, `GetHarnessHistory(skillId)`
- [x] `Skills/Eval/EvalHarnessService.cs` — реализация EvalHarnessService: загружает SkillTestSuite для навыка, запускает через SkillEvaluationEngine, сравнивает с baseline, возвращает RegressionResult
- [x] `Skills/Eval/SkillTestGenerator.cs` — генератор тестов: `GenerateDeterministicFixtures(skill, count)` (seeded, reproducibility), `GenerateLlmJudgeCases(skill, count)` (optional LLM-judge cases с temperature=0, seed)
- [x] `Skills/Eval/BaselineManager.cs` — управление baseline: `SaveBaseline(skillId, suiteId, result)`, `LoadBaseline(skillId)`, `HasBaseline(skillId)`; baselines хранятся в `skills/.baselines/{skillId}.json`
- [x] `Skills/Eval/RegressionResult.cs` — результат сравнения: `HasRegression`, `ScoreDelta`, `PreviousScore`, `CurrentScore`, `BlockedReasons` (список regression-тестов)
- [x] `Skills/Eval/SkillHarnessController.cs` — WebAPI endpoints: `POST /api/skills/{id}/eval/harness` (run harness, return RegressionResult), `POST /api/skills/{id}/eval/baseline` (record baseline), `GET /api/skills/{id}/eval/baseline` (get baseline), `GET /api/skills/{id}/eval/history` (история)
- [x] `SkillLifecycleService` — интеграция: `EvalHarnessAsync` вызывается после skill promotion; если `RegressionResult.HasRegression == true` и `config.BlockOnRegression == true` — откатывает изменения и кидает `RegressionBlockedException`
- [x] `Config/AppConfig.cs` — добавить `EvalConfig`: `BlockOnRegression` (default true), `BaselineComparisonThreshold` (default 0.05 — max допустимое падение score), `DefaultFixtureCount` (default 5)
- [x] `CLI/Commands/SkillEvalCommand.cs` — CLI команда `/skill eval {id}`: запуск harness, вывод baseline comparison, regression status
- [x] `tests/Hercules.Agent.Tests/Skills/Eval/EvalHarnessServiceTests.cs` — unit-тесты: harness run, baseline record/compare, regression detection, no-baseline baseline creation
- [x] `tests/Hercules.Agent.Tests/Skills/Eval/BaselineManagerTests.cs` — unit-тесты: save/load/has baseline, baseline file format, missing baseline
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (562 total: 556 pass, 6 pre-existing failures: OTel/Wasm/Budget emoji)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Skills/Eval/` folder** — 5 новых файлов:
- `IEvalHarnessService.cs` — интерфейс: RunHarnessAsync, RecordBaselineAsync, CompareWithBaseline, GetBaseline, GetBaselineHistory, HasBaseline
- `EvalHarnessService.cs` — реализация: загружает test suite, генерирует fixtures если нет suite, запускает через SkillEvaluationEngine, сравнивает с baseline
- `BaselineManager.cs` — управление baseline в `skills/.baselines/{skillId}.json`; Save/Load/Has/Delete/GetAllBaselines
- `RegressionResult.cs` + `RegressionDetail.cs` — HasRegression, ScoreDelta, PreviousScore, CurrentScore, BaselineId, BlockedReasons
- `BaselineRecord.cs` + `TestCaseResult.cs` — persisted baseline snapshot
- `SkillTestGenerator.cs` — GenerateDeterministicFixtures (seeded RNG по skillId hash, воспроизводимо), GenerateLlmJudgeCases (structured judge prompt)
- `SkillPackageManifest.cs` — добавлены JudgedBy + JudgePrompt в SkillTestCase, Description в SkillTestSuite

**`Config/AppConfig.cs`** — `EvalConfig`:
- BlockOnRegression (default true), BaselineComparisonThreshold (0.05), DefaultFixtureCount (5), EnableLlmJudgeCases (false)

**`Skills/SkillLifecycleService.cs`** — добавлен:
- EvalHarnessAsync: запуск harness + regression check, rollback + RegressionBlockedException при блокировке
- RegressionBlockedException: SkillId + RegressionResult

**`Hercules.WebApi/Controllers/SkillHarnessController.cs`** — 5 endpoints:
- POST /api/skills/{id}/eval/harness
- POST /api/skills/{id}/eval/baseline
- GET /api/skills/{id}/eval/baseline
- GET /api/skills/{id}/eval/history
- GET /api/eval/baselines

**`CLI/ConsoleUI.cs`** — добавлен `/skill eval {id}`:
- Использует AnsiConsole.Status для отображения progress
- Показывает baseline info + regression status
- Цветовая индикация: зелёный ✓ без регрессии, красный ⚠ регрессия

**DI (CLI + WebAPI):**
- EvalConfig singleton, BaselineManager, SkillTestGenerator, IEvalHarnessService → EvalHarnessService

**Tests** — 17 новых тестов (8 BaselineManagerTests + 9 EvalHarnessServiceTests):
- BaselineManagerTests: save, load, null, has, delete, overwrite, get all
- EvalHarnessServiceTests: no-baseline creates baseline, no regression, regression detected, small drop below threshold, has baseline, get history filtered

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 556/562 passed (6 pre-existing: OTel activity source + WASM timing + Budget emoji)

## Scope / Likely files
src/agent/Skills/Eval/, src/agent/CLI/Commands/SkillEvalCommand.cs

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)
- блокирует / опирается на: [task_007 — typed-contracts](task_007.md)

## Risks / Rollback
Flaky LLM-judge; нужны reproducibility-инварианты (temperature=0, seed).

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
