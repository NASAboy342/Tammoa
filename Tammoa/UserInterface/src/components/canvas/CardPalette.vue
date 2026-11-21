<script setup lang="ts">
/** "Add card" menu: the card types the backend offers, grouped and searchable. Closes on pick, Esc or outside click. */
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'
import { filterCards, groupCards } from '@/helpers/cardCatalog'

const props = defineProps<{ types: CardTypeInfo[]; iconFor: (type: string) => IconName; loading?: boolean }>()
const emit = defineEmits<{ add: [type: string] }>()

const open = ref(false)
const query = ref('')
const root = ref<HTMLElement | null>(null)
const search = ref<HTMLInputElement | null>(null)
const groups = computed(() => groupCards(filterCards(props.types, query.value)))

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

async function toggle() {
  open.value = !open.value
  if (open.value) {
    query.value = ''
    await nextTick()
    search.value?.focus()
  }
}
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
      @click="toggle"
    >
      <AppIcon :name="loading ? 'loader' : 'plus'" :size="15" :class="loading ? 'animate-spin' : ''" /> Add card
    </button>
    <div v-if="open" class="absolute left-0 top-full z-30 mt-1 flex max-h-[70vh] w-80 flex-col rounded-card border border-line-strong bg-surface" role="menu" data-testid="palette">
      <span class="relative block border-b border-line p-1.5">
        <AppIcon name="search" :size="14" class="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-muted" />
        <input
          ref="search"
          v-model="query"
          type="text"
          aria-label="Search cards"
          placeholder="Search cards"
          autocomplete="off"
          class="min-h-8 w-full rounded-control border border-line-strong bg-surface py-1 pl-8 pr-2 text-xs outline-none placeholder:text-muted focus:border-ink"
          data-testid="palette-search"
        />
      </span>
      <div class="min-h-0 flex-1 overflow-auto p-1.5">
        <p v-if="!groups.length" class="flex items-center gap-1.5 p-2 text-xs text-muted" data-testid="palette-empty"><AppIcon name="info" :size="13" /> No card matches</p>
        <section v-for="g in groups" :key="g.category" :data-testid="`palette-group-${g.category}`">
          <h3 class="px-2 pb-0.5 pt-2 text-[10px] font-semibold uppercase tracking-widest text-muted">{{ g.label }}</h3>
          <button
            v-for="t in g.cards"
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
              <span class="line-clamp-2 block text-[11px] leading-snug text-muted" :title="t.description">{{ t.description }}</span>
            </span>
          </button>
        </section>
      </div>
    </div>
  </div>
</template>
