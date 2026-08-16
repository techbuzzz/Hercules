import { createApp } from "vue";
import { createPinia } from "pinia";
import { createI18n } from "vue-i18n";
import App from "./App.vue";
import "./assets/styles/global.css";
import "./mock-api";

import en from "./i18n/en.json";
import ru from "./i18n/ru.json";

const i18n = createI18n({
  legacy: false,
  locale: "en",
  fallbackLocale: "en",
  messages: { en, ru },
});

const app = createApp(App);
app.use(createPinia());
app.use(i18n);
app.mount("#app");