# Stage 3 — Skill Authoring + Push

**Epic:** Hercules Studio
**Status:** pending
**Estimate:** 1-2 недели
**Dependencies (backend):** нет
**Dependencies (Studio):** Stage 2

## Goal

Создание навыков в Studio и push на агента: POST/PUT/import, cross-agent install, skill templates (3-5 базовых + file-based .NET examples), pre-check через manifest validate + DangerousCodeScanner.

## Tasks

### 3.1 — Push skill to agent

- [ ] `components/skills/PushSkillDialog.vue`:
  - Select target agent(s) from connections (multi-select for cross-agent)
  - Push modes:
    - **New skill** → `POST /api/skills` (contribute key)
    - **Update existing** → `PUT /api/skills/{id}` (contribute key)
    - **Import package** → `POST /api/skills/import` (multipart .skillpkg)
  - Conflict resolution: replace | skip | rename (for import)
  - Pre-check: `POST /api/skills/{id}/manifest/validate` (if endpoint exists)
  - Pre-check: DangerousCodeScanner warnings (if C# files present) → show warnings → confirm
  - Progress indicator per target agent
  - Results: success/fail per agent, toast

### 3.2 — Cross-agent install

- [ ] Multi-select target agents in PushSkillDialog
- [ ] `Promise.all(targets.map(agent => pushTo(agent)))` — parallel push
- [ ] Per-agent result: success, error message
- [ ] Summary: "Pushed to 3/4 agents. 1 failed: {error}"

### 3.3 — Skill package builder

- [ ] `components/skills/SkillPackager.vue`:
  - Build .skillpkg from current editor content
  - Structure: skill.meta.json + skill.prompt.md + skill.description.md + optional .cs files
  - Download .skillpkg locally
  - Upload to agent via `POST /api/marketplace/publish` (multipart)
  - Or use `POST /api/skills/import` directly

### 3.4 — Skill templates

- [ ] `renderer/src/assets/skill-templates/` — 5 templates:
  1. **HTTP call** — skill with HTTP tool declaration + prompt for API calls
  2. **Code execution** — skill with C# file-based app template + prompt for code tasks
  3. **A2A delegate** — skill for delegating to another agent
  4. **Custom** — empty skill with basic structure
  5. **File-based .NET example** — skill with .cs file using SkillSdk (IHttpClient, IMemoryClient)
- [ ] `components/skills/SkillTemplateGallery.vue`:
  - Grid of template cards
  - Click → create new skill from template → SkillEditor with pre-filled content
  - Preview: show template prompt, meta, C# code

### 3.5 — Pre-check validation

- [ ] Before push:
  - `POST /api/skills/{id}/manifest/validate` (if skill has manifest)
  - If C# files present:
    - Call backend DangerousCodeScanner (via `POST /api/...execute...` scan-only mode, or dedicated endpoint)
    - Show warnings: "Found dangerous pattern: File.Delete at line 42"
    - Block push if critical violations (user can override with confirmation)
- [ ] `components/skills/ValidationReport.vue`:
  - List of warnings/errors with line numbers
  - Severity badges: critical (block), warning (confirm), info
  - "Push anyway" button for warnings

### 3.6 — Draft management

- [ ] `stores/skills.ts` (расширить):
  - `drafts: Map<skillId, SkillDraft>` — all local drafts
  - `hasUnpushedChanges(skillId): boolean` — compare draft vs agent version
  - `listDrafts()` — all drafts for current connection
  - `deleteDraft(skillId)`
- [ ] Sidebar indicator: dot on skill card if has unpushed local changes
- [ ] "Publish changes" button in SkillEditor when local != agent

### 3.7 — File-based .NET skills (basic editing)

- [ ] In SkillEditor, "C# files" tab:
  - List of .cs files in skill (add/remove/rename)
  - Monaco with csharp language (basic syntax, no IntelliSense)
  - Template C# file structure:
    ```csharp
    // Hercules SkillSdk template
    using Hercules.SkillSdk;
    
    public class MySkill
    {
        private readonly IHttpClient _http;
        private readonly IMemoryClient _memory;
        
        public MySkill(IHttpClient http, IMemoryClient memory)
        {
            _http = http;
            _memory = memory;
        }
        
        public async Task<string> ExecuteAsync(ISessionContext ctx, string input)
        {
            // TODO: implement skill logic
            return $"Processed: {input}";
        }
    }
    ```
  - Note: "Full C# test-run available in Stage 6 (sandbox integration)"

### 3.8 — Tests

- [ ] Unit: PushSkillDialog logic, cross-agent push (mock), template gallery
- [ ] E2E: create skill from template → edit → push to agent → see in agent's skill list

## Acceptance criteria

- [ ] PushSkillDialog: select agent(s), choose mode (new/update/import), push
- [ ] Cross-agent: push to multiple agents in parallel, per-agent results
- [ ] SkillPackager: build .skillpkg, download, or upload to agent
- [ ] Template gallery: 5 templates, create from template → pre-filled editor
- [ ] Pre-check: manifest validate + DangerousCodeScanner warnings shown
- [ ] Validation report: severity badges, block critical, "push anyway" for warnings
- [ ] Drafts: local changes indicator, "Publish changes" button
- [ ] C# files: add/remove/rename, Monaco csharp syntax
- [ ] `npm run build` + tests pass

## Scope / Likely files

- `renderer/src/components/skills/PushSkillDialog.vue`, `SkillPackager.vue`, `SkillTemplateGallery.vue`, `ValidationReport.vue`
- `renderer/src/assets/skill-templates/`

## Dependencies

- **Backend:** нет (текущий API: POST/PUT/skills/import, manifest/validate, DangerousCodeScanner)
- **Studio:** Stage 2

## Risks / Rollback

- **DangerousCodeScanner endpoint:** может не существовать standalone endpoint для scan-only. Mitigation: `POST /api/skills/{id}/manifest/validate` может включать scan, или добавить endpoint в backend.
- **.skillpkg format:** зависит от бэкенд SkillPackager. Mitigation: использовать `POST /api/skills/import` (multipart) напрямую без локальной сборки.

## Links

- Epic: [../README.md](../README.md)
- Stage 2: [stage_02_chat_skills.md](stage_02_chat_skills.md)
- Stage 6: [stage_06_config_restart.md](stage_06_config_restart.md) (full C# sandbox)