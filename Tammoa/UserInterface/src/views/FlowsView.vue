<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import FlowSwitch from '@/components/FlowSwitch.vue'
import FlowFormDialog from '@/components/FlowFormDialog.vue'
import DeleteFlowDialog from '@/components/DeleteFlowDialog.vue'
import TemplateDialog from '@/components/TemplateDialog.vue'
import { useFlowStore } from '@/stores/flowStore'
import { formatWhen } from '@/helpers/flowHelpers'
import type { FlowSummary } from '@/services/flowApi'

const store = useFlowStore()
const router = useRouter()

const creating = ref(false)
const dialogError = ref<string | null>(null)
const dialogBusy = ref(false)
const toDelete = ref<FlowSummary | null>(null)
const picking = ref(false)

onMounted(store.load)

function openCreate() {
  dialogError.value = null
  creating.value = true
}

async function templateCreated(flow: { id: string; name: string }) {
  picking.value = false
  await store.load()
  await router.push({ name: 'flow-edit', params: { id: flow.id } })
}

async function submitCreate(name: string, description: string) {
  dialogBusy.value = true
  dialogError.value = null
  try {
    const flow = await store.create(name, description)
    creating.value = false
    await router.push({ name: 'flow-edit', params: { id: flow.id } })
  } catch (e) {
    dialogError.value = e instanceof Error ? e.message : String(e)
  } finally {
    dialogBusy.value = false
  }
}

function openDelete(flow: FlowSummary) {
  dialogError.value = null
  toDelete.value = flow
}

async function confirmDelete(typed: string) {
  if (!toDelete.value) return
  dialogBusy.value = true
  dialogError.value = null
  try {
    await store.remove(toDelete.value.id, typed)
    toDelete.value = null
  } catch (e) {
    dialogError.value = e instanceof Error ? e.message : String(e)
  } finally {
    dialogBusy.value = false
  }
}

const iconBtn =
  'inline-flex h-7 w-7 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover disabled:opacity-40'
</script>

<template>
  <section class="mx-auto max-w-3xl px-4 py-8" data-testid="flows">
    <div class="mb-5 flex items-center gap-3">
      <h1 class="flex items-center gap-2 text-2xl font-bold tracking-tight"><AppIcon name="git-branch" :size="22" /> Flows</h1>
      <span class="flex-1" />
      <button
        type="button"
        class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover"
        data-testid="from-template"
        @click="picking = true"
      >
        <AppIcon name="layers" :size="15" /> From a template
      </button>
      <button
        type="button"
        class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90"
        data-testid="new-flow"
        @click="openCreate"
      >
        <AppIcon name="plus" :size="15" /> New flow
      </button>
    </div>

    <p v-if="store.error && !creating && !toDelete" class="mb-3 flex items-center gap-1.5 rounded-control border-2 border-dashed border-ink p-2.5 text-xs font-semibold" data-testid="flows-error">
      <AppIcon name="alert" :size="14" /> {{ store.error }}
      <button type="button" :class="[iconBtn, 'ml-auto']" title="Retry" aria-label="Retry" data-testid="flows-retry" @click="store.load()">
        <AppIcon name="refresh" :size="14" />
      </button>
    </p>

    <p v-if="store.loading && !store.flows.length" class="flex items-center gap-2 text-muted" data-testid="flows-loading">
      <AppIcon name="loader" :size="15" class="animate-spin" /> Loading flows
    </p>

    <div v-else-if="!store.flows.length && !store.error" class="rounded-card border border-dashed border-line-strong p-8" data-testid="flows-empty">
      <div class="text-center">
        <AppIcon name="git-branch" :size="40" class="mx-auto text-muted" />
        <p class="mt-2 text-lg font-bold tracking-tight" data-testid="welcome-title">Welcome to Tammoa</p>
        <p class="mx-auto mt-1 max-w-md text-xs text-ink-soft">
          Build automations from <strong>cards</strong> joined by lines: a script, a web request, a question to an AI model. Nothing here yet, so the quickest start is a ready-made flow.
        </p>
      </div>
      <ol class="mx-auto mt-5 max-w-md space-y-2 text-xs text-ink-soft" data-testid="welcome-steps">
        <li class="flex items-start gap-2"><AppIcon name="layers" :size="14" class="mt-0.5 shrink-0" /> <span><strong>Pick a template.</strong> "Hello, flow" needs nothing: no AI, no setup.</span></li>
        <li class="flex items-start gap-2"><AppIcon name="play" :size="14" class="mt-0.5 shrink-0" /> <span><strong>Press Run</strong> in the flow and watch each card finish. Click a card to see what it received and produced.</span></li>
        <li class="flex items-start gap-2"><AppIcon name="plug" :size="14" class="mt-0.5 shrink-0" /> <span>For AI cards, add your Ollama server under <RouterLink :to="{ name: 'connections' }" class="inline-flex items-center gap-1 font-semibold underline" data-testid="welcome-connections"><AppIcon name="plug" :size="11" /> Connections</RouterLink> first.</span></li>
      </ol>
      <div class="mt-5 flex flex-wrap justify-center gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90" data-testid="welcome-template" @click="picking = true">
          <AppIcon name="layers" :size="15" /> Start from a template
        </button>
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="welcome-new" @click="openCreate">
          <AppIcon name="plus" :size="15" /> Empty flow
        </button>
      </div>
    </div>

    <ul v-else class="space-y-2" data-testid="flow-list">
      <li
        v-for="flow in store.flows"
        :key="flow.id"
        class="flex flex-wrap items-center gap-3 rounded-card border border-line p-3.5"
        :data-testid="`flow-row-${flow.id}`"
      >
        <AppIcon name="git-branch" :size="18" class="text-muted" />
        <div class="min-w-0 flex-1 basis-48">
          <RouterLink
            :to="{ name: 'flow-edit', params: { id: flow.id } }"
            class="inline-flex max-w-full items-center gap-1.5 truncate font-semibold hover:underline"
            :data-testid="`flow-open-${flow.id}`"
          >
            <AppIcon name="edit" :size="13" /> <span class="truncate" :title="flow.name" data-testid="flow-name">{{ flow.name }}</span>
          </RouterLink>
          <p v-if="flow.description" class="truncate text-xs text-ink-soft" :title="flow.description">{{ flow.description }}</p>
          <p class="mt-0.5 flex flex-wrap items-center gap-x-3 text-[11px] text-muted">
            <span class="inline-flex items-center gap-1 whitespace-nowrap"><AppIcon name="layers" :size="11" /> {{ flow.cardCount }} cards</span>
            <span class="inline-flex items-center gap-1 whitespace-nowrap"><AppIcon name="clock" :size="11" /> {{ formatWhen(flow.updatedAt) }}</span>
          </p>
        </div>
        <FlowSwitch
          :enabled="flow.enabled"
          :busy="store.busyId === flow.id"
          :testid="`flow-toggle-${flow.id}`"
          @toggle="store.setEnabled(flow.id, !flow.enabled).catch(() => {})"
        />
        <button type="button" :class="iconBtn" title="Duplicate flow" aria-label="Duplicate flow" :disabled="store.busyId === flow.id" :data-testid="`flow-duplicate-${flow.id}`" @click="store.duplicate(flow.id).catch(() => {})">
          <AppIcon name="copy" :size="14" />
        </button>
        <button type="button" :class="iconBtn" title="Delete flow" aria-label="Delete flow" :disabled="store.busyId === flow.id" :data-testid="`flow-delete-${flow.id}`" @click="openDelete(flow)">
          <AppIcon name="trash" :size="14" />
        </button>
      </li>
    </ul>

    <TemplateDialog v-if="picking" @created="templateCreated" @cancel="picking = false" />
    <FlowFormDialog v-if="creating" :busy="dialogBusy" :error="dialogError" @submit="submitCreate" @cancel="creating = false" />
    <DeleteFlowDialog v-if="toDelete" :flow-name="toDelete.name" :busy="dialogBusy" :error="dialogError" @confirm="confirmDelete" @cancel="toDelete = null" />
  </section>
</template>
