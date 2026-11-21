<script setup lang="ts">
/** Long text clamped to a few lines, with Show more / Show less (only offered when the text is actually cut) and the full text as a tooltip. */
import { nextTick, onMounted, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'

const props = withDefaults(defineProps<{ text: string; lines?: number; testid?: string; strong?: boolean }>(), { lines: 2, testid: undefined, strong: false })

const open = ref(false)
const cut = ref(false)
const el = ref<HTMLElement | null>(null)

function measure() {
  const node = el.value
  if (node && !open.value) cut.value = node.scrollHeight > node.clientHeight + 1
}
onMounted(measure)
watch(() => props.text, () => nextTick(measure))
</script>

<template>
  <div>
    <p
      ref="el"
      :title="cut && !open ? text : undefined"
      class="break-words text-[11px]"
      :class="[strong ? 'text-ink font-semibold' : 'text-muted', open ? '' : 'overflow-hidden']"
      :style="open ? undefined : { display: '-webkit-box', WebkitBoxOrient: 'vertical', WebkitLineClamp: lines }"
      :data-testid="testid"
    >{{ text }}</p>
    <button
      v-if="cut || open"
      type="button"
      class="mt-0.5 inline-flex items-center gap-1 text-[11px] font-semibold text-ink-soft hover:text-ink"
      data-testid="expand-text"
      @click="open = !open; nextTick(measure)"
    >
      <AppIcon :name="open ? 'chevron-up' : 'chevron-down'" :size="12" /> {{ open ? 'Show less' : 'Show more' }}
    </button>
  </div>
</template>
