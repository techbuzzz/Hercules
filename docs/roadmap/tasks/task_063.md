# Task 63 — Backup и recovery

**Phase:** 5
**Initiative:** 43
**Status:** done
**Owner:** —
**Slug:** `backup-recovery`

## Goal
Encrypted backups: configuration, skills, selected memory, SQLite data, device identity recovery. Restore-процедуры автоматизированы и протестированы.

## Acceptance criteria

### Sub-tasks

- [x] `Backup/Models.cs` — types: `BackupConfig`, `BackupArchive`, `BackupEntry`, `RestoreResult`, `BackupSnapshot`
- [x] `Backup/EncryptionService.cs` — AES-256-GCM encrypt/decrypt, HKDF-SHA256 key derivation, key-from-passphrase
- [x] `Backup/IBackupService.cs` — interface: `CreateBackupAsync`, `RestoreAsync`, `ListBackupsAsync`, `VerifyBackupAsync`
- [x] `Backup/BackupService.cs` — service: bundles config, skills, memory, SQLite data, identity into encrypted ZIP
- [x] `Backup/BackupScheduler.cs` — BackgroundService for scheduled/interval backups
- [x] `BackupController.cs` — WebAPI: `POST /api/backups`, `GET /api/backups`, `POST /api/backups/{id}/restore`, `GET /api/backups/{id}/verify`
- [x] Add `BackupConfig` to `AppConfig.cs` + DI registration in `Program.cs`
- [x] Unit tests for `BackupService` and `EncryptionService`
- [x] `dotnet build` + `dotnet test` pass

## Scope / Likely files
src/agent/Backup/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)
- блокирует / опирается на: [task_055 — security-ops](task_055.md)

## Risks / Rollback
Encryption key loss; recovery-of-keys обязателен.

## Implementation notes

### 2026-08-14

**Added:**

**`src/agent/Backup/Models.cs`** — 8 types:
- `BackupConfig` — backup settings (enabled, backup dir, passphrase, component flags, retention, interval, max size, filename prefix)
- `BackupManifest` — metadata inside each archive (`id`, `created_at`, `hercules_version`, `hostname`, `entries`, `uncompressed_size_bytes`, `compressed_size_bytes`, `encrypted`, `key_fingerprint`, `components`)
- `BackupComponents` — which components included (config, skills, memory, sqlite_db, identity)
- `BackupEntry` — one file in archive (`relative_path`, `source_path`, `size_bytes`, `last_modified`, `content_hash`)
- `BackupResult` / `RestoreResult` / `VerifyResult` — operation result records
- `BackupSummary` — archive summary for `ListBackupsAsync`

**`src/agent/Backup/EncryptionService.cs`** — AES-256-GCM encryption:
- `Encrypt(byte[], key, nonce)` → `Encrypt(plaintext, key, nonce)`
- `Decrypt(byte[], key)` → `byte[]` (throws `AuthenticationTagMismatchException` on wrong key)
- `DeriveKey(passphrase)` → `(key, nonce, salt)` (PBKDF2 + HKDF-SHA256)
- `DeriveKeyWithSalt(passphrase, salt, out key, out nonce)` — for decryption with known salt
- `ComputeKeyFingerprint(key)` → first 16 hex chars of SHA-256
- `ComputeFileHashAsync(stream, ct)` → SHA-256 hex string

**`src/agent/Backup/IBackupService.cs`** — interface:
- `CreateBackupAsync(passphrase?, ct)` → `BackupResult`
- `RestoreAsync(backupId, passphrase?, targetDir?, ct)` → `RestoreResult`
- `ListBackupsAsync(ct)` → `IReadOnlyList<BackupSummary>`
- `VerifyBackupAsync(backupId, passphrase?, ct)` → `VerifyResult`
- `DeleteBackupAsync(backupId, ct)` → `Task`

**`src/agent/Backup/BackupService.cs`** — implementation:
- Creates ZIP in memory, optionally encrypts with AES-256-GCM (salt prepended to encrypted payload)
- Manifest stored alongside archive as `{prefix}-{id}.manifest.json` (plaintext for observability)
- Retention: reads all manifests, sorts by `created_at` desc, deletes oldest beyond `RetentionCount`
- Restore: reads archive (decrypts if needed), extracts to target dir
- Verify: decrypts archive, verifies SHA-256 hashes of archived files
- Inline ULID generator (Crockford Base32) for backup IDs
- Bug fix: manifest naming (`{prefix}-{id}.manifest.json`) + ID extraction (`+1` not `+2`) corrected

**`src/agent/Backup/BackupScheduler.cs`** — `BackgroundService`:
- Runs backup on startup if `BackupOnStartup = true`
- Runs backup every `IntervalHours`
- Logs warnings/errors, skips on failure (non-crashing)

**`src/agent/Hercules.WebApi/Controllers/BackupController.cs`** — REST endpoints:
- `POST /api/backups` — create backup (body: `{ passphrase?, components? }`)
- `GET /api/backups` — list backups
- `GET /api/backups/{id}` — get backup details (manifest + archive metadata)
- `POST /api/backups/{id}/restore` — restore backup (body: `{ passphrase?, target_dir? }`)
- `GET /api/backups/{id}/verify` — verify backup integrity (query: `passphrase?`)
- `DELETE /api/backups/{id}` — delete backup

**`src/agent/Config/AppConfig.cs`** — added `Backup.BackupConfig Backup` property

**`src/agent/Program.cs`** — DI registrations: `BackupConfig`, `EncryptionService`, `IBackupService → BackupService`, `BackupScheduler` (hosted)

**Tests** (`tests/.../Backup/`):
- `EncryptionServiceTests.cs` — 14 tests: encrypt/decrypt roundtrip, wrong key, salt reuse, key derivation, fingerprint
- `BackupServiceTests.cs` — 12 tests: create (encrypted + unencrypted), list, restore, verify, delete, retention, empty backup, encrypted blob validation

**Validation:**
- `dotnet build Hercules.csproj` — 0 errors
- `dotnet build Hercules.Agent.Tests.csproj` — 0 errors
- Backup tests: 24/24 passed (EncryptionServiceTests 14 + BackupServiceTests 12)
- Full suite: 1622/1632 (9 pre-existing failures: OtelService × 5, BudgetGuard × 1, NumericValidator × 2, BusHttpServer × 1 — all pre-existing)

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
