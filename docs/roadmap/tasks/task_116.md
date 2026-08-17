# Task 116 — Web-UI: migрація api.ts на Vue Query hooks

**Phase:** 8
**Status:** pending
**Owner:** —
**Slug:** `webui-migrate-vue-query`
**Studio Stage:** N/A (hercules-web)

## Goal
Мигрировать hercules-web с hand-written `src/lib/api.ts` (1391 строк) на generated Orval Vue Query hooks. Заменить all hand-written DTOs и API methods на generated. Добавить Vue Query plugin в Astro islands.

## Acceptance criteria
- [ ] `@tanstack/vue-query` интегрирован в Astro islands
- [ ] Vue Query `QueryClient` создан и доступен в islands (через Astro layout или client-side provide)
- [ ] `src/lib/api.ts` (1391 строк) — удалён или оставлен только для non-query утилит (API_BASE, API_KEY)
- [ ] All DTOs из `api.ts` заменены на generated `src/api/models/`
- [ ] All API methods заменены на generated `src/api/generated/` Vue Query hooks
- [ ] Astro components (`*.astro`) обновлены: импорты из `src/api/generated/` вместо `src/lib/api.ts`
- [ ] Vue island components обновлены: используют `useXxxQuery` / `useXxxMutation` вместо `api.xxx()`
- [ ] Loading/error states из Vue Query
- [ ] `npm run build` (astro build) проходит
- [ ] Manual smoke: `npm run dev` → все страницы работают (chat, skills, mesh, config, stats)

## Migration pattern (Astro)

```astro
---
// Before: server-side fetch in Astro frontmatter
import { api } from "../lib/api";
const skills = await api.listSkills();
---

<!-- After: client-side Vue Query in island -->
<SkillsList client:load />
```

```vue
<!-- SkillsList.vue (island) -->
<script setup lang="ts">
import { useListSkillsQuery } from "../api/generated/skills/skills";
const { data: skills, isLoading } = useListSkillsQuery();
</script>
```

## Astro-specific considerations

- Astro frontmatter (server-side) не может использовать Vue Query (client-side)
- Для SSR данных: оставить server-side fetch в frontmatter с generated types (openapi-typescript schema)
- Для interactive islands: Vue Query hooks
- Гибрид: SSR начальные данные → hydrate в Vue Query island

## Dependencies
- task_115 (Orval setup for hercules-web)
- task_109-112 (backend OpenAPI)

## Scope / Likely files
- Modified: all `src/hercules-web/src/pages/*.astro` (6 pages)
- Modified: all `src/hercules-web/src/components/*.astro` (8 components)
- Removed: `src/hercules-web/src/lib/api.ts` (или значительно сокращён)
- New: Vue Query setup в Astro layout

## Notes
- Самый большой diff в hercules-web (1391 строк api.ts → generated)
- Можно делать по страницам: chat → skills → mesh → config → stats → profile
- MSW mocks позволяют dev без агента

## Links
- Backlog: [../backlog.md](../backlog.md)
- task_115: [task_115.md](task_115.md)