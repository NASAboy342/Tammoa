<script setup lang="ts">
/** Centered dialog over a dimmed backdrop, teleported to body (avoids clipping). Esc and backdrop click emit `close`. */
import { onMounted, onUnmounted } from 'vue'

defineProps<{ testid?: string; labelledby?: string }>()
const emit = defineEmits<{ close: [] }>()

function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close')
}
onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => window.removeEventListener('keydown', onKey))
</script>

<template>
  <Teleport to="body">
    <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" @mousedown.self="emit('close')">
      <div
        role="dialog"
        aria-modal="true"
        :aria-labelledby="labelledby"
        :data-testid="testid"
        class="w-full max-w-md rounded-card border border-line-strong bg-surface p-5"
      >
        <slot />
      </div>
    </div>
  </Teleport>
</template>
