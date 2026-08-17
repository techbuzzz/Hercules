# Task 112 — WithName на Marketplace + Template controllers

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `withname-marketplace-template`
**Studio Stage:** 0 (pre-req для Orval operationId)

## Goal
Добавить `.WithName("OperationId")` на все endpoints в MarketplaceController и TemplateController. Это единственные 2 контроллера из 35 без `WithName`. Orval использует operationId для генерации имён TS функций (`useListMarketplaceQuery`, `useApplyTemplateMutation`).

## Acceptance criteria
- [ ] `MarketplaceController.cs` — `.WithName(...)` на каждый MapXxx
- [ ] `TemplateController.cs` — `.WithName(...)` на каждый MapXxx
- [ ] OperationId имена консистентны с паттерном остальных контроллеров (PascalCase, глагол + существительное)
- [ ] Minimal API стиль сохранён
- [ ] OpenAPI документ содержит operationId для всех endpoints (включая Marketplace и Templates)
- [ ] `dotnet build` + `dotnet test` pass

## Suggested operationIds

### MarketplaceController
| Endpoint | OperationId |
|---|---|
| GET /api/marketplace | ListMarketplace |
| GET /api/marketplace/search | SearchMarketplace |
| GET /api/marketplace/{file}/verify | VerifyMarketplacePackage |
| GET /api/marketplace/{file}/deps | GetMarketplacePackageDeps |
| POST /api/marketplace/install | InstallMarketplacePackage |
| POST /api/marketplace/install-with-deps | InstallMarketplacePackageWithDeps |
| DELETE /api/marketplace/{file} | DeleteMarketplacePackage |
| POST /api/marketplace/publish | PublishMarketplacePackage |
| POST /api/marketplace/import | ImportMarketplacePackage |
| POST /api/marketplace/import-url | ImportMarketplacePackageFromUrl |

### TemplateController
| Endpoint | OperationId |
|---|---|
| GET /api/templates | ListTemplates |
| GET /api/templates/{fileName} | GetTemplate |
| POST /api/templates/{fileName}/apply | ApplyTemplate |

## Dependencies
- нет (можно делать параллельно с task_109/110/111)

## Scope / Likely files
src/agent/Hercules.WebApi/Controllers/MarketplaceController.cs, src/agent/Hercules.WebApi/Controllers/TemplateController.cs

## Links
- Backlog: [../backlog.md](../backlog.md)