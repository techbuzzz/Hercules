<script setup lang="ts">
/**
 * Monaco editor wrapper (Stage 2).
 *
 * Monaco ships as an AMD/ESM bundle that expects language workers to be
 * resolved by the host, which Vite does not do for us. Configuring
 * `MonacoEnvironment` once, at module scope, is the supported way to point
 * those worker URLs at real modules.
 *
 * The wrapper deliberately exposes only `v-model` and `language` — everything
 * else about the editor is an implementation detail so it can be replaced
 * without touching views.
 */
import { onBeforeUnmount, onMounted, ref, watch } from "vue";
// The core editor API only. Importing the `monaco-editor` root pulls
// `esm/vs/index.js`, which statically registers every language contribution and
// therefore emitted ~9.5 MB of workers (ts.worker alone is 7 MB) for editors
// that only ever hold Markdown or JSON.
//
// monaco-editor's exports map is "./*" -> "./esm/vs/*.js", so specifiers below
// omit the `esm/vs/` prefix; including it resolves to a doubled path.
import * as monaco from "monaco-editor/editor/editor.api";
import editorWorker from "monaco-editor/editor/editor.worker.js?worker";
import jsonWorker from "monaco-editor/language/json/json.worker.js?worker";
// Basic-languages contribution, cheap and eagerly applied. Markdown has no
// language worker, so it needs none — only tokenisation.
import "monaco-editor/languages/definitions/markdown/markdown.js";

// Configure once, before any editor is created.
(self as unknown as { MonacoEnvironment: monaco.Environment }).MonacoEnvironment = {
  getWorker(_moduleId: string, label: string) {
    return label === "json" ? new jsonWorker() : new editorWorker();
  },
};

const props = withDefaults(
  defineProps<{
    modelValue: string;
    language?: string;
    readOnly?: boolean;
    placeholder?: string;
  }>(),
  { language: "markdown", readOnly: false, placeholder: "" },
);

const emit = defineEmits<{ "update:modelValue": [string] }>();

const host = ref<HTMLDivElement | null>(null);
let editor: monaco.editor.IStandaloneCodeEditor | null = null;
/** Guards against echoing programmatic updates (theme change, reset) back to the parent. */
let applyingExternal = false;

onMounted(() => {
  if (!host.value) return;

  editor = monaco.editor.create(host.value, {
    value: props.modelValue,
    language: props.language,
    readOnly: props.readOnly,
    minimap: { enabled: false },
    scrollBeyondLastLine: false,
    fontSize: 13,
    lineHeight: 20,
    fontFamily: "var(--mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace)",
    automaticLayout: true,
    padding: { top: 10, bottom: 10 },
    scrollbar: { verticalScrollbarSize: 10, horizontalScrollbarSize: 10 },
    renderLineHighlight: "none",
    contextmenu: false,
  });

  if (props.placeholder) {
    editor.updateOptions({ placeholder: props.placeholder });
  }

  editor.onDidChangeModelContent(() => {
    if (applyingExternal || !editor) return;
    emit("update:modelValue", editor.getValue());
  });
});

onBeforeUnmount(() => {
  editor?.dispose();
  editor = null;
});

watch(
  () => props.modelValue,
  (next) => {
    if (!editor || editor.getValue() === next) return;
    applyingExternal = true;
    editor.setValue(next);
    applyingExternal = false;
  },
);

watch(
  () => props.language,
  (next) => {
    const model = editor?.getModel();
    if (model) monaco.editor.setModelLanguage(model, next);
  },
);

watch(
  () => props.readOnly,
  (next) => editor?.updateOptions({ readOnly: next }),
);

defineExpose({
  /** Inserts text at the caret — used by "insert template" affordances. */
  insertAtCaret(text: string) {
    if (!editor) return;
    const selection = editor.getSelection();
    editor.executeEdits("studio", [
      {
        range: selection ?? new monaco.Range(0, 0, 0, 0),
        text,
        forceMoveMarkers: true,
      },
    ]);
    editor.focus();
  },
});
</script>

<template>
  <div ref="host" class="h-full w-full" />
</template>

<style scoped>
:deep(.monaco-editor .margin) {
  background-color: transparent;
}
</style>