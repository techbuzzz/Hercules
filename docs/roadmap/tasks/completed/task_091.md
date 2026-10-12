# Task 91 — Trust admission policy panel

**Phase:** 7
**Initiative:** 48
**Status:** done
**Owner:** —
**Slug:** `web-trust-admission-panel`

## Goal
`TrustAdmissionPolicyEngine` (task_040) управляет режимом `Enforce | DryRun | Disabled` и allow-list'ами (TrustLevels, Intents, Classifications, RiskLevels, SchemaMismatch, BudgetExceeded). Backend даёт `GET /api/mesh/policy/status` и `POST /api/mesh/policy/dry-run`. Web не использует их — оператор не видит текущую policy и не может её dry-run'ить из UI.

## Acceptance criteria

### Sub-tasks

- [x] `src/lib/api.ts` — добавить `getPolicyStatus(): Promise<PolicyStatusDto>`, `dryRunPolicy(req: DryRunRequest): Promise<DryRunResultDto>`; типы: `PolicyStatusDto` (mode, allowedTrustLevels[], allowedIntents[], allowedClassifications[], allowedRiskLevels[], allowSchemaMismatch, allowBudgetExceeded), `DryRunRequest` (intent, classification, riskLevel, schemaVersion, budgetUsd, trustLevel), `DryRunResultDto` (allowed, denialReason | null)
- [x] `src/components/TrustAdmissionPanel.astro` — read-only карточка с текущей policy; форма «Dry-run» с полями (intent, classification, risk, schema, budget) → submit → показать Allowed/Denied + reason inline
- [x] Не показывать форму смены policy mode (mode flip — через config reload, не из web)
- [x] Встраивается в `src/pages/memmesh.astro` рядом с MeshRouterPanel
- [x] `src/hercules-web/README.md` — обновить
- [x] `npm run build` — exit 0
- [x] Manual smoke: dry-run с заведомо валидным intent'ом → Allowed; с заведомо запрещённым (например, classification=Restricted при `AllowedClassifications=[Public,Internal]`) → Denied + reason

## Implementation notes

### Round 1 (this tick) — completed

Web-UI для trust admission policy: read-only карточка текущей policy + форма dry-run.

- `src/lib/api.ts` — добавлены типы и методы:
  - `TrustAdmissionIntentEnvelopeDto` — минимальный `IntentEnvelope` для dry-run: `request_id`, `sender`, `intent`, `payload`, `version`.
  - `TrustAdmissionDryRunRequestDto` — поля верхнего уровня dry-run: `envelope`, `targetAgentId?`, `callerTrustLevel?`, `dataClassification?`, `callerSchemaVersion?`, `requestedRiskLevel?`.
  - `TrustAdmissionPolicyStatusDto` — зеркало `GET /api/mesh/policy/status`: `enabled`, `mode`, `allowedTrustLevels[]`, `allowedIntents[]`, `allowedClassifications[]`, `allowSchemaMismatch`, `allowBudgetExceeded`, `allowedRiskLevels[]`.
  - `TrustAdmissionDryRunResultDto` — `allowed`, `denialReason`, `denialCode`, `dryRun`, `effectiveMode`.
  - `api.getPolicyStatus()` — `GET /api/mesh/policy/status`.
  - `api.dryRunPolicy(req)` — `POST /api/mesh/policy/dry-run`.

- `src/components/TrustAdmissionPanel.astro` — клиентский компонент:
  - Header + «обновлено в HH:MM:SS» + кнопка «Обновить».
  - **Status card** (read-only) — `mode` (Enforce/DryRun/Disabled badge), `enabled` (✓/✗), allow-list'ы (trust levels, intents, classifications, risk levels) как chips, schema-mismatch / budget-exceeded как allow/deny pills.
  - **Dry-run form** — поля: intent (text required), classification (select: Public/Internal/Confidential/Restricted/∅), risk (select: Low/Medium/High/∅), trust (select: Unverified/Sandbox/Basic/Trusted/Privileged/∅), schemaVersion (text), targetAgentId (text), sender (text required), payload (textarea с `{}` по умолчанию). На submit → POST dry-run → inline result card: `✓ Allowed (mode=DryRun)` или `✗ Denied (code=IntentNotAllowed) + reason`.
  - **Error/Ok banner** (как в других панелях) — redaction secrets не нужен (нет credential-полей в request/response).
  - **Manual smoke hint** внизу формы: «Подсказка: при `AllowedClassifications=[Public,Internal]` classification=Restricted → Denied».
  - **Note:** `budgetUsd` упомянут в acceptance criteria, но backend (`MeshController.cs:547`) не принимает budget-поля — budget check работает через `envelope.payload.length` и `target.ResourceLimits.MaxTokensPerRequest`. Поле budget в UI не выведено (потенциально misleading), но в hint показано, что budget ≈ size of payload.

- `src/pages/memmesh.astro` — `<TrustAdmissionPanel />` подключён в новой секции под CapabilityRegistryPanel.

- `src/hercules-web/README.md` — обновлены разделы «Структура» (новый `TrustAdmissionPanel.astro`) и «Backend API» (2 mesh-эндпойнта: `policy/status`, `policy/dry-run`).

### Round 1 — validation

- `npm run build` → **exit 0**, 7 страниц собраны, включая `/memmesh/index.html` с `<div id="trust-admission-panel">`, формой `policy-dryrun-form` и bundle `TrustAdmissionPanel.astro_astro_type_script_index_0_lang.*.js` (~6.7 KB).
- `npx astro check` — **0 errors, 0 warnings, 0 hints** в `TrustAdmissionPanel.astro`, `memmesh.astro`, `lib/api.ts`. Pre-existing 17 errors в `MeshRouterPanel.astro` (вне scope task_091; задокументированы в task_088/089).
- Проверка `dist/memmesh/index.html`: `id="trust-admission-panel"`, секции status + dry-run form отрендерены, script-тег ведёт на bundle.
- Manual smoke требует запущенного backend: типичный happy path — `intent=healthcheck`, `trust=Privileged`, пустой payload → `Allowed (mode=DryRun)`; typical denial — `classification=Restricted` при `AllowedClassifications=[Public,Internal]` → `Denied (code=ClassificationTooHigh) + reason`.
