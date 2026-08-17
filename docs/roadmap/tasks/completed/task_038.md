# Task 38 - Mehanizmy discovery

**Phase:** 3
**Status:** done
**Owner:** -
**Slug:** `discovery`

## Goal
Static config, registry lookup i mDNS/Bonjour dlya deployment-specific discovery. Discovery sam po sebe ne vydayot trust.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Discovery/IDiscoverySource.cs` — `IDiscoverySource` interface: `Source`, `DiscoverAsync`, `Name`
- [x] `src/agent/Mesh/Discovery/DiscoveryResult.cs` — result wrapper: `DiscoveredAgent`, `Source`, `DiscoveredAt`, `Error`
- [x] `src/agent/Mesh/Discovery/StaticDiscoverySource.cs` — static config from `MeshConfig.Peers` + manifest fetch
- [x] `src/agent/Mesh/Discovery/RegistryDiscoverySource.cs` — delegates to `CapabilityRegistry` (re-exports agents)
- [x] `src/agent/Mesh/Discovery/IMdnsClient.cs` + `MdnsDiscoverySource.cs` — mDNS/Bonjour via DNS-SD multicast; stub on Windows
- [x] `src/agent/Mesh/Discovery/DiscoveryService.cs` — orchestrates all sources, deduplicates by agentId, cache with TTL
- [x] `Config/AppConfig.cs` — add `DiscoveryConfig` to `MeshConfig`
- [x] `MeshServiceExtensions.cs` — wire `IDiscoveryService`, `IDiscoverySource[]` via DI
- [x] `MeshController.cs` — add discovery endpoints: `/api/mesh/discovery/sources`, `/api/mesh/discovery/agents`, `/api/mesh/discovery/refresh`
- [x] `tests/Phase3Tests/DiscoveryTests.cs` — unit tests for each source + orchestration
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase3` — all Phase 3 tests pass (157/157)

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Discovery/`** — novaia papka dlya discovery layer:

- `DiscoveryResult.cs` — `DiscoveredAgent` record (AgentId, DisplayName, Endpoint, ManifestUrl, Source, DiscoveredAt, Capabilities, ManifestLoaded, Error) i `DiscoveryResult` wrapper s `Agents`, `Source`, `SourceName`, `Success`, `Error`. `DiscoverySourceKind` enum (Static/Registry/Mdns).
- `IDiscoverySource.cs` — `IDiscoverySource` interface: `Source`, `Name`, `DiscoverAsync`, `IsEnabled`.
- `StaticDiscoverySource.cs` — chitaet `MeshConfig.Peers`, fetchit manifest s kazhdogo peer, ispolzuet `HttpClient`, propuskaet self-agent.
- `RegistryDiscoverySource.cs` — delegiruet k `CapabilityRegistry.ListAgents()`, propuskaet self-agent.
- `IMdnsClient.cs` — `IMdnsClient` interface s `BrowseAsync` i `ResolveAsync`. `MdnsServiceInstance` i `MdnsResolvedInstance` records.
- `NoopMdnsClient.cs` — stub realizatsiya dlya Windows (net resursov, vozvrashchaet pustoi potok).
- `MdnsDiscoverySource.cs` — ispolzuet `IMdnsClient` dlya mDNS browse, izвлекает agentId iz TXT record.
- `DiscoveryService.cs` — `IDiscoveryService` + `DiscoveryService`: orchestruet vse source, dedupliciruet po AgentId (predpochitaet s manifestLoaded), keshiruet s TTL.

**`Config/AppConfig.cs`** — `MeshConfig` rasshiren s `Discovery DiscoveryConfig { get; set; } = new()`.

**`DiscoveryConfig`** — `AutoDiscoverOnStart`, `EnableMdns`, `MdnsServiceType`, `CacheTtlSeconds`, `AutoRefreshIntervalSeconds`.

**`Mesh/MeshServiceExtensions.cs`** — dobavleny registratsii:
- `StaticDiscoverySource` s `IHttpClientFactory`
- `NoopMdnsClient` kak `IMdnsClient` (stub)
- `MdnsDiscoverySource`
- `RegistryDiscoverySource`
- `DiscoveryService` kak `IDiscoveryService`

**`MeshController.cs`** — dobavleny discovery endpoints:
- `GET /api/mesh/discovery/sources` — status vsex discovery source
- `GET /api/mesh/discovery/agents` — vse discoverovannye agenty (s filtrom po source)
- `POST /api/mesh/discovery/refresh` — force-refresh vsex source

**`tests/Phase3Tests/DiscoveryTests.cs`** — 14 unit testov: StaticDiscoverySource, RegistryDiscoverySource, MdnsDiscoverySource, DiscoveryService (caching, deduplication, source filtering).

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (pre-existing warnings only)
- `dotnet test Phase3Tests` — 157/157 passed
- `dotnet test Phase4Tests` — 29/29 passed

## Scope / Likely files
src/agent/Mesh/Discovery/

## Dependencies
- blokiruet / opiraetsya na: [task_034 - capability-registry](task_034.md)

## Risks / Rollback
Spoofing discovery; trust policy otdelno.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
