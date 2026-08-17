# Task 11 — Слоистая память

**Phase:** 1
**Status:** done
**Owner:** —
**Slug:** `layered-memory`

## Goal
Разделить request context, short-lived session/working memory, durable facts и append-only episodic records. Memory writes имеют source, confidence, TTL, sensitivity.

## Acceptance criteria

### Sub-tasks

- [x] `Memory/Layers/MemoryEntry.cs` — record с metadata: `Source` (string), `Confidence` (high/medium/low), `TtlMinutes` (int, 0=permanent), `Sensitivity` (public/internal/sensitive/restricted), `CreatedAt`, `Tags` (List<string>)
- [x] `Memory/Layers/IRequestContext` + `RequestContext` — ephemeral per-request data: current user input, session ID, conversation turns (не сохраняется на диск)
- [x] `Memory/Layers/IWorkingMemory` + `WorkingMemoryService` — short-lived in-memory key-value store: stores agent reasoning, scratchpad, mid-session facts; `Set(key, value, MemoryEntry)`, `Get(key)`, `Clear()`; session-scoped lifetime
- [x] `Memory/Layers/IDurableFactsStore` + `DurableFactsService` — backed by Markdown files in Memory/DurableFacts/: `StoreFact(key, value, MemoryEntry)`, `GetFact(key)`, `SearchFacts(query)`, `DeleteFact(key)`; facts can have TTL (permanent if 0)
- [x] `Memory/Layers/IEpisodicStore` + `EpisodicStore` — append-only episodic records: session summaries with `MemoryEntry` metadata; `AppendEpisode(sessionId, summary, MemoryEntry)`, `GetRecentEpisodes(count)`, `SearchEpisodes(query)`
- [x] `Memory/Layers/LayeredMemoryManager.cs` — facade composing all 4 layers; `BuildContextBlock()` assembles request context + working memory + durable facts + recent episodes for LLM prompt
- [x] `Memory/Layers/LayerMetadataExtractor.cs` — LLM-based extraction of source/confidence/sensitivity/Ttl from raw memory content (used by PersistSessionAsync)
- [x] `Storage/MemoryStore.cs` — add `WriteWithMetadataAsync(path, content, MemoryEntryMetadata)` — writes content + sidecar `.meta.json`
- [x] `Storage/Models.cs` — add `MemoryEntryMetadata` record for JSON sidecar
- [x] `Agent/MemoryManager.cs` — refactor to use `LayeredMemoryManager`; `PersistSessionAsync` now extracts facts/entities/prefs with `MemoryEntry` metadata (source="session_extract", confidence=medium, sensitivity=internal)
- [x] `Memory/Layers/LayeredMemoryConfig.cs` — `LayeredMemoryConfig` with: `MaxWorkingMemoryEntries` (default 100), `MaxEpisodesInContext` (default 5), `DefaultFactTtlMinutes` (default 0=permanent), `SensitivityRedactionEnabled` (default true)
- [x] `Config/AppConfig.cs` — add `MemoryConfig` section
- [x] `Program.cs` (CLI + WebAPI) — register layered memory services: `IWorkingMemory` (scoped), `IDurableFactsStore` (singleton), `IEpisodicStore` (singleton), `LayerMetadataExtractor` (singleton), `LayeredMemoryManager` (singleton)
- [x] Unit tests: `LayeredMemoryTests.cs` — 17 тестов: each layer CRUD, metadata preservation, TTL, sensitivity, context assembly
- [x] Unit tests: `WorkingMemoryServiceTests.cs` — 8 тестов: Set/Get/Clear, session isolation, capacity limit
- [x] `dotnet build` проходит без warnings
- [x] `dotnet test` проходит (431/431)

## Scope / Likely files
src/agent/Memory/Layers/, src/agent/Memory/Metadata/

## Dependencies
- блокирует / опирается на: [task_003 — hybrid-storage](task_003.md)

## Risks / Rollback
Утечка чувствительных данных в LLM-контекст; redaction-обязательна.

## Implementation notes

### 2026-08-12

**Добавлено:**

**`src/agent/Memory/Layers/`** — 9 новых файлов:
- `MemoryEntry.cs` — metadata record: Source, Confidence (High/Medium/Low), TtlMinutes, Sensitivity (Public/Internal/Sensitive/Restricted), CreatedAt, Tags; IsExpired and ShouldRedact helpers
- `IRequestContext.cs` / `RequestContext.cs` — ephemeral per-request context (not persisted)
- `IWorkingMemory.cs` / `WorkingMemoryService.cs` — ConcurrentDictionary-backed key-value store with capacity limit (default 100 entries) and random eviction
- `IDurableFactsStore.cs` / `DurableFactsService.cs` — Markdown + JSON sidecar for durable facts; slugified keys, TTL support, tag/keyPrefix search, expired fact cleanup
- `IEpisodicStore.cs` / `EpisodicStore.cs` — append-only monthly Markdown files with structured episode headers; parse episode headers to extract metadata
- `LayeredMemoryManager.cs` — facade composing all 4 layers; `BuildContextBlockAsync()` assembles non-redacted facts + recent episodes + working memory for LLM
- `LayerMetadataExtractor.cs` — LLM-based extraction of metadata from raw content (source/confidence/sensitivity/TTL/tags)
- `LayeredMemoryConfig.cs` — configuration: MaxWorkingMemoryEntries, MaxEpisodesInContext, DefaultFactTtlMinutes, SensitivityRedactionEnabled

**`Storage/Models.cs`** — `MemoryEntryMetadata` record for JSON sidecar serialization

**`Storage/MemoryStore.cs`** — `WriteWithMetadataAsync(path, content, MemoryEntryMetadata)` — writes content + JSON sidecar

**`Agent/MemoryManager.cs`** — refactored:
- Constructor now takes `LayeredMemoryManager?` (optional, backward-compatible)
- `PersistSessionAsync(transcript, sessionId, ct)` — stores extracted facts/episodes/preferences in layered memory with proper metadata
- `BuildContextBlock()` — appends layered context to legacy profile/prefs/entities block
- `Reset()` — clears both legacy store and layered working memory
- `AgentCore.EndSessionAsync` — updated to pass `SessionId` to `PersistSessionAsync`

**`Config/AppConfig.cs`** — added `MemoryConfig` section:
- MaxWorkingMemoryEntries (100), MaxEpisodesInContext (5), DefaultFactTtlMinutes (0=permanent), SensitivityRedactionEnabled (true), MaxFactAgeDays (0)

**`Program.cs` (CLI + WebAPI)** — DI registrations for all layered memory services

**`tests/.../Memory/Layers/`** — 25 new unit tests:
- LayeredMemoryTests.cs (17 tests): all 4 layers, TTL, sensitivity redaction, context assembly
- WorkingMemoryServiceTests.cs (8 tests): CRUD, isolation, capacity

**Validation:**
- `dotnet build` Hercules.csproj — 0 errors, 0 warnings
- `dotnet build` Hercules.WebApi.csproj — 0 errors, 0 warnings
- `dotnet test` — 431/431 passed

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
