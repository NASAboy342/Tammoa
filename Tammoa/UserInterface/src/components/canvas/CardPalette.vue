<script setup lang="ts">
/** "Add card" menu: lists the card types the backend offers. Closes on pick, Esc or outside click. */
import { onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'

defineProps<{ types: CardTypeInfo[]; iconFor: (type: string) => IconName; loading?: boolean }>()
const emit = defineEmits<{ add: [type: string] }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

function onDocDown(e: PointerEvent) {
  if (open.value && root.value && !root.value.contains(e.target as Node)) open.value = false
}
function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') open.value = false
}
onMounted(() => {
  document.addEventListener('pointerdown', onDocDown)
  window.addEventListener('keydown', onKey)
})
onUnmounted(() => {
  document.removeEventListener('pointerdown', onDocDown)
  window.removeEventListener('keydown', onKey)
})

function pick(type: string) {
  emit('add', type)
  open.value = false
}
</script>

<template>
  <div ref="root" class="relative">
    <button
      type="button"
      class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3 text-xs font-semibold hover:bg-surface-hover"
      :aria-expanded="open"
      data-testid="palette-toggle"
      @click="open = !open"
    >
      <AppIcon :name="loading ? 'loader' : 'plus'" :size="15" :class="loading ? 'animate-spin' : ''" /> Add card
    </button>
    <div v-if="open" class="absolute left-0 top-full z-30 mt-1 w-72 rounded-card border border-line-strong bg-surface p-1.5" role="menu" data-testid="palette">
      <button
        v-for="t in types"
        :key="t.type"
        type="button"
        role="menuitem"
        class="flex w-full items-start gap-2.5 rounded-control p-2 text-left hover:bg-surface-hover"
        :data-testid="`palette-item-${t.type}`"
        @click="pick(t.type)"
      >
        <span class="mt-0.5 inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border border-line-strong"><AppIcon :name="iconFor(t.type)" :size="15" /></span>
        <span class="min-w-0">
          <span class="block text-xs font-semibold">{{ t.name }}</span>
          <span class="block text-[11px] leading-snug text-muted">{{ t.description }}</span>
        </span>
      </button>
    </div>
  </div>
</template>
