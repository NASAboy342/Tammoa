<script setup lang="ts">
/** "Data in this flow": every Data card with its reference (copy), a preview, and for JSON text its fields. Usable from any card. */
import { onMounted, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import FieldExplorer from './FieldExplorer.vue'
import { dataApi, type DataCardSummary } from '@/services/dataApi'
import { useDataStore } from '@/stores/dataStore'
import { tpl } from '@/helpers/fields'

const props = defineProps<{ flowId: string; /** changes when the flow was saved, so new Data cards show up */ refreshKey?: unknown }>()
const store = useDataStore()
const items = ref<DataCardSummary[]>([])
const open = ref<Record<string, unknown>>({})
const copied = ref<string | null>(null)

async function load() {
  try {
    items.value = await dataApi.list(props.flowId)
  } catch {
    items.value = []
  }
}
async function toggle(item: DataCardSummary) {
  if (item.cardId in open.value) {
    delete open.value[item.cardId]
    return
  }
  try {
    const text = (await dataApi.read(props.flowId, item.cardId)).text
    open.value[item.cardId] = JSON.parse(text)
  } catch {
    open.value[item.cardId] = {}
  }
}
async function copy(reference: string) {
  try {
    await navigator.clipboard.writeText(reference)
    copied.value = reference
    setTimeout(() => copied.value === reference && (copied.value = null), 1500)
  } catch {
    /* clipboard blocked */
  }
}
onMounted(load)
watch(() => [props.flowId, props.refreshKey, store.version], async () => {
  await load()
  for (const id of Object.keys(open.value)) {
    const item = items.value.find((i) => i.cardId === id)
    delete open.value[id]
    if (item?.isJson) await toggle(item) // keep an opened card open, with fresh fields
  }
})
</script>

<template>
  <section v-if="items.length" data-testid="data-cards">
    <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="database" :size="12" /> Data in this flow <span class="font-normal normal-case tracking-normal">(usable in every card)</span></h3>
    <ul class="space-y-1.5">
      <li v-for="d in items" :key="d.cardId" class="rounded-control border border-line" :data-testid="`data-card-${d.cardId}`">
        <div class="flex items-center gap-1.5 px-2 py-1.5">
          <span class="min-w-0 shrink-0 truncate font-mono text-[11px] font-semibold" :title="tpl(`data.${d.name}`)" data-testid="data-card-name">{{ d.name }}</span>
          <span class="shrink-0 rounded bg-surface-muted px-1 text-[10px] font-semibold uppercase tracking-wide text-muted">{{ d.isJson ? 'json' : 'text' }}</span>
          <span v-if="d.scope === 'shared'" class="shrink-0 rounded border border-line-strong px-1 text-[10px] font-semibold uppercase tracking-wide text-muted" title="Shared by name with every flow">shared</span>
          <span class="min-w-0 flex-1 truncate font-mono text-[11px] text-ink-soft" :title="d.preview" data-testid="data-card-preview">{{ d.size ? d.preview : 'empty' }}</span>
          <button v-if="d.isJson" type="button" class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" :title="d.cardId in open ? 'Hide the fields' : 'Show the fields'" :aria-label="d.cardId in open ? 'Hide the fields' : 'Show the fields'" data-testid="data-card-fields" @click="toggle(d)">
            <AppIcon :name="d.cardId in open ? 'chevron-up' : 'list'" :size="12" />
          </button>
          <button type="button" class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" :title="`Copy ${tpl(`data.${d.name}`)}`" :aria-label="`Copy ${tpl(`data.${d.name}`)}`" data-testid="data-card-copy" @click="copy(tpl(`data.${d.name}`))">
            <AppIcon :name="copied === tpl(`data.${d.name}`) ? 'check' : 'copy'" :size="12" />
          </button>
        </div>
        <div v-if="d.cardId in open" class="border-t border-line p-1.5" data-testid="data-card-explorer">
          <FieldExplorer :value="open[d.cardId]" :root="`data.${d.name}`" />
        </div>
      </li>
    </ul>
  </section>
</template>
