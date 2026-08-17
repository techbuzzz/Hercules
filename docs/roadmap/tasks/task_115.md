# Task 115 — Web-UI: openapi-typescript + Orval setup

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `webui-openapi-codegen-setup`
**Studio Stage:** N/A (hercules-web, не Studio)

## Goal
Настроить тот же pipeline генерации TypeScript API клиента в hercules-web (Astro) из OpenAPI документа агента. Использовать openapi-typescript (types) + Orval (Vue Query hooks для Astro islands + Zod + MSW mocks). Generated files коммитятся в git. Regen — ручной.

## Acceptance criteria
- [ ] `openapi-typescript` установлен как devDependency в `hercules-web`
- [ ] `openapi-fetch` установлен как dependency
- [ ] `orval` установлен как devDependency
- [ ] `@tanstack/vue-query` установлен как dependency (для Astro Vue islands)
- [ ] `zod` установлен как dependency
- [ ] `msw` установлен как devDependency
- [ ] `scripts/gen-api.mjs` — скрипт gen:api:types + gen:api:orval
- [ ] `package.json` scripts: `gen:api`, `gen:api:types`, `gen:api:orval`
- [ ] `orval.config.ts` — config: tags-split, vue-query, zod, mock, mutator (Astro env vars)
- [ ] `src/api/mutator.ts` — fetch wrapper с PUBLIC_API_BASE + PUBLIC_API_KEY (Astro env)
- [ ] `src/api/schema.ts` — generated types
- [ ] `src/api/generated/` — generated Vue Query hooks per domain
- [ ] `src/api/models/` — generated Zod schemas + DTO interfaces
- [ ] `src/api/README.md` — "Generated. Do not edit. Run: npm run gen:api"
- [ ] `.gitignore` — НЕ игнорировать `src/api/`
- [ ] `npm run gen:api` работает
- [ ] `npm run build` (astro build) проходит с generated files

## orval.config.ts (hercules-web)

```typescript
import { defineConfig } from "orval";

export default defineConfig({
  hercules: {
    input: { target: "http://localhost:8421/openapi/v1.json" },
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

## mutator.ts (hercules-web — Astro env vars)

```typescript
const API_BASE = import.meta.env.PUBLIC_API_BASE || "http://localhost:8421";
const API_KEY = import.meta.env.PUBLIC_API_KEY || "dev-local-key";

export const customMutator = async (config) => {
  const res = await fetch(`${API_BASE}${config.url}`, {
    method: config.method,
    headers: { "X-Api-Key": API_KEY, "Content-Type": "application/json", ...config.headers },
    body: config.body ? JSON.stringify(config.body) : undefined,
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return { data: await res.json(), status: res.status };
};
```

## Astro Vue islands integration

Astro islands с `client:load` / `client:visible` поддерживают Vue Query:
```astro
---
import SkillsList from '../components/SkillsList.vue';
---
<SkillsList client:load />
```

```vue
<!-- SkillsList.vue -->
<script setup lang="ts">
import { useListSkillsQuery } from "../api/generated/skills/skills";
const { data: skills, isLoading } = useListSkillsQuery();
</script>
```

## Dependencies (backend)
- task_109 (AddOpenApi)
- task_110 (WithTags)
- task_111 (Produces<T> + DTOs)
- task_112 (WithName)

## Scope / Likely files
- New: `src/hercules-web/scripts/gen-api.mjs`
- New: `src/hercules-web/orval.config.ts`
- New: `src/hercules-web/src/api/` (generated + mutator)
- Modified: `src/hercules-web/package.json` (scripts + deps)

## Links
- Backlog: [../backlog.md](../backlog.md)
- task_113: [task_113.md](task_113.md) (Studio — тот же pipeline)