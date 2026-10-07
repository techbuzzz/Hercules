# ADR-0003: Port range 8421-8521 for Hercules agents

**Status:** Accepted
**Date:** 2026-08-14

## Context

Hercules agents currently default to port 5000, which conflicts with:
- Flask (Python)
- Synology DSM
- UPnP
- Syncthing

Need a dedicated port range that:
- Avoids all popular services (DBs, queues, web dev, monitoring, container, VNC)
- Has 100 ports for fleet agents on one machine
- Is mnemonic and easy to remember
- Is compact for fast Studio scanning

## Decision

**Port range 8421-8521. Default agent port = 8421.**

## Rationale

- **Mnemonic:** 8-4-2-1 = powers of two — programmers remember instantly
- **No conflicts:** 8421 is not used by any popular service
- **100 ports:** 8421-8521 = enough for fleet on one machine
- **Compact:** Studio scans 100 ports × 50 concurrent × 300ms = ~1s
- **Legacy fallback:** Studio also scans 5000 for existing installations

## Conflicts checked

| Service | Port | Conflict? |
|---|---|---|
| MySQL | 3306 | no |
| PostgreSQL | 5432 | no |
| Redis | 6379 | no |
| MongoDB | 27017 | no |
| RabbitMQ | 5672, 15672 | no |
| Kafka | 9092 | no |
| Node/React dev | 3000 | no |
| Astro (hercules-web) | 4321 | no |
| Flask/Synology/UPnP | 5000 | **yes — this is why we migrate** |
| Jupyter | 8888 | no |
| Prometheus | 9090 | no |
| Kibana | 5601 | no |
| Docker | 2375-2377 | no |
| K8s API | 6443 | no |
| VNC | 5900 | no |
| mDNS | 5353 | no |

## Consequences

- **Breaking change:** existing installations on 5000 must migrate to 8421
- CHANGELOG + Studio legacy scan (5000) for smooth migration
- `Hercules.WebApi/Program.cs` default URL changes
- `hercules-web/.env.example` PUBLIC_API_BASE changes
- Studio scans both 5000 (legacy) and 8421-8521 (new)