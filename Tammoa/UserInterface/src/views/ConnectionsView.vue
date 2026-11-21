<script setup lang="ts">
import { onMounted, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import ConnectionFormDialog from '@/components/ConnectionFormDialog.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ExpandableText from '@/components/ExpandableText.vue'
import { useConnectionStore } from '@/stores/connectionStore'
import type { ConnectionInfo, SaveConnection } from '@/services/connectionApi'

const store = useConnectionStore()
const editing = ref<ConnectionInfo | null>(null)
const creating = ref(false)
const toDelete = ref<ConnectionInfo | null>(null)
const busy = ref(false)
const dialogError = ref<string | null>(null)
const providerName = (id: string) => store.providers.find((p) => p.provider === id)?.name ?? id

onMounted(store.load)

function openCreate() {
  dialogError.value = null
  creating.value = true
}
function openEdit(c: ConnectionInfo) {
  dialogError.value = null
  editing.value = c
}

async function save(value: SaveConnection) {
  busy.value = true
  dialogError.value = null
  try {
    if (editing.value) await store.update(editing.value.id, value)
    else {
      const created = await store.create(value)
      void store.test(created.id) // show right away whether it works
    }
    creating.value = false
    editing.value = null
  } catch (e) {
    dialogError.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

async function confirmDelete() {
  if (!toDelete.value) return
  busy.value = true
  dialogError.value = null
  try {
    await store.remove(toDelete.value.id)
    toDelete.value = null
  } catch (e) {
    dialogError.value = e instanceof Error ? e.message : String(e)
  } finally {
    busy.value = false
  }
}

const iconBtn = 'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover disabled:opacity-40'
</script>

<template>
  <section class="mx-auto max-w-3xl px-4 py-8" data-testid="connections">
    <div class="mb-1 flex items-center gap-3">
      <h1 class="flex items-center gap-2 text-2xl font-bold tracking-tight"><AppIcon name="plug" :size="22" /> Connections</h1>
      <span class="flex-1" />
      <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90" data-testid="new-connection" @click="openCreate">
        <AppIcon name="plus" :size="15" /> New connection
      </button>
    </div>
    <p class="mb-5 text-xs text-muted">Addresses and keys of the AI services your flows use. Cards pick a connection by name; the key itself never leaves this computer.</p>

    <p v-if="store.error" class="mb-3 flex items-center gap-1.5 rounded-control border-2 border-dashed border-ink p-2.5 text-xs font-semibold" data-testid="connections-error">
      <AppIcon name="alert" :size="14" /> {{ store.error }}
      <button type="button" :class="[iconBtn, 'ml-auto']" title="Retry" aria-label="Retry" @click="store.load()"><AppIcon name="refresh" :size="14" /></button>
    </p>
    <p v-if="store.loading && !store.connections.length" class="flex items-center gap-2 text-muted"><AppIcon name="loader" :size="15" class="animate-spin" /> Loading</p>

    <div v-else-if="!store.connections.length && !store.error" class="rounded-card border border-dashed border-line-strong p-10 text-center" data-testid="connections-empty">
      <AppIcon name="plug" :size="40" class="mx-auto text-muted" />
      <p class="mt-2 text-ink-soft">No connections yet</p>
      <p class="text-xs text-muted">Add your Ollama server to use AI cards.</p>
    </div>

    <ul v-else class="space-y-2" data-testid="connection-list">
      <li v-for="c in store.connections" :key="c.id" class="rounded-card border border-line p-3.5" :data-testid="`connection-row-${c.id}`">
        <div class="flex flex-wrap items-center gap-3">
          <AppIcon name="plug" :size="18" class="text-muted" />
          <div class="min-w-0 flex-1 basis-48">
            <p class="truncate font-semibold" :title="c.name" data-testid="connection-name">{{ c.name }}</p>
            <p class="flex flex-wrap items-center gap-x-3 text-[11px] text-muted">
              <span class="inline-flex items-center gap-1"><AppIcon name="bot" :size="11" /> {{ providerName(c.provider) }}</span>
              <span class="inline-flex min-w-0 items-center gap-1"><AppIcon name="globe" :size="11" /> <span class="truncate" :title="c.baseUrl">{{ c.baseUrl }}</span></span>
              <span class="inline-flex items-center gap-1" data-testid="connection-key-state"><AppIcon name="key" :size="11" /> {{ c.hasKey ? 'Key saved' : 'No key' }}</span>
            </p>
          </div>
          <button
            type="button"
            class="inline-flex min-h-7 items-center gap-1.5 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover disabled:opacity-40"
            :disabled="store.tests[c.id]?.testing"
            :data-testid="`connection-test-${c.id}`"
            @click="store.test(c.id)"
          >
            <AppIcon :name="store.tests[c.id]?.testing ? 'loader' : 'activity'" :size="13" :class="store.tests[c.id]?.testing ? 'animate-spin' : ''" /> Test
          </button>
          <button type="button" :class="iconBtn" title="Edit connection" aria-label="Edit connection" :data-testid="`connection-edit-${c.id}`" @click="openEdit(c)"><AppIcon name="edit" :size="14" /></button>
          <button type="button" :class="iconBtn" title="Delete connection" aria-label="Delete connection" :data-testid="`connection-delete-${c.id}`" @click="toDelete = c; dialogError = null"><AppIcon name="trash" :size="14" /></button>
        </div>

        <div v-if="store.tests[c.id] && !store.tests[c.id].testing" class="mt-2.5 rounded-control border p-2.5 text-xs" :class="store.tests[c.id].ok ? 'border-line-strong' : 'border-2 border-dashed border-ink'" :data-testid="`connection-result-${c.id}`">
          <p v-if="store.tests[c.id].ok" class="flex items-center gap-1.5 font-semibold">
            <AppIcon name="check-circle" :size="14" /> Connected: {{ providerName(c.provider) }} {{ store.tests[c.id].version }} · {{ store.tests[c.id].models.length }} model{{ store.tests[c.id].models.length === 1 ? '' : 's' }}
          </p>
          <ExpandableText v-if="store.tests[c.id].ok && store.tests[c.id].models.length" :text="store.tests[c.id].models.join('  ·  ')" :lines="2" />
          <p v-if="!store.tests[c.id].ok" class="flex items-start gap-1.5 font-semibold"><AppIcon name="x-circle" :size="14" class="mt-0.5 shrink-0" /> <span class="break-words">{{ store.tests[c.id].error }}</span></p>
        </div>
      </li>
    </ul>

    <ConnectionFormDialog v-if="creating" :providers="store.providers" :busy="busy" :error="dialogError" @submit="save" @cancel="creating = false" />
    <ConnectionFormDialog v-if="editing" :providers="store.providers" :existing="editing" :busy="busy" :error="dialogError" @submit="save" @cancel="editing = null" />
    <ConfirmDialog
      v-if="toDelete"
      title="Delete connection"
      :message="`Delete '${toDelete.name}'? Its saved key is removed too. Cards that use it must be changed first.`"
      confirm-label="Delete"
      confirm-icon="trash"
      danger
      :busy="busy"
      :error="dialogError"
      @confirm="confirmDelete"
      @cancel="toDelete = null"
    />
  </section>
</template>
