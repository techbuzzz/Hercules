# Task 21 — Маркетплейс навыков

**Phase:** 2
**Status:** done
**Owner:** —
**Slug:** `skill-marketplace`

## Goal
data/Skills/marketplace/: import/export CLI, integrity hashes, signed packages, разрешение зависимостей, public template repository.

## Acceptance criteria

### Sub-tasks

- [x] `MarketplaceConfig` в `Config/AppConfig.cs` — SigningKey (default null), RequireSignature (default false), AllowHttpImport (default false), SignatureAlgorithm ("hmac-sha256"), MaxPackageSizeMb (default 50)
- [x] `SkillPackageManifest.Dependencies` — List<SkillDependency> (Id, VersionConstraint — semver range string)
- [x] `SkillDependency` record — Id, VersionConstraint (e.g. "≥1.0.0,<2.0.0"), IsRequired (default true)
- [x] `MarketplaceSigningService.cs` — ComputeHash (SHA256 of zip bytes), ComputeSignature (HMAC-SHA256), VerifyHash, VerifySignature; interface `IMarketplaceSigningService`
- [x] `SkillPackager.Export` — compute and write `signature.txt` (HMAC-SHA256) alongside the .skillpkg; include `hash_sha256.txt` (SHA256)
- [x] `SkillPackager.Validate` — verify signature and hash on import; fail with clear error if mismatch
- [x] `DependencyResolver.cs` in `Skills/Marketplace/` — ResolveAsync(skillId, marketplace) → topologically sorted list of skills to install; detects cycles; respects version constraints
- [x] `DependencyResolver` implements graph traversal (Kahn-style topological sort with explicit enter/exit markers; cycle detection via onStack set)
- [x] `SkillMarketplace` — add `InstallWithDeps(skillId, resolver)` → installs skill + all dependencies; `GetDependencies(manifest)` → list declared dependencies
- [x] `MarketplaceController.cs` — WebAPI endpoints: GET /api/marketplace, GET /api/marketplace/search, POST /api/marketplace/publish, POST /api/marketplace/import, POST /api/marketplace/install, POST /api/marketplace/install-with-deps, POST /api/marketplace/import-url, GET /api/marketplace/{file}/verify, GET /api/marketplace/{file}/deps, DELETE /api/marketplace/{file}
- [x] `ConsoleUI` — extend /marketplace: `verify {file}` (check hash+signature), `deps {file}` (show dependency tree), `import-url {url}` (HTTP download + install with deps)
- [x] `Phase2Config` — add MarketplaceConfig sub-section
- [x] `Program.cs` (CLI + WebAPI) — register IMarketplaceSigningService in DI; register MarketplaceConfig
- [x] Unit tests: `MarketplaceSigningServiceTests.cs` — 13 tests: compute hash, verify hash, compute/verify signature, mismatch detection, empty input
- [x] Unit tests: `DependencyResolverTests.cs` — 15 tests: no deps, linear deps, cycle detection, version constraint matching, missing required/optional deps, version mismatch, cancellation, deduplication
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (Phase2 tests: 68/68 passed; full suite: 678/684, 6 pre-existing failures in OtelServiceTests + BudgetGuardTests)

## Scope / Likely files
src/agent/Skills/Marketplace/, src/agent/Skills/SkillPackager.cs, src/agent/CLI/ConsoleUI.cs, src/agent/Hercules.WebApi/Controllers/MarketplaceController.cs

## Dependencies
- блокирует / опирается на: [task_019 — skill-package-format](task_019.md)
- блокирует / опирается на: [task_020 — skill-manifest](task_020.md)

## Risks / Rollback
Supply chain; подпись пакетов обязательна для prod.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Config/AppConfig.cs`** — `MarketplaceConfig` секция:
- `SigningKey` (base64-encoded, default null)
- `RequireSignature` (default false)
- `AllowHttpImport` (default false)
- `SignatureAlgorithm` ("hmac-sha256")
- `MaxPackageSizeMb` (default 50)
- `TemplateRepositoryUrl` (опционально)

**`Skills/SkillPackageManifest.cs`** — `SkillDependency` record + `Dependencies` поле:
- `Id` — ID зависимого навыка
- `VersionConstraint` — semver range ("^1.0.0", "~1.2.3", ">=2.0.0", "1.2.3" — major-only, etc.)
- `IsRequired` (default true) — false = warn but don't block

**`Skills/Marketplace/MarketplaceSigningService.cs`** — `IMarketplaceSigningService` + реализация:
- `ComputeHash(byte[])` — SHA256 hex
- `ComputeSignature(hashHex)` — HMAC-SHA256 base64 (uses `MarketplaceConfig.SigningKey`)
- `VerifyHash(bytes, expectedHashHex)` — case-insensitive compare
- `VerifySignature(hashHex, sigBase64)` — constant-time `CryptographicOperations.FixedTimeEquals`
- Throws `InvalidOperationException` when signing requested but key not configured
- Catches all crypto errors and returns `false` from `VerifySignature` for safe failure mode

**`Skills/SkillPackager.cs`** — integrity sidecars + signing:
- `WriteIntegritySidecars` — после записи ZIP в temp, читает bytes, вычисляет hash, опционально подписывает
- Записывает `hash_sha256.txt` (всегда) и `signature.txt` (если ключ сконфигурирован)
- Атомарный rename из temp в final path
- `Validate` — после read package проверяет hash и signature, добавляет errors если mismatch

**`Skills/Marketplace/DependencyResolver.cs`** — iterative DFS с enter/exit markers:
- Iterative post-order traversal с `Stack<string>` маркированных entries (`enter:{id}` / `exit:{id}`)
- Cycle detection через `onStack` set
- `getManifest` callback: `null` = manifest not found, `[]` = no deps, `[deps]` = declared deps
- `VersionMatchesConstraint` — поддержка `^`, `~`, `>=`, `<=`, `>`, `<`, `=`, exact
- Version encoding: `int = major*10000 + minor*100 + patch`

**`Skills/SkillMarketplace.cs`** — `InstallWithDeps` + `GetDependencies`:
- Сканирует .skillpkg в marketplace/, читает manifest
- `InstallWithDeps` — устанавливает все required deps перед root skill
- `GetDependencies` — возвращает `DependencyInfo` для UI отображения
- `ImportFromUrlAsync` — HTTP download (если `AllowHttpImport = true`), size limit 50MB, validate before import
- `VerifyPackage` — проверяет hash + signature

**`Hercules.WebApi/Controllers/MarketplaceController.cs`** — 10 endpoints:
- `GET /api/marketplace` — list
- `GET /api/marketplace/search?q=` — search
- `POST /api/marketplace/publish` — multipart upload (publish to marketplace)
- `POST /api/marketplace/import` — multipart upload (import to local skills)
- `POST /api/marketplace/install` — install from marketplace (with conflict resolution)
- `POST /api/marketplace/install-with-deps` — install with deps
- `POST /api/marketplace/import-url` — HTTP import
- `GET /api/marketplace/{file}/verify` — hash+signature check
- `GET /api/marketplace/{file}/deps` — dependency tree
- `DELETE /api/marketplace/{file}` — remove from marketplace

**`CLI/ConsoleUI.cs`** — `/marketplace` sub-commands:
- `list` — таблица пакетов
- `search {q}` — поиск
- `install {f}` — установка
- `install-deps {f}` — установка с зависимостями
- `verify {f}` — hash + signature check
- `deps {f}` — показать дерево зависимостей
- `publish {path}` — опубликовать
- `import-url {url}` — HTTP import

**DI (CLI + WebAPI)**:
- `MarketplaceConfig` singleton
- `IMarketplaceSigningService` → `MarketplaceSigningService`

**Tests** — 28 new tests:
- `MarketplaceSigningServiceTests` (13): hash/signature compute+verify, mismatch, case-insensitive, without-key throws
- `DependencyResolverTests` (15): no deps, linear chain, missing required, missing optional, already installed, version mismatch, cancellation, dedup, cycle detection, semver constraints (^, ~, >=, <=, <, exact)

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test --filter "FullyQualifiedName~Phase2"` — 68/68 passed
- `dotnet test` (full) — 678/684 passed (6 pre-existing: OTel activity source + BudgetGuard emoji)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
