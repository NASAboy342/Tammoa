<script setup lang="ts">
/** Start a flow from a ready-made template. A template with an AI card also asks which connection and model to use (both can be left for later). */
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import IconSelect from '@/components/forms/IconSelect.vue'
import ModalShell from '@/components/ModalShell.vue'
import { cardApi, type ParamOption } from '@/services/cardApi'
import { supportApi, type FlowTemplate } from '@/services/supportApi'
import { useConnectionStore } from '@/stores/connectionStore'
import { ICONS } from '@/components/icons/icons'

const emit = defineEmits<{ created: [flow: { id: string; name: string }]; cancel: [] }>()

const connections = useConnectionStore()
const templates = ref<FlowTemplate[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const selectedId = ref<string | null>(null)
const connectionId = ref('')
const model = ref('')
const models = ref<ParamOption[]>([])
const modelsBusy = ref(false)
const modelsError = ref<string | null>(null)
const busy = ref(false)

const selected = computed(() => templates.value.find((t) => t.id === selectedId.value) ?? null)
const ollamaConnections = computed(() => connections.byProvider('ollama'))
const iconOf = (t: FlowTemplate): IconName => (t.icon in ICONS ? (t.icon as IconName) : 'git-branch')

onMounted(async () => {
  try {
    const [list] = await Promise.all([supportApi.templates(), connections.load()])
    templates.value = list
    selectedId.value = list[0]?.id ?? null
    if (ollamaConnections.value.length === 1) connectionId.value = ollamaConnections.value[0].id
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
})

watch(connectionId, async (id) => {
  model.value = ''
  models.value = []
  modelsError.value = null
  if (!id) return
  modelsBusy.value = true
  try {
    models.value = await cardApi.options('ollama.models', id)
    if (!models.value.length) modelsError.value = 'This connection has no models yet. Pull one in Ollama (for example: ollama pull qwen3:14b), then choose again.'
  } catch (e) {
    modelsError.value = e instanceof Error ? e.message : String(e)
  } finally {
    modelsBusy.value = false
  }
})

async function create() {
  if (!selected.value || busy.value) return
  busy.value = true
  error.value = null
  try {
    const useAi = selected.value.needsAi && connectionId.value !== ''
    const flow = await supportApi.createFromTemplate(selected.value.id, useAi ? { connectionId: connectionId.value, model: model.value || undefined } : {})
    emit('created', flow)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <ModalShell testid="template-dialog" labelledby="template-title" size="lg" @close="emit('cancel')">
    <h2 id="template-title" class="mb-1 flex items-center gap-2 text-lg font-bold tracking-tight"><AppIcon name="layers" :size="18" /> Start from a template</h2>
    <p class="mb-4 text-xs text-muted">A ready-made flow you can run at once and change as you like.</p>

    <p v-if="loading" class="flex items-center gap-2 text-muted"><AppIcon name="loader" :size="15" class="animate-spin" /> Loading</p>

    <div v-else-if="templates.length" class="grid gap-2 sm:grid-cols-[14rem_1fr]">
      <div role="radiogroup" aria-label="Templates" class="space-y-1.5" data-testid="template-list">
        <button
          v-for="t in templates"
          :key="t.id"
          type="button"
          role="radio"
          :aria-checked="selectedId === t.id"
          class="flex w-full items-center gap-2 rounded-control border px-2.5 py-2 text-left text-xs font-semibold hover:bg-surface-hover"
          :class="selectedId === t.id ? 'border-2 border-ink' : 'border-line-strong'"
          :data-testid="`template-${t.id}`"
          @click="selectedId = t.id"
        >
          <AppIcon :name="iconOf(t)" :size="15" class="shrink-0" />
          <span class="min-w-0 flex-1 truncate">{{ t.name }}</span>
          <AppIcon v-if="selectedId === t.id" name="check" :size="14" />
        </button>
      </div>

      <div v-if="selected" class="rounded-card border border-line p-3.5" data-testid="template-details">
        <p class="mb-1 flex items-center gap-2 text-sm font-bold"><AppIcon :name="iconOf(selected)" :size="16" /> {{ selected.name }}</p>
        <p class="mb-2 text-xs text-ink-soft" data-testid="template-description">{{ selected.description }}</p>
        <p class="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-muted">
          <span class="inline-flex items-center gap-1"><AppIcon name="layers" :size="11" /> {{ selected.cardCount }} cards</span>
          <span v-for="n in selected.needs" :key="n" class="inline-flex items-center gap-1" data-testid="template-need"><AppIcon :name="n === 'Internet' ? 'globe' : 'bot'" :size="11" /> Needs {{ n }}</span>
          <span v-if="!selected.needs.length" class="inline-flex items-center gap-1"><AppIcon name="check-circle" :size="11" /> Nothing to set up</span>
        </p>

        <div v-if="selected.needsAi" class="space-y-2.5" data-testid="template-ai">
          <p v-if="!ollamaConnections.length" class="flex items-start gap-2 rounded-control border border-dashed border-line-strong p-2.5 text-[11px] text-ink-soft" data-testid="template-needs-connection">
            <AppIcon name="info" :size="14" class="mt-0.5 shrink-0" />
            <span>
              No Ollama connection yet. You can create the flow now and choose the connection in its AI card later, or
              <RouterLink :to="{ name: 'connections' }" class="inline-flex items-center gap-1 font-semibold underline" data-testid="template-go-connections" @click="emit('cancel')"><AppIcon name="plug" :size="11" /> add one first</RouterLink>.
            </span>
          </p>
          <template v-else>
            <IconSelect
              v-model="connectionId"
              label="Connection"
              icon="plug"
              :options="[{ value: '', label: 'Choose later in the card' }, ...ollamaConnections.map((c) => ({ value: c.id, label: c.name }))]"
              testid="template-connection"
            />
            <IconSelect
              v-if="connectionId"
              v-model="model"
              label="Model"
              icon="bot"
              :placeholder="modelsBusy ? 'Loading models…' : 'Choose a model'"
              :disabled="modelsBusy || !models.length"
              :options="models.map((m) => ({ value: m.value, label: m.label }))"
              testid="template-model"
            />
            <p v-if="modelsError" class="flex items-start gap-1.5 text-[11px] font-semibold" data-testid="template-models-error"><AppIcon name="alert" :size="13" class="mt-0.5 shrink-0" /> {{ modelsError }}</p>
          </template>
        </div>
      </div>
    </div>

    <p v-if="error" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="template-error"><AppIcon name="alert" :size="14" class="mt-0.5 shrink-0" /> {{ error }}</p>

    <div class="mt-5 flex justify-end gap-2">
      <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="template-cancel" @click="emit('cancel')">
        <AppIcon name="x" :size="15" /> Cancel
      </button>
      <button type="button" :disabled="!selected || busy" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="template-create" @click="create">
        <AppIcon :name="busy ? 'loader' : 'plus'" :size="15" :class="busy ? 'animate-spin' : ''" /> Create flow
      </button>
    </div>
  </ModalShell>
</template>
