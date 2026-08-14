# Task 39 - Identichnost i delegatsiya

**Phase:** 3
**Status:** done
**Owner:** -
**Slug:** `identity-delegation`

## Goal
mTLS, API-klyuchi ili OAuth-sovmestimye bearer-tokeny dlya peer-vyzovov. Delegated request neset minimum identity claims i tool authority.

## Acceptance criteria

### Sub-tasks

- [x] `src/agent/Mesh/Auth/DelegationToken.cs` — model: `Token`, `Subject`, `Issuer`, `Audience`, `Scope`, `ExpiresAt`, `Claims`
- [x] `src/agent/Mesh/Auth/IIdentityProvider.cs` — abstraction: `AuthenticateAsync`, `AuthMethod`
- [x] `src/agent/Mesh/Auth/BearerIdentityProvider.cs` — verify HMAC-SHA256 signed bearer tokens
- [x] `src/agent/Mesh/Auth/ApiKeyIdentityProvider.cs` — verify API keys (per-peer from config)
- [x] `src/agent/Mesh/Auth/MTlsIdentityProvider.cs` — stub for mTLS client cert verification
- [x] `src/agent/Mesh/Auth/TokenIssuer.cs` — issue short-lived bearer tokens (with scope)
- [x] `src/agent/Mesh/Auth/PeerAuthMiddleware.cs` — inbound auth for `/api/mesh/intent`, `/api/mesh/tasks/*`
- [x] `src/agent/Mesh/Auth/PeerAuthConfig.cs` — `TokenSecret`, `TokenTtlSeconds`, `MaxDelegationDepth`, `PeerKeys`
- [x] `Config/AppConfig.cs` — add `MeshConfig.PeerAuth` (`PeerAuthConfig`)
- [x] `MeshServiceExtensions.cs` — wire `IIdentityProvider`, `TokenIssuer`, `PeerAuthMiddleware`
- [x] `Program.cs` — register `PeerAuthMiddleware` in pipeline
- [x] `HttpTransportAdapter.cs` — apply outbound credentials via `IPeerCredentialProvider`
- [x] `TransportFactory.cs` — pass `IPeerCredentialProvider` to HTTP adapter
- [x] `tests/Phase3Tests/PeerAuthTests.cs` — 28 unit tests (token issue/verify, scope reduction, depth limits, providers, middleware logic)
- [x] `dotnet build` — 0 errors
- [x] `dotnet test Phase3` — all Phase 3 tests pass (186/186 incl. 28 new)

## Implementation notes

### 2026-08-13

**`src/agent/Mesh/Auth/`** — novaya papka dlya inter-agent auth:

- `DelegationToken.cs` — `DelegationToken` record: Version, Subject, Issuer, Audience, IssuedAt, ExpiresAt, JwtId, DelegationDepth, Scope, Claims, RootRequestId. `IdentityResult` record: Subject, AuthMethod, Issuer, Audience, Scopes, ExpiresAt, DelegationDepth, RootRequestId, Claims + `HasScope`, `Anonymous`.
- `IIdentityProvider.cs` — `IIdentityProvider` interface s `AuthMethod` i `AuthenticateAsync(headers, ct)`. `AuthenticationException` dlya nevalidnyx kredens.
- `BearerIdentityProvider.cs` — verify HMAC-signed bearer tokens cherez `Authorization: Bearer` header.
- `ApiKeyIdentityProvider.cs` — verify API keys (X-Api-Key + X-Agent-Id) s constant-time compare.
- `MTlsIdentityProvider.cs` — stub dlya mTLS cherez `X-Client-Cert-Subject` header (real verification — task extension).
- `TokenIssuer.cs` — `Issue`, `Delegate`, `Verify` s HMAC-SHA256, scope reduction proverka, depth limit. Generates random secret if `TokenSecret` empty.
- `PeerAuthConfig.cs` — `Enabled`, `TokenSecret`, `TokenTtlSeconds`, `MaxDelegationDepth`, `PeerKeys`, `OutboundMode`, `DefaultScopes`.
- `IPeerCredentialProvider.cs` — outbound abstraction: `ResolveAsync(targetAgentId, auth, ct)`, `PeerCredentials` (BearerToken + CustomHeaders + Apply method).
- `DefaultPeerCredentialProvider.cs` — issue bearer tokens (default) or resolve API keys (apikey mode). Honors parent auth context: increments depth, intersects scope.
- `PeerAuthMiddleware.cs` — applies to `/api/mesh/*` and `/agent.manifest.json`. Iterates providers, throws 401 on auth failure, enforces max depth. `HttpContext.GetMeshIdentity()` extension.

**`Config/AppConfig.cs`** — `MeshConfig.PeerAuth { get; set; } = new()`.

**`Mesh/MeshServiceExtensions.cs`** — wire TokenIssuer, IIdentityProvider[] (Bearer/ApiKey/MTls), IPeerCredentialProvider.

**`TransportFactory.cs`** — pass `IPeerCredentialProvider` to `HttpTransportAdapter` (constructor injection).

**`HttpTransportAdapter.cs`** — use `_credentials.ResolveAsync(targetAgentId, envelope.Auth)` to apply outbound auth (replaces hardcoded "mesh-key" placeholder). Backward-compat fallback for peer.Auth.Type=="apikey" when no provider.

**`Program.cs`** — `app.UseMiddleware<PeerAuthMiddleware>()` after `RateLimitMiddleware`.

**`tests/Phase3Tests/PeerAuthTests.cs`** — 28 unit tests covering: token issue/verify, tampered rejection, expired rejection, depth limits, scope reduction subset check, Bearer/ApiKey/MTls providers (auth/reject/missing), credential provider (bearer/apikey/none modes, auth context, excessive depth), PeerCredentials.Apply, IdentityResult.HasScope, DelegationToken.GetScopes/HasScope.

**Validation:**
- `dotnet build src/agent/Hercules.csproj -c Release` — 0 errors (pre-existing warnings only)
- `dotnet test Phase3+Phase4` — 214/214 passed (186 Phase3 + 28 Phase4 = 214 total)

## Scope / Likely files
src/agent/Mesh/Auth/

## Dependencies
- blokiruet / opiraetsya na: [task_015 - secrets-config](task_015.md)

## Risks / Rollback
Token leakage; korotkozhivushchie tokeny (5 min TTL) + scope reduction. mTLS — stub implementation, real verification — future work.

## Links
- Backlog: [../backlog.md](../backlog.md)
- Roadmap (EN): [../../ROADMAP-EN.md](../../ROADMAP-EN.md)
- Roadmap (RU): [../../ROADMAP-RU.md](../../ROADMAP-RU.md)
