# Hercules.Supervisor

Process owner for Hercules agents. Implements the restart protocol the agent has always
documented as belonging to an external supervisor — *"supervisor (Studio / systemd / watcher)"*
in `Hercules.WebApi/Controllers/SystemController.cs`. The agent never kills itself.

Studio is a browser SPA since [ADR-0009](../../docs/EPIC_Hercules_Studio/adr/0009-web-first-studio.md),
so it can no longer inspect the process table, spawn processes or kill anything. This process does.

## What it does

- **Launches** each configured agent on start-up (optional).
- **Health-monitor**s each agent's `/api/health`; restarts after N consecutive failures.
- **Honours** `POST /api/system/restart` from an operator: performs the restart, then clears
  the flag. Exactly once per request.
- **Publishes** a PID registry on `GET /supervisor/agents`, replacing the process-table scan
  the old Electron UI performed.

## Status API

Bound to `127.0.0.1:8479` by default (`Supervisor:StatusPort`).

| Method | Path | Purpose |
|---|---|---|
| GET | `/supervisor/agents` | PID registry: name, pid, state, restarts, crashes, uptime |
| GET | `/supervisor/health` | Liveness of the supervisor itself |
| POST | `/supervisor/agents/{name}/start` | Start an agent |
| POST | `/supervisor/agents/{name}/stop` | Stop an agent |
| POST | `/supervisor/agents/{name}/restart` | Restart an agent |

```jsonc
// GET /supervisor/agents
{
  "count": 1,
  "agents": [{
    "name": "main", "pid": 18156, "state": "running",
    "restartCount": 0, "crashCount": 0,
    "consecutiveHealthFailures": 0, "uptime": "00:01:18.61"
  }]
}
```

## Credentials — read this before trusting `appsettings.json`

`POST /api/system/restart` requires the **system** role; a contribute key gets 403.

`ApiKeyStore.LoadOrGenerate` resolves keys in this order, and **configuration wins**:

1. `WebApi:ApiKeys` from the agent's `appsettings.json`, if non-empty — the file is not touched.
2. Otherwise `{DataRoot}/security/keys.json`, if it exists.
3. Otherwise a fresh contribute+system pair is generated and written there.

So with this repo's `WebApi:ApiKeys` in place, the live keys are `dev-local-key` (contribute)
and `dev-system-key` (system), **not** whatever is in `keys.json` — that file is only consulted
when configuration supplies nothing. If you clear `WebApi:ApiKeys`, read the generated keys with:

```powershell
Get-Content "D:\Sources\Github\HerculesData\security\keys.json" -Raw | ConvertFrom-Json
```

Then set `Supervisor__ApiKey` (environment variable) to the `hc_sys_…` value.

## Restart semantics

A restart request means *perform a restart*, not *keep restarting until healthy*:

1. Poll `GET /api/system/restart-pending`.
2. If pending and not already handled for that `requestedAt`, restart the `Primary` agent
   (honouring `RestartCooldownSeconds`).
3. Mark the request handled, then `POST /api/system/restart/clear`.

If the replacement fails to boot, recovery is the **health loop's** job (`AutoRestart`), not a
repeat of the operator's request. Restarting again would turn one operator action into a
restart loop.

Two failure modes this had to absorb, both found the hard way:

- The command may be a wrapper. `dotnet run` spawns a separate `Hercules.WebApi` process
  that survives the wrapper's tree-kill and keeps port 8421, so the replacement dies with
  "address already in use". **Supervise the built binary, not the build tool**
  (see `Supervisor:Agents[].Command`).
- The agent's log file is opened with `FileShare.Read` historically, so an overlapping
  restart made the new process throw an unhandled `IOException` on its first log line.
  Fixed in `FileLoggerProvider`, which now uses `FileShare.ReadWrite` and swallows
  logging failures.

## Configuration

`Supervisor` section of `appsettings.json` (all keys overridable via
`Supervisor__` environment variables):

| Key | Default | Meaning |
|---|---|---|
| `ApiBaseUrl` | `http://127.0.0.1:8421` | Agent REST base URL |
| `ApiKey` | — | **System-role** key, exchanged for a short-lived session token |
| `RestartPollSeconds` | 5 | `restart-pending` poll interval |
| `RestartGraceSeconds` | 10 | Close-wait, and the port-release budget |
| `UnhealthyThreshold` | 3 | Consecutive failures before AutoRestart |
| `RestartCooldownSeconds` | 30 | Minimum gap between restarts |
| `StatusPort` | 8479 | Status API port |
| `AutoStart` | false | Launch configured agents on boot |
| `Agents[]` | — | Per-agent command, env, health URL, cadence, `Primary` flag |

`Command` may be relative — it is resolved against `WorkingDirectory` (Windows resolves a
relative `ProcessStartInfo.FileName` against the *parent's* directory, so the supervisor
resolves it itself).

## Running

```bash
dotnet run --project src/hercules-supervisor
```

The agent's stdout/stderr are drained and re-emitted through the supervisor's logger.
That is the replacement for the terminal xterm.js used to show: agent output is no longer
lost, it goes to the supervisor log.