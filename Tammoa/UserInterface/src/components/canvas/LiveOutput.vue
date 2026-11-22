<script setup lang="ts">
/**
 * What a running card has produced so far, shown as it grows: the model's thinking (collapsible) and its answer.
 * It follows the end of the text while you are at the bottom, and leaves you alone if you scrolled up to read.
 */
import { nextTick, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { LiveOutput } from '@/services/runApi'
import { toolCallsOf } from '@/helpers/toolCalls'
import ToolCallList from './ToolCallList.vue'

const props = defineProps<{ live: LiveOutput }>()

const showThinking = ref(true)
const thinkingEl = ref<HTMLElement | null>(null)
const textEl = ref<HTMLElement | null>(null)

function follow(el: HTMLElement | null, change: () => void) {
  const stick = !el || el.scrollHeight - el.scrollTop - el.clientHeight < 24 // was at (or near) the bottom
  change()
  void nextTick(() => {
    if (stick && el) el.scrollTop = el.scrollHeight
  })
}
watch(() => props.live.thinking, () => follow(thinkingEl.value, () => {}), { flush: 'pre' })
watch(() => props.live.text, () => follow(textEl.value, () => {}), { flush: 'pre' })

const count = (s?: string) => (s ?? '').length.toLocaleString()
</script>

<template>
  <div class="space-y-2" data-testid="inspector-live" aria-live="polite">
    <div class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
      <AppIcon name="edit" :size="13" /> Writing now
    </div>

    <ToolCallList v-if="toolCallsOf(live).length" :calls="toolCallsOf(live)" testid="live-tool-calls" />

    <section v-if="live.thinking" data-testid="live-thinking-section">
      <button type="button" class="mb-1 flex w-full items-center gap-1.5 text-left text-[11px] font-semibold text-ink-soft" :aria-expanded="showThinking" data-testid="live-thinking-toggle" @click="showThinking = !showThinking">
        <AppIcon :name="showThinking ? 'chevron-up' : 'chevron-down'" :size="12" /> Thinking
        <span class="font-normal text-muted" data-testid="live-thinking-count">{{ count(live.thinking) }} chars</span>
      </button>
      <pre v-if="showThinking" ref="thinkingEl" class="max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-control border border-dashed border-line-strong p-2 font-mono text-[11px] italic leading-snug text-ink-soft" data-testid="live-thinking">{{ live.thinking }}</pre>
    </section>

    <section data-testid="live-answer-section">
      <p class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold text-ink-soft">
        <AppIcon name="bot" :size="12" /> Answer <span class="font-normal text-muted" data-testid="live-text-count">{{ count(live.text) }} chars</span>
      </p>
      <pre ref="textEl" class="max-h-56 min-h-8 overflow-auto whitespace-pre-wrap break-words rounded-control border border-line-strong p-2 font-mono text-[11px] leading-snug" data-testid="live-text">{{ live.text }}<span class="live-caret" aria-hidden="true" /></pre>
    </section>
  </div>
</template>

<style scoped>
.live-caret {
  display: inline-block;
  width: 6px;
  height: 1em;
  margin-left: 1px;
  vertical-align: text-bottom;
  background: var(--ink);
  animation: blink 1s steps(2, start) infinite;
}
@keyframes blink {
  to {
    visibility: hidden;
  }
}
@media (prefers-reduced-motion: reduce) {
  .live-caret {
    animation: none;
  }
}
</style>
