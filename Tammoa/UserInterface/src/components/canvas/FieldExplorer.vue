<script setup lang="ts">
/**
 * "What can I use here?" for a piece of data: every field with its type, a preview and a button that copies the exact
 * template ({{input.json.name}}) to paste into a card setting. Long values keep their full text as a tooltip.
 */
import { computed, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import { flattenFields } from '@/helpers/fields'

const props = withDefaults(defineProps<{ value: unknown; root?: string; testid?: string }>(), { root: 'input', testid: 'fields' })

const query = ref('')
const copied = ref<string | null>(null)
const result = computed(() => flattenFields(props.value, props.root))
const rows = computed(() => {
  const q = query.value.trim().toLowerCase()
  return q ? result.value.rows.filter((r) => r.path.toLowerCase().includes(q) || r.preview.toLowerCase().includes(q)) : result.value.rows
})

async function copy(template: string) {
  try {
    await navigator.clipboard.writeText(template)
    copied.value = template
    setTimeout(() => copied.value === template && (copied.value = null), 1500)
  } catch {
    /* clipboard blocked: the template is shown in the tooltip and can be typed */
  }
}
</script>

<template>
  <div class="overflow-hidden rounded-control border border-line" :data-testid="testid">
    <span v-if="result.rows.length > 8" class="relative block border-b border-line p-1.5">
      <AppIcon name="search" :size="13" class="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted" />
      <input v-model="query" type="text" aria-label="Filter fields" placeholder="Filter fields" autocomplete="off" class="min-h-7 w-full rounded-control border border-line-strong bg-surface py-1 pl-7 pr-2 text-[11px] outline-none placeholder:text-muted focus:border-ink" data-testid="fields-filter" />
    </span>
    <ul class="max-h-64 overflow-auto" data-testid="fields-list">
      <li
        v-for="r in rows"
        :key="r.path"
        class="flex items-center gap-1.5 border-b border-line px-2 py-1 last:border-b-0 hover:bg-surface-hover"
        :style="{ paddingLeft: `${8 + r.depth * 12}px` }"
        data-testid="field-row"
        :data-path="r.path"
      >
        <span class="min-w-0 shrink-0 truncate font-mono text-[11px] font-semibold" :class="r.depth === 0 ? 'max-w-[8rem]' : 'max-w-[9rem]'" :title="r.path" data-testid="field-label">{{ r.label }}</span>
        <span class="shrink-0 rounded bg-surface-muted px-1 text-[10px] font-semibold uppercase tracking-wide text-muted" data-testid="field-type">{{ r.type }}</span>
        <span class="min-w-0 flex-1 truncate font-mono text-[11px] text-ink-soft" :title="r.full" data-testid="field-preview">{{ r.preview }}</span>
        <button
          v-if="r.addressable"
          type="button"
          class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface"
          :title="`Copy ${r.template}`"
          :aria-label="`Copy ${r.template}`"
          data-testid="field-copy"
          @click="copy(r.template)"
        >
          <AppIcon :name="copied === r.template ? 'check' : 'copy'" :size="12" />
        </button>
        <span v-else class="inline-flex h-6 w-6 shrink-0 items-center justify-center text-muted" title="This name contains a character a template cannot use (. [ ] | { }). Read it in a script instead." data-testid="field-unaddressable">
          <AppIcon name="info" :size="12" />
        </span>
      </li>
      <li v-if="!rows.length" class="px-2 py-2 text-[11px] text-muted" data-testid="fields-none">No field matches</li>
    </ul>
    <p v-if="result.truncated && !query" class="border-t border-line px-2 py-1 text-[11px] text-muted" data-testid="fields-truncated">Only the first fields are listed. Use the JSON view to see everything.</p>
  </div>
</template>
