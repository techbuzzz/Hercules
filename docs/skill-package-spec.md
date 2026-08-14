# Skill Package Specification

> **Version:** 1.0.0
> **Status:** Active
> **Package Format Version:** `1.0`

Hercules stores each skill as a directory (folder structure) and distributes them as ZIP archives (`.skillpkg`). Both representations are semantically equivalent — a folder can be zipped into a `.skillpkg` and vice versa.

---

## 1. Folder Structure

Each skill lives in its own subdirectory under `data/Skills/` (or any configured `SkillsDir`):

```
skill.{id}/
├── skill.meta.json       # Required: skill metadata (SkillMeta JSON)
├── skill.prompt.md       # Required: system prompt for the LLM
├── skill.description.md  # Required: human-readable description (what the skill does, when to invoke)
├── skill.tests.json      # Optional: SkillTestSuite (see Section 3)
├── skill.examples.json   # Optional: usage examples
├── skill.changelog.md    # Optional: version history in Markdown
├── tool.schema.json      # Optional: ToolDeclaration[] (required/optional tools)
└── skill.usage.json      # Optional: usage log (for analytics, not exported by default)
```

### 1.1 `skill.meta.json`

Represents the `SkillMeta` C# record. Schema:

```jsonc
{
  "id": "string (UUID without dashes, 8 chars, e.g. \"a1b2c3d4\")",
  "name": "string",
  "description": "string",
  "phrase_receivers": ["string", "..."],  // routing triggers
  "tools": [                               // optional
    {
      "name": "string",
      "description": "string",
      "required": true                     // true | false
    }
  ],
  "created_at": "YYYY-MM-DD",
  "version": 1,
  "success_rate": 1.0,                    // 0..1
  "total_uses": 0,
  "deprecated_at": null,                  // null | ISO date string
  "deprecation_reason": null,             // null | string
  "last_evaluation_score": null          // null | 0..1
}
```

> **Backward compatibility:** legacy packages may contain `triggers` instead of `phrase_receivers`. The deserializer migrates `triggers` to `phrase_receivers` automatically.

### 1.2 `skill.prompt.md`

Free-form Markdown containing the system prompt (instructions for the LLM when this skill is selected). No schema constraints.

### 1.3 `skill.description.md`

Human-readable Markdown describing what the skill does, when it should be invoked, and any usage notes. Used in marketplace listings and skill catalogs.

### 1.4 `skill.tests.json`

`SkillTestSuite` JSON (see Section 3). Omit or set to `null` to skip.

### 1.5 `skill.examples.json`

Usage examples for the skill. Schema:

```jsonc
{
  "examples": [
    {
      "input": "string",       // example user input
      "expected_behavior": "string"  // what the skill should do
    }
  ]
}
```

### 1.6 `skill.changelog.md`

Optional Markdown file containing version history. Recommended format:

```markdown
# Changelog

## v2 — 2025-06-01
- Added new routing phrase
- Improved prompt for edge cases

## v1 — 2025-01-15
- Initial release
```

### 1.7 `tool.schema.json`

Optional `ToolDeclaration[]` JSON array describing tools used by this skill. Same schema as in `SkillPackageManifest.ToolDeclaration`.

### 1.8 `skill.usage.json`

Usage log. Not exported by default (contains user-specific analytics). Schema:

```jsonc
[
  {
    "timestamp": "ISO 8601",
    "success": true,
    "confidence": "high"   // high | medium | low
  }
]
```

---

## 2. ZIP Package (`.skillpkg`)

A `.skillpkg` file is a ZIP archive containing the folder structure above.

**Naming:** `{skill-id}-v{version}.skillpkg` (e.g. `a1b2c3d4-v2.skillpkg`)

**Entries:**

```
{skill-id}/
├── skill.meta.json
├── skill.prompt.md
├── skill.description.md
├── skill.tests.json       (if present)
├── skill.examples.json    (if present)
├── skill.changelog.md     (if present)
├── tool.schema.json       (if present)
└── skill.usage.json       (only when `includeUsage: true` on export)
```

The top-level directory name inside the ZIP must match `skill.meta.json / id`.

---

## 3. Test Suite Schema (`skill.tests.json`)

```jsonc
{
  "version": "1.0",
  "description": "string",
  "tests": [
    {
      "name": "string",
      "input": "string",
      "expected_contains": "string",    // optional
      "min_confidence": "high",          // optional: high | medium | low
      "expected_mode": "skill",         // optional: skill | direct | tool
      "judged_by": "deterministic",      // optional: deterministic | llm | null
      "judge_prompt": "string"           // optional, used when judged_by == "llm"
    }
  ]
}
```

---

## 4. Manifest (`skill.package.json`) — ZIP only

When exported as a `.skillpkg`, a `skill.package.json` entry is added at the ZIP root (same level as the skill folder). This manifest is used for package-level metadata and is NOT stored in the folder structure.

```jsonc
{
  "package_version": 1,
  "package_format": "folder",      // "folder" | "zip"
  "package_spec_version": "1.0.0",
  "skill": {
    "id": "string",
    "name": "string",
    "description": "string",
    "phrase_receivers": ["..."],
    "version": 1,
    "created_at": "YYYY-MM-DD"
  },
  "tools": [ /* ToolDeclaration[] */ ],
  "tests": { /* SkillTestSuite */ },
  "source": {
    "author": "string",
    "license": "string",
    "repository": "string"
  },
  "created_at": "ISO 8601"
}
```

> **Note:** `skill.package.json` is a ZIP-only convenience manifest. The authoritative metadata lives in `skill.meta.json`.

---

## 5. Backward Compatibility

| Scenario | Behavior |
|----------|----------|
| Loading a legacy flat-file skill (pre-spec) | FileSkillRepository loads `skill.{id}.meta.json`, `skill.{id}.md`, `skill.{id}.prompt.md` using legacy path methods. Detected by absence of `skill.meta.json` in a `{id}/` subdirectory. |
| `triggers` vs `phrase_receivers` | `SkillMeta.Triggers` setter migrates legacy `triggers` key to `phrase_receivers` on deserialization. |
| Missing optional files | All optional files (`tests`, `examples`, `changelog`, `tool.schema`) are optional. Missing files are treated as absent. |
| ZIP without top-level folder | ZIPs with files directly at root (pre-spec exports) are still readable — the manifest is read from `skill.package.json` and content from individual files. |

---

## 6. Versioning

- **Package format semver:** `1.0.0` (this spec). Changes to `package_spec_version` in the manifest signal breaking changes.
- **Skill version:** separate integer field in `skill.meta.json` — tracks skill content version, not package format version.
- **ZIP package version:** `package_version` in `skill.package.json` tracks the manifest schema version (currently `1`).

---

## 7. Security

- Secrets are never written to exported packages. `SecretMaskingService` redacts `skill.prompt.md` and `skill.description.md` before export when `SecretsConfig.RedactInExports = true`.
- `skill.usage.json` is excluded from exports by default (`includeUsage: false`).
- `skill.changelog.md` may contain internal notes — redact before public distribution.

---

## 8. File Reference

| File | Required | Purpose |
|------|----------|---------|
| `skill.meta.json` | Yes | Skill identity and routing metadata |
| `skill.prompt.md` | Yes | LLM system prompt |
| `skill.description.md` | Yes | Human-readable skill description |
| `skill.tests.json` | No | Evaluation test suite |
| `skill.examples.json` | No | Usage examples for documentation |
| `skill.changelog.md` | No | Version history |
| `tool.schema.json` | No | Tool dependencies |
| `skill.usage.json` | No | Usage analytics (not exported) |
| `skill.package.json` | ZIP only | Package-level manifest |
