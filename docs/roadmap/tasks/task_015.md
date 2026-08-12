# Task 15 — Секреты и конфигурация

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `secrets-config`

## Goal
appsettings + environment variables; секреты никогда не пишутся в skill packages, Markdown memory, telemetry или export archives.

## Acceptance criteria

### Sub-tasks

- [x] `Config/AppConfig.cs` — добавить `SecretsConfig` секцию: `EnvironmentVariablePrefix` (default "HERCULES_SECRET_"), `SecretsFile` (default null), `SecretReferencePrefix` (default "env:"), `RedactInExports` (default true), `RedactInMemory` (default true), `RedactInTelemetry` (default true)
- [x] `Config/ISecretMaskingService.cs` — интерфейс: `MaskSecrets(text)`, `ResolveSecret(reference)`, `IsSecretReference(text)`, `ExpandSecretReferences(text)`
- [x] `Config/SecretMaskingService.cs` — реализация: использует `IRedactionService` для redaction; `ResolveSecret` читает env vars по префиксу HERCULES_SECRET_; `IsSecretReference` проверяет `SecretReferencePrefix`
- [x] `Program.cs` (CLI + WebAPI) — зарегистрировать `SecretsConfig` singleton и `ISecretMaskingService` → `SecretMaskingService` в DI
- [x] `Skills/SkillPackager.cs` — при Export: redacted description + prompt через `ISecretMaskingService` перед записью в ZIP; конструктор с SecretsConfig + ISecretMaskingService
- [x] `Storage/MemoryStore.cs` — при `WriteProfileAsync`, `WritePreferencesAsync`, `WriteEntitiesAsync`, `AppendAsync`, `AppendContextAsync`: redacted content через `ISecretMaskingService`; конструктор с SecretsConfig + ISecretMaskingService
- [x] `Observability/OtelService.cs` — интеграция: `ISecretMaskingService?` nullable; `SetTag` + `SetTags` + `AddEvent` redacts string values если `SecretsConfig.RedactInTelemetry = true`
- [x] `WebApi/Controllers/AuditController.cs` — `/api/audit/export`: redacted details/policy_decision/permission_used через `ISecretMaskingService`
- [x] `tests/.../Config/SecretMaskingServiceTests.cs` — 24 unit-тестов: MaskSecrets (API key, email, phone, bearer, multiple), ResolveSecret (env var, case-insensitive, non-existent), IsSecretReference, ExpandSecretReferences, null redaction service
- [x] `dotnet build` проходит без warnings (NU1902 на OTel.Api — pre-existing)
- [x] `dotnet test` проходит (539/545; 6 pre-existing failures: OTel activity source в test env + WASM sandbox + locale)

## Scope / Likely files
src/agent/Config/, src/agent/HostBuilderExtensions.cs

## Dependencies
- блокирует / опирается на: [task_001 — core-agent-loop](task_001.md)

## Implementation notes

### 2026-08-12

**Добавлено:**

**`Config/AppConfig.cs`** — `SecretsConfig`:
- `EnvironmentVariablePrefix` ("HERCULES_SECRET_") — префикс env vars для секретов
- `SecretReferencePrefix` ("env:") — префикс ссылок на секреты
- `RedactInExports` / `RedactInMemory` / `RedactInTelemetry` — флаги включения redaction

**`Config/ISecretMaskingService.cs`** — интерфейс с 4 методами: `MaskSecrets`, `ResolveSecret`, `IsSecretReference`, `ExpandSecretReferences`

**`Config/SecretMaskingService.cs`** — реализация:
- `MaskSecrets`: использует `IRedactionService` (API keys, emails, phones, credit cards, bearer tokens) + literal env var values
- `ResolveSecret("env:MYAPIKEY")`: prepends `HERCULES_SECRET_` prefix → ищет `HERCULES_SECRET_MYAPIKEY` в env vars
- `IsSecretReference`: проверяет `SecretReferencePrefix` prefix
- `ExpandSecretReferences`: заменяет `env:VAR_NAME` на resolved values

**DI** (CLI + WebAPI):
- `SecretsConfig` singleton
- `ISecretMaskingService` → `SecretMaskingService` (использует `IRedactionService`)

**`Skills/SkillPackager.cs`** — новый конструктор с `SecretsConfig` + `ISecretMaskingService`; `Export` redacts description + prompt перед записью в .skillpkg ZIP

**`Storage/MemoryStore.cs`** — новый конструктор; `WriteProfileAsync`, `WritePreferencesAsync`, `WriteEntitiesAsync`, `AppendAsync`, `AppendContextAsync` redacted content перед записью

**`Observability/OtelService.cs`** — nullable `SecretsConfig?` + `ISecretMaskingService?`; `SetTag`, `SetTags`, `AddEvent` redacts string values when `RedactInTelemetry = true`; `OtelHostBuilderExtensions` использует DI factory

**`WebApi/Controllers/AuditController.cs`** — `/api/audit/export` redacts details/policy_decision/permission_used перед CSV export

**Tests** — `tests/.../Config/SecretMaskingServiceTests.cs` — 24 теста (24/24 passed)

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings (NU1902 pre-existing)
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 539/545 passed (6 pre-existing failures: OTel ActivitySource без listener + WASM sandbox + locale encoding)

## Risks / Rollback
Случайная утечка в логи; централизованный redaction-фильтр.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
