<script setup lang="ts">
/** A small read-only code block with syntax highlighting and a Copy button, sized to its content. */
import { computed, defineAsyncComponent, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'

const CodeEditor = defineAsyncComponent(() => import('@/components/forms/CodeEditor.vue'))
const props = withDefaults(defineProps<{ code: string; language?: string; label?: string }>(), { language: 'text', label: 'Example code' })

const LABEL: Record<string, string> = { shell: 'Shell', javascript: 'JavaScript', python: 'Python', json: 'JSON', text: 'Text' }
const height = computed(() => Math.min(280, Math.max(48, props.code.split('\n').length * 19 + 14)))
const copied = ref(false)

async function copy() {
  try {
    await navigator.clipboard.writeText(props.code)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    /* clipboard blocked: the text can be selected in the block */
  }
}
</script>

<template>
  <div class="overflow-hidden rounded-control border border-line-strong" data-testid="code-snippet">
    <div class="flex items-center gap-1.5 border-b border-line bg-surface-muted px-2 py-1">
      <AppIcon name="code" :size="12" class="text-muted" />
      <span class="text-[11px] font-semibold text-ink-soft">{{ LABEL[language] ?? language }}</span>
      <span class="flex-1" />
      <button type="button" class="inline-flex min-h-6 items-center gap-1 rounded-control px-1.5 text-[11px] font-semibold hover:bg-surface-hover" title="Copy the code" aria-label="Copy the code" data-testid="snippet-copy" @click="copy">
        <AppIcon :name="copied ? 'check' : 'copy'" :size="12" /> {{ copied ? 'Copied' : 'Copy' }}
      </button>
    </div>
    <div :style="{ height: `${height}px` }" data-testid="snippet-body">
      <CodeEditor :model-value="code" :language="language" :label="label" read-only />
    </div>
  </div>
</template>
