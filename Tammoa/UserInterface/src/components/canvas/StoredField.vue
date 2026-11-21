<script setup lang="ts">
/**
 * The text a Data card keeps (a plain .txt file). It is edited and saved on its own ("Save text"), independent of the flow's Save,
 * so it can be used at once; a run that writes to the card updates it here without a reload (unless you have unsaved edits).
 * Shows how other cards refer to it, and where the file is.
 */
import { computed, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import CodeField from '@/components/forms/CodeField.vue'
import { dataApi, type DataEntry } from '@/services/dataApi'
import { useDataStore } from '@/stores/dataStore'
import { dataNameProblem } from '@/helpers/dataNames'
import { tpl } from '@/helpers/fields'
import { formatWhen } from '@/helpers/flowHelpers'

const props = defineProps<{ flowId: string; cardId: string; cardName: string; saved: boolean; label: string }>()

const store = useDataStore()
const entry = ref<DataEntry | null>(null)
const draft = ref('')
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)
const changedElsewhere = ref(false)
const justSaved = ref(false)
const copied = ref<'ref' | 'path' | null>(null)

const dirty = computed(() => entry.value !== null && draft.value !== entry.value.text)
const problem = computed(() => dataNameProblem(props.cardName))
const reference = computed(() => tpl(`data.${props.cardName.trim()}`))
const tooBig = computed(() => draft.value.length > 1024 * 1024)

async function load(silent = false) {
  if (!props.saved) return
  if (!silent) loading.value = true
  error.value = null
  try {
    entry.value = await dataApi.read(props.flowId, props.cardId)
    draft.value = entry.value.text
    changedElsewhere.value = false
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!props.saved || saving.value || tooBig.value) return
  saving.value = true
  error.value = null
  justSaved.value = false
  try {
    entry.value = await dataApi.write(props.flowId, props.cardId, draft.value)
    draft.value = entry.value.text
    changedElsewhere.value = false
    justSaved.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    saving.value = false
  }
}

async function copy(what: 'ref' | 'path', text: string) {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = what
    setTimeout(() => copied.value === what && (copied.value = null), 1500)
  } catch {
    /* clipboard blocked: the text is shown on screen */
  }
}

watch(() => [props.flowId, props.cardId, props.saved], () => load(), { immediate: true })
// someone (a run, another window) changed this text: follow it, unless that would throw away what is being typed
watch(
  () => store.version,
  () => {
    if (store.last?.flowId !== props.flowId || store.last.cardId !== props.cardId) return
    if (dirty.value) changedElsewhere.value = true
    else void load(true)
  },
)

const btn = 'inline-flex min-h-8 items-center gap-1.5 rounded-control border px-2.5 text-[11px] font-semibold disabled:opacity-40'
</script>

<template>
  <div class="space-y-2" :data-testid="`stored-${cardId}`">
    <span class="block text-[11px] font-semibold text-ink-soft">{{ label }}</span>

    <p v-if="!saved" class="flex items-start gap-1.5 rounded-control border border-dashed border-line-strong p-2 text-xs text-ink-soft" data-testid="stored-unsaved">
      <AppIcon name="info" :size="13" class="mt-0.5 shrink-0" /> Save the flow once, then type the text for this card here.
    </p>

    <template v-else>
      <p v-if="loading && !entry" class="flex items-center gap-1.5 text-xs text-muted"><AppIcon name="loader" :size="13" class="animate-spin" /> Loading</p>

      <template v-if="entry">
        <CodeField v-model="draft" :label="label" language="text" placeholder="Type the text to store here" testid="stored-editor" />

        <div class="flex flex-wrap items-center gap-1.5">
          <button type="button" :disabled="!dirty || saving || tooBig" :class="[btn, 'border-ink bg-ink text-on-ink hover:opacity-90']" data-testid="stored-save" @click="save">
            <AppIcon :name="saving ? 'loader' : 'save'" :size="13" :class="saving ? 'animate-spin' : ''" /> Save text
          </button>
          <button type="button" :disabled="!dirty" :class="[btn, 'border-line-strong hover:bg-surface-hover']" title="Throw away what you typed and show the saved text" data-testid="stored-revert" @click="draft = entry!.text; changedElsewhere = false">
            <AppIcon name="refresh" :size="13" /> Revert
          </button>
          <span v-if="dirty" class="inline-flex items-center gap-1 text-[11px] font-semibold" data-testid="stored-dirty"><AppIcon name="edit" :size="12" /> Not saved yet</span>
          <span v-else-if="justSaved" class="inline-flex items-center gap-1 text-[11px] font-semibold" data-testid="stored-saved"><AppIcon name="check-circle" :size="12" /> Saved</span>
        </div>

        <p v-if="tooBig" class="flex items-start gap-1.5 text-[11px] font-semibold" data-testid="stored-toobig"><AppIcon name="alert" :size="12" class="mt-0.5" /> Stored text is limited to 1 MB.</p>
        <p v-if="changedElsewhere" class="flex items-center gap-1.5 rounded-control border border-dashed border-ink p-1.5 text-[11px] font-semibold" data-testid="stored-changed-elsewhere">
          <AppIcon name="info" :size="12" /> The saved text changed (a run or another window). Your edits are kept.
          <button type="button" class="ml-auto inline-flex min-h-6 items-center gap-1 rounded-control border border-line-strong px-1.5 hover:bg-surface-hover" data-testid="stored-reload" @click="load()"><AppIcon name="refresh" :size="11" /> Load it</button>
        </p>
        <p v-if="error" class="flex items-start gap-1.5 text-[11px] font-semibold" data-testid="stored-error"><AppIcon name="alert" :size="12" class="mt-0.5" /> {{ error }}</p>

        <div class="space-y-1 rounded-control border border-line p-2 text-[11px] text-ink-soft">
          <p v-if="problem" class="flex items-start gap-1.5 font-semibold text-ink" data-testid="stored-name-problem"><AppIcon name="alert" :size="12" class="mt-0.5 shrink-0" /> {{ problem }}</p>
          <p v-else class="flex items-center gap-1.5">
            Other cards read it as
            <code class="rounded bg-surface-muted px-1 font-mono text-ink" data-testid="stored-reference">{{ reference }}</code>
            <button type="button" class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" :title="`Copy ${reference}`" :aria-label="`Copy ${reference}`" data-testid="stored-copy-reference" @click="copy('ref', reference)">
              <AppIcon :name="copied === 'ref' ? 'check' : 'copy'" :size="12" />
            </button>
          </p>
          <p class="flex items-start gap-1.5">
            <AppIcon name="file-text" :size="12" class="mt-0.5 shrink-0" />
            <span class="min-w-0 flex-1 break-all font-mono" :title="entry.path" data-testid="stored-path">{{ entry.path }}</span>
            <button type="button" class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" title="Copy the file path" aria-label="Copy the file path" data-testid="stored-copy-path" @click="copy('path', entry!.path)">
              <AppIcon :name="copied === 'path' ? 'check' : 'copy'" :size="12" />
            </button>
          </p>
          <p class="text-muted" data-testid="stored-meta">{{ entry.size.toLocaleString() }} characters<span v-if="entry.updatedAt">, saved {{ formatWhen(entry.updatedAt) }}</span><span v-else>, nothing saved yet</span></p>
        </div>
      </template>
      <p v-else-if="error" class="flex items-start gap-1.5 text-[11px] font-semibold" data-testid="stored-error"><AppIcon name="alert" :size="12" class="mt-0.5" /> {{ error }}</p>
    </template>
  </div>
</template>
