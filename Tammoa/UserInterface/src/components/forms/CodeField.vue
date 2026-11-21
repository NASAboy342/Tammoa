<script setup lang="ts">
/**
 * A labelled code field: a CodeMirror editor you can drag taller (bottom edge), or open in a large dialog.
 * Works for any language the editor knows (shell, javascript, python, json, text).
 */
import { defineAsyncComponent, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import ModalShell from '@/components/ModalShell.vue'
// CodeMirror is ~500 kB: load it only when a code field is actually shown
const CodeEditor = defineAsyncComponent(() => import('./CodeEditor.vue'))

const props = withDefaults(defineProps<{ modelValue: string; label: string; language: string; placeholder?: string; testid?: string; readOnly?: boolean; hint?: string }>(), {
  placeholder: undefined,
  hint: undefined,
  testid: undefined,
  readOnly: false,
})
const emit = defineEmits<{ 'update:modelValue': [value: string]; 'open-guide': [] }>()

const expanded = ref(false)
const LANGUAGE_LABEL: Record<string, string> = { shell: 'Shell', javascript: 'JavaScript', python: 'Python', json: 'JSON', text: 'Text' }
const languageLabel = () => LANGUAGE_LABEL[props.language] ?? props.language
</script>

<template>
  <div class="overflow-hidden rounded-control border border-line-strong" :data-testid="testid">
    <div class="flex items-center gap-1.5 border-b border-line bg-surface-muted px-2 py-1">
      <AppIcon name="code" :size="13" class="text-muted" />
      <span class="text-[11px] font-semibold text-ink-soft" data-testid="code-language">{{ languageLabel() }}</span>
      <span class="flex-1" />
      <button
        v-if="placeholder && !readOnly && modelValue.trim() === ''"
        type="button"
        class="inline-flex min-h-6 items-center gap-1 rounded-control border border-line-strong bg-surface px-1.5 text-[11px] font-semibold hover:bg-surface-hover"
        title="The grey text is only a hint. Put it in as real code and edit it."
        aria-label="Insert the starter code"
        data-testid="code-insert-starter"
        @click="emit('update:modelValue', placeholder)"
      >
        <AppIcon name="plus" :size="11" /> Insert starter
      </button>
      <button
        type="button"
        class="inline-flex h-6 w-6 items-center justify-center rounded-control hover:bg-surface-hover"
        title="Open in a larger editor"
        aria-label="Open in a larger editor"
        data-testid="code-expand"
        @click="expanded = true"
      >
        <AppIcon name="maximize" :size="13" />
      </button>
    </div>
    <div class="code-resize" data-testid="code-resize">
      <CodeEditor :model-value="modelValue" :language="language" :label="label" :placeholder="placeholder" :read-only="readOnly" @update:model-value="emit('update:modelValue', $event)" />
    </div>
    <div v-if="hint" class="flex items-start gap-1.5 border-t border-line bg-surface-muted px-2 py-1.5" data-testid="code-hint">
      <AppIcon name="bulb" :size="12" class="mt-0.5 shrink-0 text-muted" />
      <p class="min-w-0 flex-1 break-words font-mono text-[11px] leading-snug text-ink-soft">{{ hint }}</p>
      <button type="button" class="inline-flex min-h-6 shrink-0 items-center gap-1 rounded-control border border-line-strong bg-surface px-1.5 text-[11px] font-semibold hover:bg-surface-hover" title="Open the guide with examples" aria-label="Open the guide with examples" data-testid="code-open-guide" @click="emit('open-guide')">
        <AppIcon name="book" :size="11" /> Examples
      </button>
    </div>

    <ModalShell v-if="expanded" size="xl" testid="code-dialog" labelledby="code-dialog-title" @close="expanded = false">
      <div class="mb-3 flex items-center gap-2">
        <AppIcon name="code" :size="16" />
        <h2 id="code-dialog-title" class="text-sm font-bold tracking-tight">{{ label }}</h2>
        <span class="rounded-md bg-surface-muted px-1.5 py-0.5 text-[11px] font-semibold text-ink-soft">{{ languageLabel() }}</span>
        <span class="flex-1" />
        <button type="button" class="inline-flex min-h-8 items-center gap-1.5 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover" data-testid="code-collapse" @click="expanded = false">
          <AppIcon name="minimize" :size="13" /> Done
        </button>
      </div>
      <div class="h-[68vh] overflow-hidden rounded-control border border-line-strong" data-testid="code-dialog-editor">
        <CodeEditor :model-value="modelValue" :language="language" :label="label" :placeholder="placeholder" :read-only="readOnly" @update:model-value="emit('update:modelValue', $event)" />
      </div>
    </ModalShell>
  </div>
</template>

<style scoped>
/* drag the bottom edge to make the editor taller or shorter */
.code-resize {
  height: 176px;
  min-height: 96px;
  max-height: 75vh;
  resize: vertical;
  overflow: hidden;
}
</style>
