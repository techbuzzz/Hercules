// @ts-check
import { defineConfig } from 'astro/config';
import tailwindcss from '@tailwindcss/vite';

// Конфигурация Astro: dev-сервер на 4321, Tailwind v4 через vite-плагин.
export default defineConfig({
  server: {
    host: true,      // слушать 0.0.0.0 (для доступа из браузера/preview)
    port: 4321,
  },
  vite: {
    plugins: [tailwindcss()],
  },
});
