# Task 84 — ProposalStore caching, async I/O, and hot-path allocations

**Phase:** 6
**Initiative:** 46
**Status:** pending
**Owner:** —
**Slug:** `proposal-store-hotpath-alloc`

## Goal
Две perf-проблемы:
1. **ProposalStore.ListAll** (`Reflection/ProposalStore.cs:75-97`): читает все JSON files синхронно под global `lock`, потом сортирует в памяти. `GetBySkill`, `GetRecent`, `CountToday` — каждый re-read'ает все файлы. O(files) sync I/O под lock на каждый call.
2. **Hot-path allocations:** нет `ArrayPool`/`MemoryPool`/`ObjectPool` usage; `new StringBuilder()` per request в context-build hot path (`LayeredMemoryManager.cs:41`, `ContextBuilder.cs:92`, `TraceSummarizer.cs:19`, `AgentCore.cs:1023`, `MemoryManager.cs:33,164`, `ResponseAggregator.cs:208`).

## Acceptance criteria
### Sub-tasks
- [ ] `Reflection/ProposalStore.cs:75-97` — кешировать file list с `FileSystemWatcher` invalidation: `List<Proposal>` cached in-memory, watcher на `_proposalsDir` invalidate на Created/Changed/Deleted/Renamed.
- [ ] Конвертировать `ListAll` в `async Task<List<Proposal>>` — использовать `File.ReadAllTextAsync` вместо `File.ReadAllText`.
- [ ] Убрать global `lock` — `ConcurrentDictionary` или `volatile` snapshot + `ImmutableList` swap on invalidation.
- [ ] `GetBySkill`, `GetRecent`, `CountToday` — использовать cached snapshot, не re-read files.
- [ ] Ввести `Microsoft.Extensions.ObjectPool` для `StringBuilder` pooling: `_sbPool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy())`.
- [ ] `Memory/Layers/LayeredMemoryManager.cs:41` — `var sb = _sbPool.Get(); try { ... } finally { _sbPool.Return(sb); sb.Clear(); }`.
- [ ] `Context/ContextBuilder.cs:92`, `Context/Summarizer/TraceSummarizer.cs:19`, `Agent/AgentCore.cs:1023`, `Agent/MemoryManager.cs:33,164`, `Mesh/Aggregation/ResponseAggregator.cs:208` — аналогично: pooled `StringBuilder`.
- [ ] `Hercules.csproj` — добавить `Microsoft.Extensions.ObjectPool` если ещё нет.
- [ ] Для больших buffer allocations (serialization, file reads): рассмотреть `ArrayPool<byte>.Shared.Rent(size)` + `Return`.
- [ ] Unit-тест: `ListAll` после FileSystemWatcher event → cache invalidated, re-read.
- [ ] Unit-тест: 1000 context-build iterations → pooled StringBuilder: GC.GetTotalMemory delta < non-pooled baseline.
- [ ] Benchmark (optional): `dotnet run --benchmark context-build` — compare before/after.
- [ ] `dotnet build` + `dotnet test` pass.

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