# Task 2 — Жизненный цикл навыков

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `skill-lifecycle`

## Goal
Автоматизировать создание, версионирование, тестирование, оценку, улучшение, депрекацию и откат навыков; high-risk изменения остаются human-gated.

## Acceptance criteria

- [x] `SkillEvaluationEngine.cs` — класс в `src/agent/Skills/`:
  - `EvaluateAsync(Skill, SkillTestSuite, CancellationToken)` — выполняет каждый SkillTestCase через AgentCore (прокидывает input, проверяет ExpectedContains, MinConfidence, ExpectedMode);
  - возвращает `SkillEvaluationResult` (score 0..1, pass/fail, per-test results);
  - таймаут на тест = 60s, общее время оценки = 120s
- [x] `SkillDeprecationManager.cs` — класс в `src/agent/Skills/`:
  - `Deprecate(id, reason)` — помечает навык deprecated в meta.json (`DeprecatedAt`, `DeprecationReason`);
  - `Rollback(id)` — восстанавливает предыдущую версию (.v{N-1}.md) и обновляет meta.json
  - `GetDeprecated()` — возвращает список deprecated навыков
  - `Undeprecate(id)` — снимает deprecated-статус
  - `SkillMeta` получает поля `DeprecatedAt`, `DeprecationReason`, `LastEvaluationScore`
- [x] `SkillLifecyclePolicy.cs` — policy-класс в `src/agent/Skills/`:
  - `RequiresApproval(Skill, SkillAction)` — возвращает PolicyCheckResult с NeedsHumanApproval для HIGH_RISK действий;
  - HIGH_RISK = Improve (SuccessRate ≤ 0.4), Rollback, Deprecate, Delete, any action on deprecated skill;
  - MEDIUM_RISK = Improve (SuccessRate 0.4–0.6), CreateWithAi; LOW_RISK = всё остальное
  - `Action` enum: Create, CreateWithAi, Improve, Update, Deprecate, Rollback, Delete, Evaluate
- [x] `SkillManager` — интеграция evaluation:
  - `EvaluateAsync(id, SkillEvaluationEngine, CancellationToken)` — запускает оценку и обновляет `Meta.LastEvaluationScore`
  - `Delete(id)` — удаляет навык с backup
- [x] `SkillLifecycleService.cs` — DI-friendly сервис, объединяющий SkillManager + SkillDeprecationManager + SkillLifecyclePolicy + SkillEvaluationEngine;
  - `Deprecate`, `Rollback`, `Undeprecate`, `EvaluateAsync`, `GetDeprecated`, `Delete`
  - `ApprovalRequiredException` при HIGH_RISK без подтверждения
- [x] `SkillLifecycleController.cs` — WebAPI endpoints:
  - `POST /api/skills/{id}/evaluate` — запустить оценку (SkillEvaluationResult)
  - `POST /api/skills/{id}/deprecate` — deprecate (body: reason), 202 if approval required
  - `POST /api/skills/{id}/rollback` — rollback, 202 if approval required
  - `POST /api/skills/{id}/undeprecate` — снять deprecated
  - `GET /api/skills/deprecated` — список deprecated
  - `POST /api/skills/{id}/improve` — improve; 202 если нужен approval
- [x] `AgentCore.EvaluateSkillAsync(Skill, input, ct)` — evaluate skill bypassing router (для engine)
- [x] Unit-тесты (17 новых тестов):
  - `SkillLifecyclePolicyTests.cs` — 10 тестов: базовые risk levels, Improve boundary, deprecated skill rules
  - `SkillDeprecationManagerTests.cs` — 8 тестов: deprecate, rollback, get deprecated, undeprecate
  - `SkillEvaluationEngineTests.cs` — 7 тестов: evaluate с mock AgentCore, score calculation, timeout
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (193/193)

## Scope / Likely files
src/agent/Skills/, src/agent/Agent/SkillManager.cs, src/agent/Agent/AgentCore.cs, src/agent/Storage/Models.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Risks / Rollback
Само-модификация навыков без approval; нужен явный policy gate.

## Implementation notes

### 2026-08-12

**Добавлено:**
- `SkillLifecyclePolicy.cs` — enum SkillAction/SkillActionRisk, PolicyCheckResult, RequiresApproval с динамическим risk для Improve
- `SkillDeprecationManager.cs` — Deprecate/Rollback/Undeprecate/GetDeprecated; использует FileSkillRepository
- `SkillEvaluationEngine.cs` — EvaluateAsync через AgentCore.EvaluateSkillAsync; per-test timeout 60s, total 120s; проверяет ExpectedContains, MinConfidence, ExpectedMode
- `SkillLifecycleService.cs` — фасад с ApprovalRequiredException для HIGH_RISK
- `SkillLifecycleController.cs` — 6 эндпоинтов; 202 Accepted с approvalRequired при HIGH/MEDIUM risk
- `SkillManager.EvaluateAsync` — обновляет Meta.LastEvaluationScore после оценки
- `AgentCore.EvaluateSkillAsync` — bypass-метод для принудительного использования навыка
- `SkillMeta` — + DeprecatedAt, DeprecationReason, LastEvaluationScore
- `SkillDto` — расширен новыми полями
- 17 unit-тестов (SkillLifecyclePolicyTests, SkillDeprecationManagerTests, SkillEvaluationEngineTests)

**Validation:**
- `dotnet build` — 0 errors, 0 warnings
- `dotnet test` — 193/193 passed

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
