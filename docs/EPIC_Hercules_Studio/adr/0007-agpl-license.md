# ADR-0007: AGPL-3.0 + Commercial License (dual licensing)

**Status:** Accepted
**Date:** 2026-08-14

## Context

Hercules Studio should be:
- Free for personal/non-commercial use
- Require a commercial license for corporate scenarios (CRM/ECM integration, enterprise deployment)

## Decision

**AGPL-3.0 (Community / Non-profit) + Commercial License for corporate use.**

## Rationale

- **AGPL-3.0:** Strong copyleft — any modified version distributed over network must share source. Corporations using it in SaaS/internal services must either open-source their modifications or buy a commercial license.
- **Dual licensing:** Same model as MongoDB (historic), Grafana, iText. Well-understood.
- **Trust-based for MVP:** No technical enforcement initially. License consent dialog at first-run. Commercial license key is just an indicator.
- **Corporate = by use case:** Integration with CRM/ECM, enterprise deployment = commercial. Personal/research/education = free.

## License consent UI (first-run)

```
┌──────────────────────────────────────────────────┐
│  Hercules Studio — License Agreement              │
│                                                   │
│  [AGPL-3.0 full text...]                          │
│                                                   │
│  ☐ I use Hercules Studio for non-commercial /     │
│    personal purposes (AGPL-3.0)                   │
│                                                   │
│  [Accept (Non-profit)]  [I have a commercial      │
│                          license key]  [Decline]   │
└──────────────────────────────────────────────────┘
```

- "Accept (Non-profit)" → continue, save `license-consent.json` with `type: "nonprofit"`
- "I have a commercial license key" → input key → save `type: "commercial", licenseKey: ...`
- "Decline" → close Studio

## Consent persistence

`userData/license-consent.json`:
```json
{
  "consentVersion": 1,
  "acceptedAt": "2026-08-14T...",
  "type": "nonprofit",
  "licenseKey": null
}
```

Re-shown on consent version change (license terms updated).

## Consequences

- Studio = open source (AGPL-3.0)
- `LICENSE` file = AGPL-3.0
- `LICENSE-COMMERCIAL.md` = commercial terms
- README/docs mention dual licensing
- No technical enforcement for MVP (trust-based)
- Studio is NOT bundled with .NET agent (agent has its own license)

## Addendum (2026-10-07) — the topology this ADR assumed no longer holds

**Status of this addendum:** records a change made when the project was relicensed
to AGPL-3.0 for **2.0.0**. The decision above stands; the *distribution topology* it
reasoned about has changed.

The ADR was written when Studio was a separate desktop application. It concluded
"Studio is NOT bundled with .NET agent (agent has its own license)", which left
MIT the natural choice for the repository root.

[ADR-0009](0009-web-first-studio.md) removed the Electron shell. The agent now
serves the Studio SPA itself at `/ui` (`http://localhost:8421/ui/`) — a single
process, no separate frontend. That has two consequences this ADR did not
anticipate:

1. **The repository is no longer cleanly separable.** An MIT-licensed agent
   distributing an AGPL-licensed SPA over the network is a distribution coupling
   MIT does not permit to stay one-way. Rather than preserve a split the topology
   no longer supports, the **whole repository is licensed AGPL-3.0 + commercial**
   as of 2.0.0.
2. **AGPL §13 is now triggered in practice.** Section 13 obliges an operator of a
   modified network-service version to offer its corresponding source to remote
   users. Before 2.0.0 the agent was distributed as software; from 2.0.0 it is a
   network service hosting the AGPL Studio, which is the case §13 was written for.

Concretely, as of 2.0.0:

- `LICENSE` = AGPL-3.0 (previously MIT)
- `LICENSE-COMMERCIAL.md` added — the commercial path the ADR required but which
  had never been written
- `src/hercules-studio/package.json` already declared `"license": "AGPL-3.0"`, so it
  is now consistent with the repository rather than contradicting it
- License consent is already implemented in Studio (`LicenseDialog.vue`,
  `platform/web.ts`, en + ru) and requires no new work

**This was an owner decision, not an inferred fix.** Existing MIT consumers are
affected, so the relicensing is a separate commit carrying a `BREAKING CHANGE`
changelog entry — it must never be mistaken for an incidental edit.

An open question that is *not* resolved here, and is a business decision rather
than a release blocker: whether a commercial license should cover the
agent + Studio **bundle** as shipped, now that the two can no longer be licensed
apart.

## Alternatives considered

- **MIT:** too permissive, no corporate incentive to buy license
- **BSL (Business Source License):** becomes open after N years; less familiar to users
- **Apache 2.0:** permissive, no copyleft pressure
- **Proprietary closed-source:** contradicts open-source ethos of Hercules