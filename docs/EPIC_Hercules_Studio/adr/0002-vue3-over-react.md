# ADR-0002: Vue 3 over React for renderer

**Status:** Accepted
**Date:** 2026-08-14

## Context

Studio renderer needs a UI framework. Team preference: Vue. Alternatives: React, Svelte, Solid.

## Decision

**Choose Vue 3 (Composition API + `<script setup>`).**

## Rationale

- **Team preference:** Vue is the preferred choice
- **Composition API:** Clean reactive state, composables reuse, TS-friendly
- **Pinia:** Official state management, typed stores
- **shadcn-vue:** Tailwind-based component library available
- **Vue Flow:** Native Vue graph library for mesh + BPMN
- **vue-i18n:** Mature i18n
- **Ecosystem:** Vite + Vue = fast HMR, first-class TS support

## Consequences

- Vue ecosystem slightly smaller than React for some niche libs
- Vue Flow less mature than React Flow, but sufficient

## Alternatives considered

- **React + Next.js:** Larger ecosystem, but team preference is Vue
- **Svelte:** Smaller ecosystem, fewer IDE-quality components
- **Solid:** Too niche for IDE product