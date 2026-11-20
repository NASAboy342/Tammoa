<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import IconField from '@/components/forms/IconField.vue'
import FlowSwitch from '@/components/FlowSwitch.vue'
import DeleteFlowDialog from '@/components/DeleteFlowDialog.vue'
import { flowApi, type Flow } from '@/services/flowApi'
import { countCards, countConnections, formatWhen } from '@/helpers/flowHelpers'

const route = useRoute()
const router = useRouter()
const id = String(route.params.id)

const flow = ref<Flow | null>(null)
const name = ref('')
const description = ref('')
const loading = ref(true)
const saving = ref(false)
const toggling = ref(false)
const error = ref<string | null>(null)
const saved = ref(false)
const confirmingDelete = ref(false)
const deleteBusy = ref(false)
const deleteError = ref<string | null>(null)

const dirty = computed(() => !!flow.value && (name.value !== flow.value.name || description.value !== flow.value.description))
const valid = computed(() => name.value.trim().length > 0)
const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

function adopt(f: Flow) {
  flow.value = f
  name.value = f.name
  description.value = f.description
}

onMounted(async () => {
  try {
    adopt(await flowApi.get(id))
  } catch (e) {
    error.value = message(e)
  } finally {
    loading.value = false
  }
})

async function save() {
  if (!flow.value || !valid.value || saving.value) return
  saving.value = true
  error.value = null
  saved.value = false
  try {
    // The definition (cards + connections) is sent back untouched; the canvas will edit it later.
    adopt(await flowApi.update(id, { name: name.value, description: description.value, definition: flow.value.definition }))
    saved.value = true
  } catch (e) {
    error.value = message(e)
  } finally {
    saving.value = false
  }
}

async function toggle() {
  if (!flow.value) return
  toggling.value = true
  error.value = null
  try {
    const updated = await (flow.value.enabled ? flowApi.disable(id) : flowApi.enable(id))
    flow.value = { ...flow.value, enabled: updated.enabled, updatedAt: updated.updatedAt }
  } catch (e) {
    error.value = message(e)
  } finally {
    toggling.value = false
  }
}

async function confirmDelete(typed: string) {
  deleteBusy.value = true
  deleteError.value = null
  try {
    await flowApi.remove(id, typed)
    await router.push({ name: 'flows' })
  } catch (e) {
    deleteError.value = message(e)
  } finally {
    deleteBusy.value = false
  }
}
</script>

<template>
  <section class="mx-auto max-w-xl px-4 py-8" data-testid="flow-edit">
    <RouterLink :to="{ name: 'flows' }" class="mb-4 inline-flex items-center gap-1.5 text-xs font-semibold text-ink-soft hover:text-ink" data-testid="back">
      <AppIcon name="arrow-left" :size="14" /> Flows
    </RouterLink>

    <p v-if="loading" class="flex items-center gap-2 text-muted" data-testid="edit-loading">
      <AppIcon name="loader" :size="15" class="animate-spin" /> Loading
    </p>

    <p v-else-if="!flow" class="flex items-center gap-1.5 rounded-control border-2 border-dashed border-ink p-2.5 text-xs font-semibold" data-testid="edit-error">
      <AppIcon name="alert" :size="14" /> {{ error }}
    </p>

    <form v-else @submit.prevent="save">
      <div class="mb-4 flex items-center gap-3">
        <h1 class="flex items-center gap-2 text-2xl font-bold tracking-tight"><AppIcon name="edit" :size="22" /> Edit flow</h1>
        <span class="flex-1" />
        <FlowSwitch :enabled="flow.enabled" :busy="toggling" testid="edit-toggle" @toggle="toggle" />
      </div>

      <div class="space-y-3">
        <IconField v-model="name" label="Name" icon="git-branch" :maxlength="100" testid="edit-name" :invalid="!valid" />
        <IconField v-model="description" label="Description" icon="edit" :maxlength="500" testid="edit-description" />
      </div>

      <div class="mt-4 rounded-card border border-line p-3.5 text-xs text-ink-soft" data-testid="edit-definition">
        <div class="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="layers" :size="13" /> Cards</div>
        <p class="flex items-center gap-4">
          <span data-testid="edit-card-count">{{ countCards(flow.definition) }} cards</span>
          <span>{{ countConnections(flow.definition) }} connections</span>
        </p>
        <p class="mt-1 text-muted">The card canvas is coming. Updated {{ formatWhen(flow.updatedAt) }}.</p>
      </div>

      <p v-if="error" class="mt-3 flex items-center gap-1.5 text-xs font-semibold" data-testid="edit-save-error"><AppIcon name="alert" :size="14" /> {{ error }}</p>
      <p v-else-if="saved && !dirty" class="mt-3 flex items-center gap-1.5 text-xs font-semibold" data-testid="edit-saved"><AppIcon name="check-circle" :size="14" /> Saved</p>

      <div class="mt-5 flex items-center gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border-2 border-ink px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="edit-delete" @click="deleteError = null; confirmingDelete = true">
          <AppIcon name="trash" :size="15" /> Delete
        </button>
        <span class="flex-1" />
        <button type="submit" :disabled="!dirty || !valid || saving" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="edit-save">
          <AppIcon :name="saving ? 'loader' : 'save'" :size="15" :class="saving ? 'animate-spin' : ''" /> Save
        </button>
      </div>
    </form>

    <DeleteFlowDialog v-if="confirmingDelete && flow" :flow-name="flow.name" :busy="deleteBusy" :error="deleteError" @confirm="confirmDelete" @cancel="confirmingDelete = false" />
  </section>
</template>
