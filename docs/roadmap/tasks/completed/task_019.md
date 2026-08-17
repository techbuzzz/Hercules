# Task 19 — Формат пакета навыка

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `skill-package-format`

## Goal
Папка навыка: skill.meta.json, skill.prompt.md, skill.tests.json, опционально tool.schema.json, examples.json и changelog.

## Acceptance criteria

### Sub-tasks

- [x] `docs/skill-package-spec.md` — формальная спецификация формата пакета навыка: файлы, схемы JSON, semver, backward compatibility
- [x] `SkillPackager.cs` — экспорт/импорт папки навыка: `skill.meta.json`, `skill.prompt.md`, `skill.tests.json`, `skill.examples.json`, `skill.changelog.md`, `tool.schema.json` (опционально)
- [x] `SkillPackageManifest.cs` — обновить формат: `PackageFormat` (folder/zip), добавить Examples и Changelog
- [x] `SkillPackagerTests.cs` — тесты: folder export/import round-trip, examples preservation, changelog, ZIP backward compatibility
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (617/624; 7 pre-existing: WASM sandbox + OTel activity source + emoji locale)

## Scope / Likely files
src/agent/Skills/Package/, docs/skill-package-spec.md

## Dependencies
- блокирует / опирается на: [task_002 — skill-lifecycle](task_002.md)

## Implementation notes

### 2026-08-12

**`docs/skill-package-spec.md`** — новая спецификация формата пакетов навыков:
- Folder-структура: `skill.{id}/skill.meta.json`, `skill.prompt.md`, `skill.description.md`, `skill.tests.json`, `skill.examples.json`, `skill.changelog.md`, `tool.schema.json`, `skill.usage.json`
- ZIP (.skillpkg): folder + `skill.package.json` манифест
- Схемы JSON, backward compatibility, security notes, versioning

**`src/agent/Skills/SkillPackageManifest.cs`** — обновлённый формат манифеста:
- `PackageFormat` ("folder" | "zip")
- `PackageSpecVersion` ("1.0.0")
- `SkillExamples` / `SkillExample` классы
- `Tests` Nullable removal (was unused in manifest)

**`src/agent/Skills/SkillPackager.cs`** — ключевые изменения:
- `ExportToFolder(skillId, outputDir, examples, changelog, includeUsage)` — новый метод для folder-экспорта
- `Export()` — теперь создаёт `skill.{id}/` папку внутри ZIP + `skill.package.json` манифест
- `ReadFromFolder(dir)` — чтение из folder-структуры: skill.meta.json + skill.prompt.md + skill.description.md
- `ReadFromZip(zipPath)` — поддержка двух форматов: новый (folder inside ZIP) и legacy (flat files)
- `Validate()` — унифицирован для folder и ZIP
- Backward compatibility: legacy ZIP (flat files) всё ещё читается

**`tests/.../SkillPackagerTests.cs`** — 13 новых тестов:
- ExportToFolder_Creates_SkillFolder, AllSpecFiles, OmitsOptionalFiles, IncludesExamples, IncludesChangelog, IncludesToolSchema
- Import_FromFolder_Restores_Skill
- Validate_Folder_WithMissingMeta, WithMissingPrompt, Valid_ReturnsNoErrors
- Export_zip_Includes_SkillFolder_Structure, Manifest_Includes_PackageFormat_AndSpecVersion

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors (NU1902 pre-existing)
- `dotnet test SkillPackager` — 22/22 passed
- `dotnet test (full)` — 617/624 passed (7 pre-existing failures)

## Risks / Rollback
Обратная совместимость при изменении формата; semver + миграции.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
