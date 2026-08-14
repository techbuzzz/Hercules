# Task 91 — Trust admission policy panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-trust-admission-panel`

## Goal
`TrustAdmissionPolicyEngine` (task_040) управляет режимом `Enforce | DryRun | Disabled` и allow-list'ами (TrustLevels, Intents, Classifications, RiskLevels, SchemaMismatch, BudgetExceeded). Backend даёт `GET /api/mesh/policy/status` и `POST /api/mesh/policy/dry-run`. Web не использует их — оператор не видит текущую policy и не может её dry-run'ить из UI.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `getPolicyStatus(): Promise<PolicyStatusDto>`, `dryRunPolicy(req: DryRunRequest): Promise<DryRunResultDto>`; типы: `PolicyStatusDto` (mode, allowedTrustLevels[], allowedIntents[], allowedClassifications[], allowedRiskLevels[], allowSchemaMismatch, allowBudgetExceeded), `DryRunRequest` (intent, classification, riskLevel, schemaVersion, budgetUsd, trustLevel), `DryRunResultDto` (allowed, denialReason | null)
- [ ] `src/components/TrustAdmissionPanel.astro` — read-only карточка с текущей policy; форма «Dry-run» с полями (intent, classification, risk, schema, budget) → submit → показать Allowed/Denied + reason inline
- [ ] Не показывать форму смены policy mode (mode flip — через config reload, не из web)
- [ ] Встраивается в `src/pages/memmesh.astro` рядом с MeshRouterPanel
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: dry-run с заведомо валидным intent'ом → Allowed; с заведомо запрещённым (например, classification=Restricted при `AllowedClassifications=[Public,Internal]`) → Denied + reason
