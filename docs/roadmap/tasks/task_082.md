# Task 82 — RouterHealthTracker, Redis CAS, Postgres reconnect, BuildServiceProvider

**Phase:** 5
**Initiative:** 45
**Status:** pending
**Owner:** —
**Slug:** `router-redis-postgres-di-fixes`

## Goal
Четыре отдельных high-severity fix в mesh/DI layer:

1. **RouterHealthTracker (H13):** `RecordSuccess`/`RecordFailure` (`Mesh/Router/RouterHealthTracker.cs:120-131`) мутируют `_window`, `_index`, `_count`, `_successes` без lock — data race, counter drift.
2. **Redis CAS (H14):** `RedisMeshStateStore.cs:148-170` — "optimistic locking" заявлен но не реализован: read-then-write без `WATCH`/`MULTI`/`EXEC` → race condition.
3. **Postgres LISTEN reconnect (H15):** `PostgresMeshBus.cs:278-289` — `WaitAsync` loop catch'ит `Exception` и тихо exits; при connection drop listener мёртв до restart процесса.
4. **BuildServiceProvider в registration (H16):** `MeshServiceExtensions.cs:451` — `services.BuildServiceProvider()` вызывается внутри `AddMeshServices` для resolution `MeshProfileLoader` → anti-pattern, duplicate container.

## Acceptance criteria
### Sub-tasks
- [ ] `Mesh/Router/RouterHealthTracker.cs:120-131` — добавить `lock` (или `SpinLock`) вокруг `RecordSuccess`/`RecordFailure` для защиты `_window`, `_index`, `_count`, `_successes`. ИЛИ конвертировать `PeerHealthRecord` в immutable record + `ConcurrentDictionary.Update` (CAS loop).
- [ ] Unit-тест: 100 параллельных `RecordSuccess`/`RecordFailure` → `_successes` == ожидаемое число, `_count` <= windowSize.
- [ ] `Mesh/Backends/Redis/RedisMeshStateStore.cs:148-170` — реализовать real optimistic lock: `var tran = db.CreateTransaction(); tran.AddCondition(Condition.HashEqual(key, "version", expectedVersion)); tran.HashSetAsync(key, ...); var ok = await tran.ExecuteAsync(ct);` если `!ok` → CAS failed.
- [ ] Unit-тест (с mock `IConnectionMultiplexer`): concurrent `CompareAndSetAsync` с одинаковым expectedVersion → только один succeeds, other returns false.
- [ ] `Mesh/Backends/Postgres/PostgresMeshBus.cs:278-289` — обернуть `WaitAsync` loop в reconnect-with-backoff: на exception → log warning → `await Task.Delay(backoff, ct)` → re-open connection → re-LISTEN → resume. Max backoff 30s. Не exit пока `ct` не cancelled.
- [ ] Unit-тест: mock `NpgsqlConnection` throws on `WaitAsync` → listener reconnects (calls OpenAsync + ListenAsync again) → resumes loop.
- [ ] `Mesh/MeshServiceExtensions.cs:451` — убрать `BuildServiceProvider()`. Рефактор: `MeshProfileLoader` resolved lazily через factory delegate: `services.AddSingleton<MeshProfileLoader>(sp => { var config = sp.GetRequiredService<IOptions<MeshProfilesConfig>>(); return new MeshProfileLoader(config); })` ИЛИ resolve в `IHostedService.StartAsync` после build.
- [ ] Unit-тест: `AddMeshServices` не вызывает `BuildServiceProvider` (grep source / reflection check).
- [ ] `dotnet build` + `dotnet test` pass.

## Scope / Likely files
src/agent/Mesh/Router/RouterHealthTracker.cs, src/agent/Mesh/Backends/Redis/RedisMeshStateStore.cs, src/agent/Mesh/Backends/Postgres/PostgresMeshBus.cs, src/agent/Mesh/MeshServiceExtensions.cs

## Dependencies
- блокирует / опирается на: [task_043 — mesh-router](task_043.md)
- блокирует / опирается на: [task_067 — redis-coordination-backend](task_067.md)
- блокирует / опирается на: [task_069 — postgres-shared-state](task_069.md)
- блокирует / опирается на: [task_066 — mesh-backends-abstraction](task_066.md)

## Risks / Rollback
Redis `WATCH`/`MULTI`/`EXEC` добавляет round-trip; для non-CAS `SetAsync` не нужен. Postgres reconnect может маскировать persistent failure — log + metric на каждый reconnect. Rollback: individual git revert per fix.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)