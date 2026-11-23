<script setup lang="ts">
/**
 * What a running card has produced so far, shown as it grows: the model's thinking (collapsible) and its answer.
 * It follows the end of the text while you are at the bottom, and leaves you alone if you scrolled up to read.
 */
import { computed, nextTick, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { LiveOutput } from '@/services/runApi'
import { PHASE_ICON, phaseElapsed, phaseOf, phaseText, toolCallsOf } from '@/helpers/toolCalls'
import ToolCallList from './ToolCallList.vue'

const props = defineProps<{ live: LiveOutput; now?: number }>()

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
const phase = computed(() => phaseOf(props.live))
// the model sends nothing while it writes a tool call: say so instead of looking frozen
const silentModel = computed(() => phase.value?.kind === 'model' && !props.live.text && !props.live.thinking)
</script>

<template>
  <div class="space-y-2" data-testid="inspector-live" aria-live="polite">
    <div class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
      <AppIcon name="edit" :size="13" /> Writing now
    </div>

    <div v-if="phase" class="rounded-control border border-dashed border-line-strong p-2" data-testid="live-phase" :data-phase="phase.kind">
      <p class="flex items-center gap-1.5 text-[11px] font-semibold">
        <AppIcon name="loader" :size="13" class="animate-spin" />
        <AppIcon :name="PHASE_ICON[phase.kind]" :size="13" />
        <span data-testid="live-phase-text">{{ phaseText(phase) }}</span>
        <span class="ml-auto font-normal text-muted" data-testid="live-phase-elapsed">{{ phaseElapsed(phase, now) }}</span>
      </p>
      <p v-if="silentModel" class="mt-1 flex items-start gap-1.5 text-[11px] text-muted" data-testid="live-phase-hint">
        <AppIcon name="info" :size="12" class="mt-0.5 shrink-0" /> The model sends nothing while it writes a tool call. Turn Reasoning on to watch its thinking.
      </p>
    </div>

    <ToolCallList v-if="toolCallsOf(live).length" :calls="toolCallsOf(live)" testid="live-tool-calls" />

    <section v-if="live.thinking" data-testid="live-thinking-section">
      <button type="button" class="mb-1 flex w-full items-center gap-1.5 text-left text-[11px] font-semibold text-ink-soft" :aria-expanded="showThinking" data-testid="live-thinking-toggle" @click="showThinking = !showThinking">
        <AppIcon :name="showThinking ? 'chevron-up' : 'chevron-down'" :size="12" /> Thinking
        <span class="font-normal text-muted" data-testid="live-thinking-count">{{ count(live.thinking) }} chars</span>
      </button>
      <pre v-if="showThinking" ref="thinkingEl" class="max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-control border border-dashed border-line-strong p-2 font-mono text-[11px] italic leading-snug text-ink-soft" data-testid="live-thinking">{{ live.thinking }}</pre>
    </section>

    <section v-if="live.text || !phase" data-testid="live-answer-section">
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
