# Task 117 — Spectral lint для OpenAPI документа

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `spectral-openapi-lint`
**Studio Stage:** 0 (pre-req для качества API контракта)

## Goal
Настроить Spectral (Stoplight) для линтинга OpenAPI документа Hercules.WebApi. Spectral проверяет:
- Correctness (синтаксис, схемы, references)
- Best practices (naming, pagination, error responses)
- OWASP API security (rate limiting, auth, input validation)
- Custom rules (Hercules-specific: tags на каждом endpoint, operationId, Produces<T>)

Linting запускается в CI на `openapi.json` (build-time generated, task_109).

## Acceptance criteria
- [ ] `spectral` npm package установлен (global или в solution root)
- [ ] `.spectral.yaml` ruleset файл в корне репозитория (или в `src/agent/Hercules.WebApi/`)
- [ ] Ruleset включает:
  - `spectral:oas` — стандартные OpenAPI 3.1 правила (built-in)
  - `spectral:oas3-api-servers` — servers определены
  - `spectral:oas3-operation-operationId` — каждый operation имеет operationId (для Orval codegen)
  - `spectral:oas3-operation-tags` — каждый operation имеет тег (для Orval tags-split)
  - Custom rule: каждый GET endpoint имеет `Produces` (response schema не пустой)
  - Custom rule: operationId в PascalCase (конвенция Hercules)
  - Custom rule: tag name в PascalCase
  - (опционально) `owasp:api3:2019` — OWASP API security ruleset
- [ ] `npm run lint:openapi` script в `src/agent/Hercules.WebApi/` (или solution root)
- [ ] CI: `dotnet build` → `spectral lint openapi.json` в GitHub Actions
- [ ] Линтинг проходит без errors на текущем openapi.json (после task_109-112)
- [ ] Warnings допустимы, errors блокируют CI

## .spectral.yaml (пример)

```yaml
extends:
  - spectral:oas
  # - owasp:api3:2019  # опционально, может быть шумно

rules:
  # Обязательные для Hercules codegen pipeline
  oas3-operation-operationId:
    description: Каждый operation должен иметь operationId (для Orval TS codegen)
    severity: error
    given: $.paths.*[get,post,put,patch,delete]
    then:
      function: truthy
      field: operationId

  oas3-operation-tags:
    description: Каждый operation должен иметь хотя бы один tag (для Orval tags-split)
    severity: error
    given: $.paths.*[get,post,put,patch,delete]
    then:
      function: truthy
      field: tags

  # Custom: PascalCase operationId
  hercules-operation-id-pascalcase:
    description: operationId должен быть в PascalCase (конвенция Hercules)
    severity: warn
    given: $.paths.*[get,post,put,patch,delete].operationId
    then:
      function: pattern
      field: operationId
      options:
        match: "^[A-Z][a-zA-Z0-9]*$"

  # Custom: response schema не пустой для success responses
  hercules-has-response-schema:
    description: Success responses (2xx) должны иметь схему (Produces<T>)
    severity: warn
    given: $.paths.*[get,post,put,patch,delete].responses[2*].content.application/json.schema
    then:
      function: truthy
```

## Setup

### Вариант 1: Global spectral (проще)

```powershell
npm install -g @stoplight/spectral-cli
cd src/agent/Hercules.WebApi
spectral lint openapi.json
```

### Вариант 2: В solution root (для CI)

```powershell
# В корне репозитория
npm init -y  # если нет package.json
npm install --save-dev @stoplight/spectral-cli
```

```jsonc
// package.json (solution root)
{
  "scripts": {
    "lint:openapi": "spectral lint src/agent/Hercules.WebApi/openapi.json"
  }
}
```

### CI integration

```yaml
# .github/workflows/studio-ci.yml (или отдельный workflow)
- name: Build agent (generates openapi.json)
  run: dotnet build src/agent/Hercules.WebApi/Hercules.WebApi.csproj

- name: Lint OpenAPI
  run: npx @stoplight/spectral-cli lint src/agent/Hercules.WebApi/openapi.json
```

## Dependencies
- task_109 (OpenAPI producer + build-time generation — нужен openapi.json)

## Scope / Likely files
- New: `.spectral.yaml` (solution root или `src/agent/Hercules.WebApi/`)
- New/Modified: `package.json` (solution root, если нет — создать для spectral)
- Modified: `.github/workflows/studio-ci.yml` (добавить lint step)

## Notes
- Spectral — Node.js tool, не зависит от .NET
- Линтинг на `openapi.json` (build-time generated, task_109), не на running agent
- Errors блокируют CI, warnings информативны
- Custom rules можно добавлять по мере необходимости (контракт evolves)
- OWASP ruleset (`owasp:api3:2019`) — опционально, может быть шумно на initial setup, добавить позже
- Spectral CLI: https://github.com/stoplightio/spectral

## Reference
- Spectral docs: https://docs.stoplight.io/docs/spectral
- OWASP ruleset: https://github.com/stoplightio/spectral-owasp-ruleset
- Custom rules: https://docs.stoplight.io/docs/spectral/rules
- Article: https://dev.to/nausaf/openapi-in-net-from-setup-to-build-time-generation-with-scalar-ui-1bio (mentions Spectral)

## Links
- Backlog: [../backlog.md](../backlog.md)
- task_109: [task_109.md](task_109.md) (openapi.json source)