/// <reference types="vite/client" />

declare module "*.vue" {
  import type { DefineComponent } from "vue";
  const component: DefineComponent<Record<string, never>, Record<string, never>, unknown>;
  export default component;
}

/** Injected at build time by vite.config.ts from package.json. */
declare const __APP_VERSION__: string;