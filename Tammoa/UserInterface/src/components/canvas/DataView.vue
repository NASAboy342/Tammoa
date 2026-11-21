<script setup lang="ts">
/** A value shown two ways: as a list of usable fields (with copy buttons), or as raw JSON (OutputViewer). */
import { ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import OutputViewer from '@/components/OutputViewer.vue'
import FieldExplorer from './FieldExplorer.vue'

withDefaults(defineProps<{ value: unknown; root?: string; title?: string; testid?: string }>(), { root: 'input', title: 'JSON', testid: 'data-view' })

const mode = ref<'fields' | 'json'>('fields')
const seg = 'inline-flex min-h-6 items-center gap-1 rounded-control border px-1.5 text-[11px] font-semibold'
</script>

<template>
  <div class="space-y-1.5" :data-testid="testid">
    <div class="flex items-center gap-1">
      <button type="button" :class="[seg, mode === 'fields' ? 'border-ink bg-surface-hover' : 'border-line-strong']" :aria-pressed="mode === 'fields'" data-testid="view-fields" @click="mode = 'fields'">
        <AppIcon name="list" :size="12" /> Fields
      </button>
      <button type="button" :class="[seg, mode === 'json' ? 'border-ink bg-surface-hover' : 'border-line-strong']" :aria-pressed="mode === 'json'" data-testid="view-json" @click="mode = 'json'">
        <AppIcon name="braces" :size="12" /> JSON
      </button>
    </div>
    <FieldExplorer v-if="mode === 'fields'" :value="value" :root="root" />
    <OutputViewer v-else :value="value" :title="title" />
  </div>
</template>
