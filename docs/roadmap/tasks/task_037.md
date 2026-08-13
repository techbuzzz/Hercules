# Task 37 — Опции транспорта

**Phase:** 3
**Status:** done
**Owner:** —
**Slug:** `transports`

## Goal
HTTP и gRPC first-class; опциональный адаптер для RabbitMQ, NATS и Azure Service Bus для асинхронных/disconnected сред.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Transport/ITransport.cs` — `ITransport` interface: `TransportKind`, `SendAsync(envelope, target, ct)`, `SupportsBidirectionalStreaming`
- [x] `src/agent/Mesh/Transport/TransportResult.cs` — result wrapper with `Kind`, `LatencyMs`, `TransportError`
- [x] `src/agent/Mesh/Transport/HttpTransportAdapter.cs` — HTTP adapter (moves existing IntentTransport logic, adds gzip support)
- [x] `src/agent/Mesh/Transport/GrpcTransportAdapter.cs` — gRPC adapter using Grpc.Net.Client (protopackage reference)
- [x] `src/agent/Mesh/Transport/IBusTransport.cs` — async messaging interface: `PublishAsync`, `SubscribeAsync`, `QueueName`, `SupportsAtMostOnce`, `SupportsAtLeastOnce`
- [x] `src/agent/Mesh/Transport/TransportFactory.cs` — factory: resolves `ITransport` from config (`MeshConfig.Transports`)
- [x] `Config/AppConfig.cs` — add `TransportConfig Transport` to `MeshConfig`
- [x] `MeshServiceExtensions.cs` — wire `ITransportFactory`, `ITransport`, update consumers (IntentRouter, SharedMemorySync, TaskLifecycleProtocol, MeshRouter)
- [x] `IntentRouter.cs` — inject `ITransport` directly (no longer concrete `IntentTransport`)
- [x] `tests/Phase3Tests/TransportTests.cs` — unit tests for factory, HTTP adapter, gRPC stub
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase` — all Phase tests pass (139 Phase3 + 29 Phase4)

## Implementation notes

### 2026-08-13

**Добавлено:**

**`src/agent/Mesh/Transport/`** — новая папка для transport layer:

- `ITransport.cs` — `ITransport : IDisposable` interface: `TransportKind` (Http/Grpc/Bus), `SendAsync`, `SupportsBidirectionalStreaming`, `DeliveryGuarantee`. Также `ICapabilityLookup` interface.
- `TransportResult.cs` — result wrapper: `Ok`, `TimedOut`, `Unreachable`, `TransportError`, `Rejected` factory methods. `TransportErrorKind` enum.
- `HttpTransportAdapter.cs` — HTTP/REST adapter: refactored из `IntentTransport`, добавляет gzip support, early cancellation check, 4xx/5xx distinction.
- `GrpcTransportAdapter.cs` — gRPC adapter: HTTP/2 via `SocketsHttpHandler`, connection pooling, `SupportsBidirectionalStreaming = true`. gRPC proto integration оставлен для task_068.
- `IBusTransport.cs` — async bus interface: `PublishAsync`, `SubscribeAsync`, `BusType`, queue names. `NoopBusTransport` stub для не-сконфигурированных окружений.
- `TransportFactory.cs` — `ITransportFactory` implementation: resolves transport from `TransportConfig`, wires via DI.

**`Config/AppConfig.cs`** — `MeshConfig` extended with `Transport Transport { get; set; } = new()`.

**`Mesh/MeshServiceExtensions.cs`** — добавлены регистрации `ITransportFactory` и `ITransport`, обновлены consumers:
- `IntentRouter` теперь принимает `ITransport` вместо `IntentTransport`; вызов `SendAsync` → `TransportResult.Response`.
- `SharedMemorySync` принимает `ITransport` + `ILogger<SharedMemorySync>`.
- `TaskLifecycleProtocol` принимает `ITransport?` (nullable).
- `MeshRouter` принимает `ITransport` вместо `IntentTransport`.

**`Mesh/CapabilityRegistry.cs`** — реализует `ICapabilityLookup.TryGet()`.

**`Hercules.csproj`** — добавлен `Grpc.Net.Client` 2.67.0.

**`tests/Phase3Tests/TransportTests.cs`** — 20 unit тестов: TransportResult factory methods, HttpTransportAdapter, GrpcTransportAdapter, NoopBusTransport, TransportFactory, TransportConfig defaults.

**Validation:**
- `dotnet build src/agent/Hercules.csproj` — 0 errors, pre-existing warnings only
- `dotnet test Phase3` — 139/139 passed
- `dotnet test Phase4` — 29/29 passed

## Scope / Likely files
src/agent/Mesh/Transport/

## Dependencies
- блокирует / опирается на: [task_035 — delegation-envelope](task_035.md)

## Risks / Rollback
Разные гарантии доставки; абстракция + per-transport caveats.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
