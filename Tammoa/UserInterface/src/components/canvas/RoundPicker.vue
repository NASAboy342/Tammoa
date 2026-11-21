<script setup lang="ts">
/** "Round 3 of 7" with previous / next: look at how a card ran in each round of a loop. Shown only when a card ran more than once. */
import { computed } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { Round } from '@/helpers/runHelpers'
import { statusLabel } from '@/helpers/runHelpers'

const props = defineProps<{ rounds: Round[]; modelValue: number | null }>()
const emit = defineEmits<{ 'update:modelValue': [value: number | null] }>()

const last = computed(() => props.rounds.length - 1)
/** index of the round shown: the chosen one, or (following the loop) the newest */
const index = computed(() => {
  const i = props.modelValue === null ? -1 : props.rounds.findIndex((r) => r.iteration === props.modelValue)
  return i === -1 ? last.value : i
})
const shown = computed(() => props.rounds[index.value])

function go(to: number) {
  const target = props.rounds[Math.min(last.value, Math.max(0, to))]
  emit('update:modelValue', target.current ? null : target.iteration) // the newest round = keep following the loop
}
const btn = 'inline-flex h-6 w-6 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover disabled:opacity-40'
</script>

<template>
  <div v-if="rounds.length > 1" class="flex items-center gap-1.5 rounded-control border border-line px-2 py-1" data-testid="round-picker">
    <AppIcon name="refresh" :size="12" class="shrink-0 text-muted" />
    <button type="button" :class="btn" :disabled="index === 0" title="Earlier round" aria-label="Earlier round" data-testid="round-prev" @click="go(index - 1)"><AppIcon name="arrow-left" :size="12" /></button>
    <span class="min-w-0 flex-1 text-center text-[11px] font-semibold" data-testid="round-label">
      Round {{ shown.iteration }} of {{ rounds[last].iteration }}
      <span class="font-normal text-muted">({{ statusLabel(shown.status).toLowerCase() }}<template v-if="shown.current && index === last">, latest</template>)</span>
    </span>
    <button type="button" :class="btn" :disabled="index === last" title="Later round" aria-label="Later round" data-testid="round-next" @click="go(index + 1)"><AppIcon name="arrow-right" :size="12" /></button>
  </div>
</template>
