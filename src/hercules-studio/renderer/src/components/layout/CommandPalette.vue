<script setup lang="ts">
defineEmits<{ close: [] }>();

const commands = [
  { id: "scan", label: "Scan for agents", shortcut: "" },
  { id: "add-connection", label: "Add connection", shortcut: "" },
  { id: "settings", label: "Open settings", shortcut: "Ctrl+," },
  { id: "new-skill", label: "New skill", shortcut: "Ctrl+N" },
  { id: "new-workflow", label: "New workflow", shortcut: "" },
];

function onSelect(_id: string) {
  // TODO: implement command actions
}
</script>

<template>
  <div class="fixed inset-0 z-50 flex items-start justify-center bg-black/50 pt-20" @click.self="$emit('close')">
    <div class="w-full max-w-lg rounded-xl border border-app bg-secondary shadow-2xl">
      <div class="border-b border-app px-4 py-3">
        <input
          type="text"
          placeholder="Type a command..."
          class="w-full bg-transparent text-app outline-none placeholder:text-secondary"
          autofocus
        />
      </div>
      <div class="max-h-80 overflow-y-auto py-1">
        <button
          v-for="cmd in commands"
          :key="cmd.id"
          class="flex w-full items-center justify-between px-4 py-2 text-left text-sm text-secondary hover:bg-tertiary hover:text-app"
          @click="onSelect(cmd.id)"
        >
          <span>{{ cmd.label }}</span>
          <span v-if="cmd.shortcut" class="text-xs text-secondary">{{ cmd.shortcut }}</span>
        </button>
      </div>
    </div>
  </div>
</template>