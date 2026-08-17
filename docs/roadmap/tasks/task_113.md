# Task 113 — Studio: openapi-typescript + openapi-fetch + Orval setup

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `studio-openapi-codegen-setup`
**Studio Stage:** 0

## Goal
Настроить pipeline генерации TypeScript API клиента в Hercules Studio из OpenAPI документа агента. Использовать openapi-typescript (types only) + openapi-fetch (typed fetch wrapper) + Orval (Vue Query hooks + Zod schemas + MSW mocks). Generated files коммитятся в git. Regen — ручной (`npm run gen:api`), можно запустить в любой момент.

## Acceptance criteria
- [ ] `openapi-typescript` установлен как devDependency
- [ ] `openapi-fetch` установлен как dependency
- [ ] `orval` установлен как devDependency
- [ ] `@tanstack/vue-query` установлен как dependency
- [ ] `zod` установлен как dependency (runtime validation)
- [ ] `msw` установлен как devDependency (mocks для dev без агента)
- [ ] `scripts/gen-api.mjs` — скрипт запуска gen:api:types + gen:api:orval
- [ ] `package.json` scripts: `gen:api`, `gen:api:types`, `gen:api:orval`
- [ ] `orval.config.ts` — config: tags-split mode, vue-query client, zod, mock, mutator
- [ ] `src/api/mutator.ts` — кастомный fetch с auth per-connection (X-Api-Key из safeStorage/mock)
- [ ] `src/api/schema.ts` — generated types (openapi-typescript output)
- [ ] `src/api/generated/` — generated Vue Query hooks per domain (Orval output)
- [ ] `src/api/models/` — generated Zod schemas + DTO interfaces (Orval output)
- [ ] `src/api/README.md` — "Generated. Do not edit. Run: npm run gen:api"
- [ ] `.gitignore` — НЕ игнорировать `src/api/` (generated files коммитятся)
- [ ] `npm run gen:api` работает: fetches openapi.json → schema.ts + generated/ + models/
- [ ] `npm run build` проходит с generated files
- [ ] `npm run typecheck` проходит с generated types

## orval.config.ts

```typescript
import { defineConfig } from "orval";

export default defineConfig({
  hercules: {
    // Source: build-time generated openapi.json (committed to git by task_109)
    // Alternative for dev: http://localhost:8421/openapi/v1.json (running agent)
    input: { target: "../../agent/Hercules.WebApi/openapi.json" },
    output: {
      mode: "tags-split",
      target: "src/api/generated",
      schemas: "src/api/models",
      client: "vue-query",
      mock: true,
      zod: true,
    },
    hooks: {
      mutator: "src/api/mutator.ts",
    },
  },
});
```

## mutator.ts

```typescript
import { useConnectionsStore } from "../stores/connections";

export const customMutator = async (config) => {
  const store = useConnectionsStore();
  const conn = store.active;
  if (!conn) throw new Error("No active agent connection");
  const key = await window.studioAPI.keys.getContributeKey(conn.id);
  
  const res = await fetch(`${conn.baseUrl}${config.url}`, {
    method: config.method,
    headers: { "X-Api-Key": key ?? "", "Content-Type": "application/json", ...config.headers },
    body: config.body ? JSON.stringify(config.body) : undefined,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return { data: await res.json(), status: res.status };
};
```

## Структура generated files

```
renderer/src/api/
├── schema.ts              # openapi-typescript (types only, all paths + schemas)
├── mutator.ts             # custom fetch wrapper (hand-written, not generated)
├── generated/             # Orval: Vue Query hooks per domain
│   ├── skills/
│   │   ├── skills.ts      # useListSkillsQuery, useCreateSkillMutation...
│   │   └── skills.msw.ts  # MSW handlers for dev
│   ├── chat/
│   │   └── chat.ts
│   ├── mesh/
│   │   └── mesh.ts
│   ├── config/
│   │   └── config.ts
│   └── ... (30 domains)
├── models/                # Orval: Zod schemas + DTO interfaces
│   ├── skillDto.ts
│   ├── chatResponseDto.ts
│   └── ...
└── README.md
```

## Dependencies (backend)
- task_109 (OpenAPI producer + build-time generation — нужен `openapi.json` в git)
- task_110 (WithTags — нужен для tags-split)
- task_111 (Produces<T> + DTOs — нужен для качественных типов)
- task_112 (WithName — нужен для operationId)

## Scope / Likely files
- New: `src/hercules-studio/scripts/gen-api.mjs`
- New: `src/hercules-studio/orval.config.ts`
- New: `src/hercules-studio/renderer/src/api/` (generated + mutator)
- Modified: `src/hercules-studio/package.json` (scripts + deps)
- Modified: `src/hercules-studio/.gitignore` (ensure api/ not ignored)
- Replace: `src/hercules-studio/renderer/src/sdk/types.ts` → `src/api/schema.ts` (generated)
- Keep: `src/hercules-studio/renderer/src/sdk/client.ts` → thin wrapper over openapi-fetch (or remove if Orval hooks replace it)

## Workflow

### Option A: From build-time openapi.json (CI, no running agent needed)
1. `dotnet build src/agent/Hercules.WebApi` → generates `openapi.json` in project root
2. `cd src/hercules-studio && npm run gen:api` (reads `../../agent/Hercules.WebApi/openapi.json`)
3. Commit: `git add renderer/src/api/ && git commit -m "regen API client"`

### Option B: From running agent (dev iteration)
1. Запустить агент: `dotnet run --project src/agent/Hercules.WebApi`
2. Temporarily change orval.config.ts target to `http://localhost:8421/openapi/v1.json`
3. `cd src/hercules-studio && npm run gen:api`
4. Restore orval.config.ts target to build-time path
5. Commit

### Using in code
```typescript
import { useListSkillsQuery } from "api/generated/skills/skills";
```

## Links
- Backlog: [../backlog.md](../backlog.md)
- Epic Studio: [../EPIC_Hercules_Studio/README.md](../EPIC_Hercules_Studio/README.md)
- Studio Stage 0: [../EPIC_Hercules_Studio/tasks/stage_00_skeleton.md](../EPIC_Hercules_Studio/tasks/stage_00_skeleton.md)