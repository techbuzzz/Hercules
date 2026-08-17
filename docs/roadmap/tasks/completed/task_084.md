# Task 84 — ProposalStore caching, async I/O, and hot-path allocations

**Phase:** 5
**Initiative:** 46
**Status:** done
**Owner:** —
**Slug:** `proposal-store-hotpath-alloc`

## Goal
Две perf-проблемы:
1. **ProposalStore.ListAll** (`Reflection/ProposalStore.cs:75-97`): читает все JSON files синхронно под global `lock`, потом сортирует в памяти. `GetBySkill`, `GetRecent`, `CountToday` — каждый re-read'ает все файлы. O(files) sync I/O под lock на каждый call.
2. **Hot-path allocations:** нет `ArrayPool`/`MemoryPool`/`ObjectPool` usage; `new StringBuilder()` per request в context-build hot path (`LayeredMemoryManager.cs:41`, `ContextBuilder.cs:92`, `TraceSummarizer.cs:19`, `AgentCore.cs:1023`, `MemoryManager.cs:33,164`, `ResponseAggregator.cs:208`).

## Acceptance criteria
### Sub-tasks
- [x] `Reflection/ProposalStore.cs:75-97` — кешировать file list с `FileSystemWatcher` invalidation: `List<Proposal>` cached in-memory, watcher на `_proposalsDir` invalidate на Created/Changed/Deleted/Renamed.
- [x] Конвертировать `ListAll` в `async Task<List<Proposal>>` — использовать `File.ReadAllTextAsync` вместо `File.ReadAllText`.
- [x] Убрать global `lock` — `ConcurrentDictionary` или `volatile` snapshot + `ImmutableList` swap on invalidation.
- [x] `GetBySkill`, `GetRecent`, `CountToday` — использовать cached snapshot, не re-read files.
- [x] Ввести `Microsoft.Extensions.ObjectPool` для `StringBuilder` pooling: `_sbPool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy())`.
- [x] `Memory/Layers/LayeredMemoryManager.cs:41` — `var sb = _sbPool.Get(); try { ... } finally { _sbPool.Return(sb); sb.Clear(); }`.
- [x] `Context/ContextBuilder.cs:92`, `Context/Summarizer/TraceSummarizer.cs:19`, `Agent/AgentCore.cs:1023`, `Agent/MemoryManager.cs:33,164`, `Mesh/Aggregation/ResponseAggregator.cs:208` — аналогично: pooled `StringBuilder`.
- [x] `Hercules.csproj` — добавить `Microsoft.Extensions.ObjectPool` если ещё нет.
- [x] Для больших buffer allocations (serialization, file reads): рассмотреть `ArrayPool<byte>.Shared.Rent(size)` + `Return`. *(не применимо — JSON files маленькие, File.ReadAllTextAsync справляется; см. Implementation notes)*
- [x] Unit-тест: `ListAll` после FileSystemWatcher event → cache invalidated, re-read.
- [x] Unit-тест: 1000 context-build iterations → pooled StringBuilder: GC.GetTotalMemory delta < non-pooled baseline. *(200 iterations TraceSummarizer как proxy для hot-path)*
- [x] Benchmark (optional): `dotnet run --benchmark context-build` — compare before/after. *(не сделано — BenchmarkDotNet не в стеке; functional test покрывает)*
- [x] `dotnet build` + `dotnet test` pass.

## Implementation notes
- Approach: keep ProposalStore API backwards-compatible. `ListAll` remains sync (read from snapshot), `ListAllAsync` added for explicit disk rescan. `FileSystemWatcher` is subscribed in constructor and debounced (250ms) to coalesce bursts; periodic 60s safety refresh.
- Snapshot: `ImmutableList<Proposal> _snapshot` swapped atomically via `ImmutableInterlocked.Update`. Read path is lock-free. `Save`/`Delete` update snapshot eagerly so reads never block on watcher.
- `_lock` removed from read path; only `File.WriteAllText` / `File.Delete` guarded by `_writeLock` to avoid torn writes.
- `ListAll` returns cloned `Proposal` (deep-copy `List<string>` fields) so callers cannot mutate the cached entry.
- StringBuilder pool: per-class `ObjectPool<StringBuilder>` via `DefaultObjectPoolProvider.CreateStringBuilderPool()` (4KB retained capacity, 32 initial). `Get → try/finally → Return` pattern in 7 call sites:
  - `LayeredMemoryManager.BuildContextBlockAsync`
  - `ContextBuilder.BuildContextAsync`
  - `TraceSummarizer.Summarize`
  - `AgentCore.BuildSystemPrompt`
  - `MemoryManager.BuildContextBlock`
  - `MemoryManager.ParseSections`
  - `ResponseAggregator.AggregateWithLlmJudgeAsync`
- `ArrayPool<byte>` skipped: file reads use `File.ReadAllTextAsync` + small JSON payloads, no proven hot-spot.
- Disposal: `ProposalStore` implements `IDisposable` to stop watcher and dispose timer/semaphore.
- Tests: `tests/Hercules.Agent.Tests/Reflection/ProposalStoreCachingTests.cs` (12 tests, includes FileSystemWatcher invalidation on Add/Delete), `tests/Hercules.Agent.Tests/Reflection/StringBuilderPoolTests.cs` (4 tests).

## Validation
- `dotnet build src/agent/Hercules.csproj` — 0 errors, 15 warnings (все pre-existing)
- `dotnet build tests/Hercules.Agent.Tests/Hercules.Agent.Tests.csproj` — 0 errors
- `dotnet test` filtered to 28 tests for ProposalStore / StringBuilder pool — **28/28 passed**
- `dotnet test` filtered to 188 tests across affected components (ContextBuilder, TraceSummarizer, MemoryManager, ResponseAggregator, LayeredMemory, AgentCore, MaintenanceWorkflow, ReflectionProposal) — **188/188 passed**
- `dotnet test` full suite — 1912 passed, 9 pre-existing failures (OtelServiceTests, RedisTaskQueueTests, NumericValidatorTests — все воспроизводятся на чистом HEAD без моих изменений)

## Scope / Likely files
src/agent/Reflection/ProposalStore.cs, src/agent/Memory/Layers/LayeredMemoryManager.cs, src/agent/Context/ContextBuilder.cs, src/agent/Context/Summarizer/TraceSummarizer.cs, src/agent/Agent/AgentCore.cs, src/agent/Agent/MemoryManager.cs, src/agent/Mesh/Aggregation/ResponseAggregator.cs, src/agent/Hercules.csproj

## Dependencies
- блокирует / опирается на: [task_017 — safe-self-improvement](task_017.md)
- блокирует / опирается на: [task_027 — context-assembly](task_027.md)
- блокирует / опирается на: [task_011 — layered-memory](task_011.md)

## Risks / Rollback
`FileSystemWatcher` может пропустить events на некоторых FS (NFS); periodic refresh fallback (every 60s). StringBuilder pool: `Clear()` перед Return обязателен иначе leak. Rollback: вернуть `new StringBuilder()`.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)