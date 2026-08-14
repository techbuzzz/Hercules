# Task 96 — Port migration 5000 → 8421

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `port-migration-8421`
**Studio Stage:** 1 (pre-PR, до старта Studio)

## Goal
Перенести дефолтный порт Hercules.WebApi с 5000 на 8421 (новый диапазон 8421-8521). См. [ADR-0003](../EPIC_Hercules_Studio/adr/0003-port-range-8421.md).

## Acceptance criteria
- [ ] `Hercules.WebApi/Program.cs` — default URL `http://localhost:8421` (Development) / `http://0.0.0.0:8421` (Production)
- [ ] `src/agent/appsettings.json` — `Kestrel.Endpoints` или `Urls` = 8421
- [ ] `src/agent/Hercules.WebApi/appsettings.json` — аналогично
- [ ] `hercules-web/.env.example` — `PUBLIC_API_BASE=http://localhost:8421`
- [ ] `hercules-web/README.md` — обновить упоминания 5000 → 8421
- [ ] `docs/QUICKSTART-EN.md`, `QUICKSTART-RU.md` — обновить порты
- [ ] `CHANGELOG-EN.md`, `CHANGELOG-RU.md` — breaking change note
- [ ] `dotnet build` + `dotnet test` pass
- [ ] Manual smoke: `dotnet run --project src/agent/Hercules.WebApi` → agent on 8421

## Dependencies
- нет

## Scope / Likely files
src/agent/Hercules.WebApi/Program.cs, src/agent/appsettings.json, src/agent/Hercules.WebApi/appsettings.json, src/hercules-web/.env.example, docs/QUICKSTART-*.md

## Links
- ADR-0003: [../EPIC_Hercules_Studio/adr/0003-port-range-8421.md](../EPIC_Hercules_Studio/adr/0003-port-range-8421.md)
- Backlog: [../backlog.md](../backlog.md)