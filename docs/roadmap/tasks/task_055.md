# Task 55 — Security operations

**Phase:** 5
**Initiative:** 40
**Status:** done
**Owner:** —
**Slug:** `security-ops`

## Goal
Fleet-wide identity rotation, credential revocation, certificate renewal, проверка подписи пакетов, vulnerability reporting, security audit export.

## Acceptance criteria
- [x] Create `src/agent/Security/` directory with security operations services
- [x] Implement `IFleetIdentityService` for fleet-wide identity rotation and credential management
- [x] Implement `ICertificateService` for certificate renewal and validation
- [x] Implement `IPackageSigningService` for signed package verification
- [x] Implement `IVulnerabilityReporter` for vulnerability reporting
- [x] Implement `ISecurityAuditExporter` for security audit export
- [x] Add `SecurityOpsConfig` to `AppConfig.cs`
- [x] Register security services in DI container
- [x] Add unit tests for core security operations
- [x] Update CHANGELOG-EN.md

## Scope / Likely files
src/agent/Security/

## Dependencies
- блокирует / опирается на: [task_015 — secrets-config](task_015.md)
- блокирует / опирается на: [task_021 — skill-marketplace](task_021.md)
- блокирует / опирается на: [task_039 — identity-delegation](task_039.md)

## Risks / Rollback
Сложность key management; интеграция с KMS.

## Implementation notes
- Created `src/agent/Security/` with 12 files (interfaces + implementations)
- `FleetIdentityService`: Fleet identity management with rotation, credential revocation
- `CertificateService`: X.509 certificate generation, renewal, validation
- `PackageSigningService`: HMAC-SHA256 signature verification (Ed25519 not available in .NET Standard)
- `VulnerabilityReporterService`: Vulnerability tracking with severity levels
- `SecurityAuditExporterService`: Security audit export, compliance reports, identity audit trails
- Fixed AppConfig structure: moved properties from MeshConfig to AppConfig
- Updated MeshServiceExtensions.cs to use `appConfig.Property` instead of `meshCfg.Property`
- Build: succeeded | Tests: 1417 passed, 9 failed (pre-existing)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
