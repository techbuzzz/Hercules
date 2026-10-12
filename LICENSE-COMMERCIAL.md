# Hercules — Commercial License

**Hercules is dual-licensed.** This file describes the commercial option.

- **Community / non-commercial use** → [AGPL-3.0](LICENSE), the GNU Affero General
  Public License, version 3 or later.
- **Commercial use** → the terms below, by agreement.

The dual-licensing decision is recorded in
[ADR-0007](docs/EPIC_Hercules_Studio/adr/0007-agpl-license.md).

## What "commercial use" means here

Under ADR-0007, the AGPL-3.0 grant covers **personal, research, educational and
other non-commercial use**. A commercial license is required when Hercules is used
for, or embedded in, a product or service that is operated for commercial
advantage. The cases named in the ADR are:

- **CRM / ECM integration** — Hercules embedded in a customer-relationship or
  enterprise-content system that serves business customers.
- **Enterprise deployment** — Hercules run inside an enterprise, or as part of a
  managed or hosted offering.

If you are unsure which side of the line you are on, ask before shipping. That
inquiry costs nothing and is not an admission of anything.

## Why AGPL-3.0 rather than MIT

The AGPL's section 13 requires that users interacting with a modified version over
a network be offered the corresponding source. Since 2.0.0 the agent serves the
Hercules Studio SPA itself at `/ui`, so a deployed agent offers **network
interaction** rather than merely shipping a binary. That is exactly the case the
AGPL was written for: it keeps improvements flowing back to the community instead
of being absorbed behind a hosted service.

This is a change from the project's earlier MIT terms. It was made deliberately,
approved by the project owner, and recorded as a `BREAKING CHANGE` in
[CHANGELOG-EN.md](CHANGELOG-EN.md). Consumers relying on the previous MIT terms
should take note before upgrading.

## Enforcement

Per ADR-0007, enforcement is **trust-based for the MVP**. Hercules Studio shows a
first-run license-consent dialog (English and Russian) and records the outcome as
`license-consent.json`; a commercial key is an indicator, not a technical control.
There is no licence server, no phone-home, and no feature gating.

## Obtaining a commercial license

Commercial terms are not published here, because they are negotiated per
deployment. Contact the project author:

- **Author:** Victor Buzin (`techbuzzz`)
- **Email:** buzin.victor@gmail.com

Please include what you intend to build, roughly how many agents you expect to
run, and whether the deployment is internal or customer-facing. Security reports
should go through the private advisory channel in [SECURITY.md](SECURITY.md)
rather than to the address above.