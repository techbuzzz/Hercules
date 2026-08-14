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

## Alternatives considered

- **MIT:** too permissive, no corporate incentive to buy license
- **BSL (Business Source License):** becomes open after N years; less familiar to users
- **Apache 2.0:** permissive, no copyleft pressure
- **Proprietary closed-source:** contradicts open-source ethos of Hercules