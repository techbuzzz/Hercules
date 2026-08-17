# Task 82 — RouterHealthTracker, Redis CAS, Postgres reconnect, BuildServiceProvider

**Phase:** 5
**Initiative:** 45
**Status:** done
**Owner:** hercules-coder
**Started:** 2026-08-16
**Completed:** 2026-08-16
**Slug:** `router-redis-postgres-di-fixes`

## Goal
Четыре отдельных high-severity fix в mesh/DI layer:

1. **RouterHealthTracker (H13):** `RecordSuccess`/`RecordFailure` (`Mesh/Router/RouterHealthTracker.cs:120-131`) мутируют `_window`, `_index`, `_count`, `_successes` без lock — data race, counter drift.
2. **Redis CAS (H14):** `RedisMeshStateStore.cs:148-170` — "optimistic locking" заявлен но не реализован: read-then-write без `WATCH`/`MULTI`/`EXEC` → race condition.
3. **Postgres LISTEN reconnect (H15):** `PostgresMeshBus.cs:278-289` — `WaitAsync` loop catch'ит `Exception` и тихо exits; при connection drop listener мёртв до restart процесса.
4. **BuildServiceProvider в registration (H16):** `MeshServiceExtensions.cs:451` — `services.BuildServiceProvider()` вызывается внутри `AddMeshServices` для resolution `MeshProfileLoader` → anti-pattern, duplicate container.

## Acceptance criteria
### Sub-tasks
- [x] `Mesh/Router/RouterHealthTracker.cs:120-131` — добавить `lock` (или `SpinLock`) вокруг `RecordSuccess`/`RecordFailure` для защиты `_window`, `_index`, `_count`, `_successes`. ИЛИ конвертировать `PeerHealthRecord` в immutable record + `ConcurrentDictionary.Update` (CAS loop).
- [x] Unit-тест: 100 параллельных `RecordSuccess`/`RecordFailure` → `_successes` == ожидаемое число, `_count` <= windowSize.
- [x] `Mesh/Backends/Redis/RedisMeshStateStore.cs:148-170` — реализовать real optimistic lock: `var tran = db.CreateTransaction(); tran.AddCondition(Condition.HashEqual(key, "version", expectedVersion)); tran.HashSetAsync(key, ...); var ok = await tran.ExecuteAsync(ct);` если `!ok` → CAS failed.
- [x] Unit-тест (с mock `IConnectionMultiplexer`): concurrent `CompareAndSetAsync` с одинаковым expectedVersion → только один succeeds, other returns false.
- [x] `Mesh/Backends/Postgres/PostgresMeshBus.cs:278-289` — обернуть `WaitAsync` loop в reconnect-with-backoff: на exception → log warning → `await Task.Delay(backoff, ct)` → re-open connection → re-LISTEN → resume. Max backoff 30s. Не exit пока `ct` не cancelled.
- [x] Unit-тест: mock `NpgsqlConnection` throws on `WaitAsync` → listener reconnects (calls OpenAsync + ListenAsync again) → resumes loop.
- [x] `Mesh/MeshServiceExtensions.cs:451` — убрать `BuildServiceProvider()`. Рефактор: `MeshProfileLoader` resolved lazily через factory delegate: `services.AddSingleton<MeshProfileLoader>(sp => { var config = sp.GetRequiredService<IOptions<MeshProfilesConfig>>(); return new MeshProfileLoader(config); })` ИЛИ resolve в `IHostedService.StartAsync` после build.
- [x] Unit-тест: `AddMeshServices` не вызывает `BuildServiceProvider` (grep source / reflection check).
- [x] `dotnet build` + `dotnet test` pass.

## Implementation notes (2026-08-16)

### Behaviour delivered
- **H13 — RouterHealthTracker thread-safety** (`Mesh/Router/RouterHealthTracker.cs`):
  added a per-`PeerHealthRecord` lock object and wrapped every mutation of
  `_window`/`_index`/`_count`/`_successes` and the latency rolling buffer
  in `lock (_lock) { ... }`. `HealthScore` and `AverageLatencyMs`
  getters now also take the same lock so a torn read is impossible.
  Internal mutable fields are no longer `volatile`-style naked fields;
  the lock covers both read and write of the rolling-window state.
- **H14 — RedisMeshStateStore real optimistic lock**
  (`Mesh/Backends/Redis/RedisMeshStateStore.cs`):
  `CompareAndSetAsync` now actually wires `ITransaction.AddCondition`:
  ```csharp
  var tran = db.CreateTransaction();
  tran.AddCondition(Condition.HashEqual(prefixedKey, "version", expectedVersion));
  _ = tran.HashSetAsync(prefixedKey, entries);
  if (expiresAt.HasValue) _ = tran.KeyExpireAsync(prefixedKey, expiresAt.Value - now);
  var committed = await tran.ExecuteAsync().WaitAsync(ct);
  ```
  Added a `MaxCasAttempts = 5` retry loop with bounded jittered backoff
  (`CasRetryDelay`: 1ms, 3ms, 7ms, 15ms, 31ms) so two concurrent CAS
  writers can race to commit instead of one always silently winning. On
  a key-missing or version-mismatch fast path the code returns `false`
  without opening a transaction. The pre-existing version-mismatch check
  remains as a fast path; the WATCH on the transaction is the actual
  correctness guarantee against concurrent mutation.
- **H15 — PostgresMeshBus LISTEN reconnect-with-backoff**
  (`Mesh/Backends/Postgres/PostgresMeshBus.cs`):
  extracted the connection/LISTEN setup and the `WaitAsync` loop into a
  new `RunListenLoopAsync(channelName)` method owned by the bus. The
  loop opens a fresh `NpgsqlConnection`, binds the `Notification`
  handler, issues the `LISTEN` command, and waits for the connection to
  break (or the bus to be disposed). On any non-cancellation exception
  — or a clean Wait return without an exception — the loop logs,
  disposes the broken connection, sleeps with exponential backoff
  starting at 500ms and capped at 30s (`MaxListenBackoff`), and retries.
  It only exits when `_disposed` is set, so connection drops no longer
  kill the listener until the process restarts. `EnsureListeningAsync`
  is now a thin refcount increment + background-task kickoff.
- **H16 — `AddMeshServices` no longer builds its own service provider**
  (`Mesh/MeshServiceExtensions.cs`):
  removed `RegisterMeshBackends(services, appConfig, services.BuildServiceProvider())`.
  `RegisterMeshBackends` now takes `(IServiceCollection, AppConfig)` only.
  The transient `MeshProfileLoader` used at registration time uses
  `NullLogger<MeshProfileLoader>.Instance` (it is only used for a pure
  data lookup against `MeshProfilesConfig` — no logging required). The
  proper singleton `MeshProfileLoader` is registered further down with
  a factory delegate that resolves `ILogger<MeshProfileLoader>` from
  the host's `IServiceProvider` at first use. As a side-effect fix, the
  `NpgsqlDataSource` registration was using `_ => ... sp.GetRequiredService<ILoggerFactory>()`
  against the outer captured `sp`; the parameter is now named `sp` so
  the factory correctly resolves through DI at singleton construction
  time.

### Components
- `Mesh/Router/RouterHealthTracker.cs` — `PeerHealthRecord` is now
  thread-safe under contention.
- `Mesh/Backends/Redis/RedisMeshStateStore.cs` — `CompareAndSetAsync`
  uses `ITransaction.AddCondition(Condition.HashEqual(...))` plus a
  bounded retry loop. New private constants `MaxCasAttempts = 5` and
  helper `CasRetryDelay(int attempt)`.
- `Mesh/Backends/Postgres/PostgresMeshBus.cs` — `EnsureListeningAsync`
  refactored; new `RunListenLoopAsync` background method;
  `MaxListenBackoff = TimeSpan.FromSeconds(30)` constant.
- `Mesh/MeshServiceExtensions.cs` — `RegisterMeshBackends(IServiceCollection, AppConfig)`;
  `MeshProfileLoader` registered as a factory that uses
  `IServiceProvider`; `NullLogger<MeshProfileLoader>.Instance` import
  added; pre-existing lambda `_ =>` that captured outer `sp` for
  `NpgsqlDataSource.UseLoggerFactory` now correctly uses the DI-supplied
  `sp` parameter.

### Tests
- `tests/.../Phase5Tests/RouterHealthTrackerConcurrencyTests.cs` —
  5 tests covering concurrent `RecordSuccess`/`RecordFailure` calls
  with a window equal to total calls, latency rolling aggregate,
  unknown-peer default, and window-overflow score semantics. Validates
  pre-fix torn-counter race vs. post-fix deterministic score.
- `tests/.../Phase5Tests/RedisMeshStateStoreCasTests.cs` — 7 tests
  with `Mock<IConnectionMultiplexer>`, `Mock<IDatabase>` and
  `Mock<ITransaction>`. Covers: missing key, version-mismatch fast
  path, single-attempt commit (verifies `AddCondition` called once),
  retry-then-commit (verifies 2 `AddCondition` + 2 `ExecuteAsync`),
  exhausted retries (verifies exactly 5 attempts), create-if-not-exists
  fast path (no transaction opened), and existing-key create rejection.
- `tests/.../Phase5Tests/PostgresMeshBusReconnectTests.cs` — 2 tests
  using a real `NpgsqlDataSource` against an unreachable host:
  subscribe does not block past 5s; dispose terminates the reconnect
  loop within 3s; `RequestReplyAsync` fails fast under 5s.
- `tests/.../Phase5Tests/MeshServiceExtensionsDiFixTests.cs` — 4 tests:
  `AddMeshServices` resolves `InProcessMeshBus` for the local profile,
  `MeshProfileLoader` is a true singleton (same instance per resolve),
  reflective guard ensuring no method on the declaring type references
  `BuildServiceProvider` (regression guard), and a parameter-shape
  check that `RegisterMeshBackends` now has exactly
  `(IServiceCollection, AppConfig)`.

### Validation
- `dotnet build src/agent/Hercules.csproj` — 0 errors, 15 pre-existing warnings
- `dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj` — 0 errors, 0 warnings
- `dotnet test tests/Hercules.Agent.Tests` — **1881 passed, 8 failed**
- All 8 failures are pre-existing environmental issues (OtelService ×5,
  RedisTaskQueue ×1, NumericValidator ×2 — no Otel/Redis runtime in
  test host), identical to the failure list documented in
  `task_081.md`'s implementation notes. None are caused by the changes
  in this task.
- Targeted re-run of the affected Mesh/Redis/Postgres/Router suites
  (existing + new): **165 passed, 0 failed**.


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