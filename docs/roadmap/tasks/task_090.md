# Task 90 — Discovery panel

**Phase:** 7
**Initiative:** 48
**Status:** pending
**Owner:** —
**Slug:** `web-discovery-panel`

## Goal
`DiscoveryService` (task_038) объединяет три источника: `StaticDiscoverySource` (config), `RegistryDiscoverySource` (capability-registry re-export), `MdnsDiscoverySource` (mDNS/Bonjour). Web не показывает, какие источники активны, какие peer'ы найдены, и не позволяет вручную триггернуть refresh. Нужна панель на mesh-странице.

## Acceptance criteria

### Sub-tasks

- [ ] `src/lib/api.ts` — добавить `listDiscoverySources(): Promise<DiscoverySourceDto[]>`, `listDiscoveredAgents(): Promise<DiscoveredAgentDto[]>`, `refreshDiscovery(): Promise<{ refreshedAt: string; added: number; removed: number }>`
- [ ] Типы: `DiscoverySourceDto` (name, kind, enabled, lastRunAt, lastError), `DiscoveredAgentDto` (agentId, source, endpoint, discoveredAt, error)
- [ ] `src/components/DiscoveryPanel.astro` — список источников со status pills, lastRunAt timestamp, error inline; список discovered agents (deduplicated by agentId), источник каждого; кнопка «Refresh now» с post-refresh toast
- [ ] Встраивается в `src/pages/memmesh.astro` как новая секция над capability-registry
- [ ] mDNS stub статус («mDNS disabled on this platform») отображается явно, без скрытия
- [ ] `src/hercules-web/README.md` — обновить
- [ ] `npm run build` — exit 0
- [ ] Manual smoke: на dev-машине без mDNS — `static` enabled, `registry` enabled, `mdns` disabled (или not-supported); refresh показывает added/removed counter
