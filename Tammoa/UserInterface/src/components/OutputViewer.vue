<script setup lang="ts">
/**
 * Shows a (possibly long) value: a scrollable preview with a character count, a copy button, and an "open in full"
 * dialog. Long lines wrap; nothing is cut off silently (the preview says how much more there is).
 */
import { computed, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import ModalShell from '@/components/ModalShell.vue'
import { prettyOutput } from '@/helpers/runHelpers'

const props = withDefaults(defineProps<{ value: unknown; title?: string; previewChars?: number }>(), { title: 'Output', previewChars: 1200 })

const full = computed(() => prettyOutput(props.value, Number.MAX_SAFE_INTEGER))
const preview = computed(() => (full.value.length > props.previewChars ? full.value.slice(0, props.previewChars) : full.value))
const cut = computed(() => full.value.length - preview.value.length)
const open = ref(false)
const copied = ref(false)

async function copy() {
  try {
    await navigator.clipboard.writeText(full.value)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    /* clipboard blocked (permissions): the full view lets the user select the text */
  }
}

const tool = 'inline-flex h-6 items-center gap-1 rounded-control px-1.5 text-[11px] font-semibold hover:bg-surface-hover'
</script>

<template>
  <div class="overflow-hidden rounded-control border border-line" data-testid="output-viewer">
    <div class="flex items-center gap-1 border-b border-line bg-surface-muted px-2 py-0.5">
      <span class="text-[11px] font-semibold text-ink-soft">{{ title }}</span>
      <span class="text-[11px] text-muted" data-testid="output-size">{{ full.length.toLocaleString() }} chars</span>
      <span class="flex-1" />
      <button type="button" :class="tool" title="Copy to clipboard" aria-label="Copy to clipboard" data-testid="output-copy" @click="copy">
        <AppIcon :name="copied ? 'check' : 'copy'" :size="12" /> {{ copied ? 'Copied' : 'Copy' }}
      </button>
      <button type="button" :class="tool" title="Open in full" aria-label="Open in full" data-testid="output-expand" @click="open = true">
        <AppIcon name="maximize" :size="12" /> Full
      </button>
    </div>
    <pre class="max-h-56 overflow-auto whitespace-pre-wrap break-words p-2 font-mono text-[11px] text-ink-soft" data-testid="output-preview">{{ preview }}</pre>
    <p v-if="cut > 0" class="border-t border-line px-2 py-1 text-[11px] text-muted" data-testid="output-cut">… {{ cut.toLocaleString() }} more characters. Use Full to read everything.</p>

    <ModalShell v-if="open" size="xl" testid="output-dialog" labelledby="output-dialog-title" @close="open = false">
      <div class="mb-3 flex items-center gap-2">
        <AppIcon name="file-text" :size="16" />
        <h2 id="output-dialog-title" class="text-sm font-bold tracking-tight">{{ title }}</h2>
        <span class="text-[11px] text-muted">{{ full.length.toLocaleString() }} chars</span>
        <span class="flex-1" />
        <button type="button" class="inline-flex min-h-8 items-center gap-1.5 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover" data-testid="output-dialog-copy" @click="copy">
          <AppIcon :name="copied ? 'check' : 'copy'" :size="13" /> {{ copied ? 'Copied' : 'Copy' }}
        </button>
        <button type="button" class="inline-flex min-h-8 items-center gap-1.5 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover" data-testid="output-dialog-close" @click="open = false">
          <AppIcon name="x" :size="13" /> Close
        </button>
      </div>
      <pre class="h-[68vh] overflow-auto whitespace-pre-wrap break-words rounded-control border border-line-strong p-3 font-mono text-xs" data-testid="output-full">{{ full }}</pre>
    </ModalShell>
  </div>
</template>
