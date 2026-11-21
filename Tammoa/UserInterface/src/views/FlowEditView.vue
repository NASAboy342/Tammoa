<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import FlowSwitch from '@/components/FlowSwitch.vue'
import DeleteFlowDialog from '@/components/DeleteFlowDialog.vue'
import FlowCanvas, { type Selection } from '@/components/canvas/FlowCanvas.vue'
import CardPalette from '@/components/canvas/CardPalette.vue'
import CardInspector, { type InspectorTab } from '@/components/canvas/CardInspector.vue'
import RunPanel from '@/components/canvas/RunPanel.vue'
import { flowApi, type Flow } from '@/services/flowApi'
import type { GuideExample } from '@/services/cardApi'
import type { NodeRunStatus } from '@/services/runApi'
import { useCardTypeStore } from '@/stores/cardTypeStore'
import { useRunStore } from '@/stores/runStore'
import { canConnect, freeSpot, hasTrigger, makeNode, newId, removeEdge, removeNode, toDefinition, toDraft, type Draft } from '@/helpers/flowDraft'
import { parseRunInput, pickNodeRun } from '@/helpers/runHelpers'
import { SIDE_DEFAULT, SIDE_MAX, SIDE_MIN, clampSideWidth, widthAfterDrag } from '@/helpers/layout'

const route = useRoute()
const router = useRouter()
const id = String(route.params.id)
const cards = useCardTypeStore()
const runs = useRunStore()

const flow = ref<Flow | null>(null)
const name = ref('')
const description = ref('')
const draft = reactive<Draft>({ nodes: [], edges: [] })
const savedSnapshot = ref('')
const selection = ref<Selection | null>(null)
const canvas = ref<InstanceType<typeof FlowCanvas> | null>(null)

const loading = ref(true)
const saving = ref(false)
const toggling = ref(false)
const error = ref<string | null>(null)
const notice = ref<string | null>(null)
const saved = ref(false)
const confirmingDelete = ref(false)
const deleteBusy = ref(false)
const deleteError = ref<string | null>(null)
const runInput = ref('')
const chosenRunId = ref<string | null>(null)
let noticeTimer: ReturnType<typeof setTimeout> | undefined
// ---- resizable side panel (drag the handle, arrow keys, double-click to reset); the width is remembered
const WIDTH_KEY = 'tammoa-side-width'
function storedWidth(): number {
  try {
    const v = Number(localStorage.getItem(WIDTH_KEY))
    return v > 0 ? clampSideWidth(v, window.innerWidth) : SIDE_DEFAULT
  } catch {
    return SIDE_DEFAULT
  }
}
const sideWidth = ref(storedWidth())
function saveWidth() {
  try {
    localStorage.setItem(WIDTH_KEY, String(sideWidth.value))
  } catch {
    /* private mode: the width simply is not remembered */
  }
}
function startResize(e: PointerEvent) {
  const startX = e.clientX
  const startWidth = sideWidth.value
  const move = (m: PointerEvent) => (sideWidth.value = clampSideWidth(widthAfterDrag(startWidth, startX, m.clientX), window.innerWidth))
  const up = () => {
    window.removeEventListener('pointermove', move)
    window.removeEventListener('pointerup', up)
    saveWidth()
  }
  window.addEventListener('pointermove', move)
  window.addEventListener('pointerup', up)
}
function onResizeKey(e: KeyboardEvent) {
  const step = e.shiftKey ? 80 : 24
  if (e.key === 'ArrowLeft') sideWidth.value = clampSideWidth(sideWidth.value + step, window.innerWidth)
  else if (e.key === 'ArrowRight') sideWidth.value = clampSideWidth(sideWidth.value - step, window.innerWidth)
  else if (e.key === 'Home') sideWidth.value = SIDE_DEFAULT
  else return
  e.preventDefault()
  saveWidth()
}
function resetWidth() {
  sideWidth.value = SIDE_DEFAULT
  saveWidth()
}
const keepWidthInWindow = () => (sideWidth.value = clampSideWidth(sideWidth.value, window.innerWidth))
window.addEventListener('resize', keepWidthInWindow)

const now = ref(Date.now()) // ticks so the duration of a running run keeps counting
const clock = setInterval(() => (now.value = Date.now()), 500)

const snapshot = () => JSON.stringify(draft)
const dirty = computed(() => !!flow.value && (name.value !== flow.value.name || description.value !== flow.value.description || snapshot() !== savedSnapshot.value))
const valid = computed(() => name.value.trim().length > 0)
const triggerPresent = computed(() => hasTrigger(draft, cards.byType))
const canRun = computed(() => !dirty.value && triggerPresent.value && !saving.value)
const runTip = computed(() => (!triggerPresent.value ? 'Add a trigger card first' : dirty.value ? 'Save before running' : 'Run the flow'))
const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

const flowRuns = computed(() => runs.forFlow(id))
const activeRun = computed(() => flowRuns.value.find((r) => r.id === chosenRunId.value) ?? flowRuns.value[0] ?? null)
const nodeStates = computed(() => {
  const states: Record<string, NodeRunStatus> = {}
  for (const n of activeRun.value?.nodes ?? []) states[n.nodeId] = n.status
  return states
})
const selectedNode = computed(() => (selection.value?.kind === 'node' ? draft.nodes.find((n) => n.id === selection.value!.id) ?? null : null))
const selectedNodeRun = computed(() => activeRun.value?.nodes.find((n) => n.nodeId === selectedNode.value?.id))
// the data tabs show the selected run's data for this card, or else the newest run in which it actually ran
const nodeData = computed(() => (selectedNode.value ? pickNodeRun(flowRuns.value, activeRun.value?.id ?? null, selectedNode.value.id) : null))
const inspectorRunInput = computed(() => nodeData.value?.run.input ?? activeRun.value?.input)
const inspectorTab = ref<InspectorTab>('settings') // remembered while you click from card to card
const incoming = computed(() =>
  selectedNode.value
    ? draft.edges
        .filter((e) => e.target === selectedNode.value!.id)
        .map((e) => draft.nodes.find((n) => n.id === e.source))
        .filter((n): n is NonNullable<typeof n> => !!n)
        .map((n) => ({ name: n.name, info: cards.byType[n.type] }))
    : [],
)

function applyExample(example: GuideExample) {
  for (const [key, value] of Object.entries(example.params ?? {})) setParam(key, value)
  inspectorTab.value = 'settings'
  say(`Example applied: "${example.title}". Edit it if you like, then Save and Run.`)
}

function adopt(f: Flow) {
  flow.value = f
  name.value = f.name
  description.value = f.description
  const d = toDraft(f.definition)
  draft.nodes = d.nodes
  draft.edges = d.edges
  savedSnapshot.value = snapshot()
  if (selection.value && !draft.nodes.some((n) => n.id === selection.value!.id) && !draft.edges.some((e) => e.id === selection.value!.id)) selection.value = null
}

function say(text: string) {
  notice.value = text
  clearTimeout(noticeTimer)
  noticeTimer = setTimeout(() => (notice.value = null), 3500)
}

onMounted(async () => {
  void cards.load()
  void runs.loadForFlow(id)
  try {
    adopt(await flowApi.get(id))
  } catch (e) {
    error.value = message(e)
  } finally {
    loading.value = false
  }
})
onUnmounted(() => {
  clearTimeout(noticeTimer)
  clearInterval(clock)
  window.removeEventListener('resize', keepWidthInWindow)
})

function addCard(type: string) {
  const info = cards.byType[type]
  if (!info) return
  const base = canvas.value?.centerWorld() ?? { x: 0, y: 0 }
  const node = makeNode(info, draft.nodes, freeSpot(draft.nodes, base))
  draft.nodes.push(node)
  selection.value = { kind: 'node', id: node.id }
}

function moveNode(nodeId: string, x: number, y: number) {
  const n = draft.nodes.find((i) => i.id === nodeId)
  if (n) n.position = { x, y }
}

function connect(source: string, target: string, handle: string) {
  const result = canConnect(draft, cards.byType, source, target, handle)
  if (!result.ok) return say(result.reason)
  draft.edges.push({ id: newId('e'), source, sourceHandle: handle, target })
  selection.value = { kind: 'edge', id: draft.edges[draft.edges.length - 1].id }
}

function removeSelection() {
  const s = selection.value
  if (!s) return
  const next = s.kind === 'node' ? removeNode(draft, s.id) : removeEdge(draft, s.id)
  draft.nodes = next.nodes
  draft.edges = next.edges
  selection.value = null
}

function setParam(key: string, value: unknown) {
  if (selectedNode.value) selectedNode.value.params = { ...selectedNode.value.params, [key]: value }
}

async function save() {
  if (!flow.value || !valid.value || saving.value) return
  saving.value = true
  error.value = null
  saved.value = false
  try {
    adopt(await flowApi.update(id, { name: name.value, description: description.value, definition: toDefinition(flow.value.definition, draft) }))
    saved.value = true
  } catch (e) {
    error.value = message(e)
  } finally {
    saving.value = false
  }
}

async function run() {
  if (!canRun.value) return
  try {
    const started = await runs.start(id, parseRunInput(runInput.value))
    chosenRunId.value = started.id
  } catch {
    /* the store keeps the message */
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

const inputCls =
  'min-h-9 w-full rounded-control border border-line-strong bg-surface py-1.5 pl-9 pr-3 text-sm outline-none placeholder:text-muted focus:border-ink focus:ring-1 focus:ring-ink aria-[invalid=true]:border-2 aria-[invalid=true]:border-dashed aria-[invalid=true]:border-ink'
</script>

<template>
  <section class="flex h-full flex-col" data-testid="flow-edit">
    <p v-if="loading" class="flex items-center gap-2 p-6 text-muted" data-testid="edit-loading">
      <AppIcon name="loader" :size="15" class="animate-spin" /> Loading
    </p>

    <div v-else-if="!flow" class="p-6">
      <RouterLink :to="{ name: 'flows' }" class="mb-4 inline-flex items-center gap-1.5 text-xs font-semibold text-ink-soft hover:text-ink" data-testid="back">
        <AppIcon name="arrow-left" :size="14" /> Flows
      </RouterLink>
      <p class="flex items-center gap-1.5 rounded-control border-2 border-dashed border-ink p-2.5 text-xs font-semibold" data-testid="edit-error">
        <AppIcon name="alert" :size="14" /> {{ error }}
      </p>
    </div>

    <template v-else>
      <form class="space-y-2 border-b border-line px-4 py-2.5" @submit.prevent="save">
        <div class="flex flex-wrap items-center gap-2">
          <RouterLink :to="{ name: 'flows' }" class="inline-flex min-h-9 items-center gap-1.5 text-xs font-semibold text-ink-soft hover:text-ink" data-testid="back">
            <AppIcon name="arrow-left" :size="14" /> Flows
          </RouterLink>
          <span class="relative block min-w-48 max-w-sm flex-1">
            <AppIcon name="git-branch" :size="15" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <input v-model="name" type="text" maxlength="100" aria-label="Flow name" placeholder="Flow name" :aria-invalid="!valid ? 'true' : undefined" :class="[inputCls, 'font-semibold']" data-testid="edit-name" autocomplete="off" />
          </span>
          <span class="relative block min-w-48 flex-1">
            <AppIcon name="edit" :size="15" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <input v-model="description" type="text" maxlength="500" aria-label="Description" placeholder="Description" :class="inputCls" data-testid="edit-description" autocomplete="off" />
          </span>
          <FlowSwitch :enabled="flow.enabled" :busy="toggling" testid="edit-toggle" @toggle="toggle" />
          <button type="button" class="inline-flex h-9 w-9 items-center justify-center rounded-control border-2 border-ink hover:bg-surface-hover" title="Delete flow" aria-label="Delete flow" data-testid="edit-delete" @click="deleteError = null; confirmingDelete = true">
            <AppIcon name="trash" :size="15" />
          </button>
          <button type="submit" :disabled="!dirty || !valid || saving" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="edit-save">
            <AppIcon :name="saving ? 'loader' : 'save'" :size="15" :class="saving ? 'animate-spin' : ''" /> Save
          </button>
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <CardPalette :types="cards.types" :icon-for="cards.iconFor" :loading="cards.loading" @add="addCard" />
          <span class="flex-1" />
          <span class="relative block w-56">
            <AppIcon name="zap" :size="15" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <input v-model="runInput" type="text" aria-label="Run input" placeholder="Run input (text or JSON)" :class="inputCls" data-testid="run-input" autocomplete="off" @keydown.enter.prevent="run" />
          </span>
          <button type="button" :disabled="!canRun" :title="runTip" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="run" @click="run">
            <AppIcon name="play" :size="15" /> Run
          </button>
        </div>

        <p v-if="error" class="flex items-center gap-1.5 text-xs font-semibold" data-testid="edit-save-error"><AppIcon name="alert" :size="14" /> {{ error }}</p>
        <p v-else-if="runs.error" class="flex items-center gap-1.5 text-xs font-semibold" data-testid="run-error"><AppIcon name="alert" :size="14" /> {{ runs.error }}</p>
        <p v-else-if="saved && !dirty" class="flex items-center gap-1.5 text-xs font-semibold" data-testid="edit-saved"><AppIcon name="check-circle" :size="14" /> Saved</p>
      </form>

      <div class="flex min-h-0 flex-1">
        <div class="relative min-w-0 flex-1">
          <FlowCanvas
            ref="canvas"
            :nodes="draft.nodes"
            :edges="draft.edges"
            :types="cards.byType"
            :icon-for="cards.iconFor"
            :states="nodeStates"
            :selection="selection"
            @move="moveNode"
            @select="selection = $event"
            @connect="connect"
            @remove="removeSelection"
          />
          <p v-if="notice" class="pointer-events-none absolute left-1/2 top-3 flex -translate-x-1/2 items-center gap-1.5 rounded-control border-2 border-dashed border-ink bg-surface px-3 py-1.5 text-xs font-semibold" data-testid="canvas-notice">
            <AppIcon name="info" :size="14" /> {{ notice }}
          </p>
          <button
            v-if="selection?.kind === 'edge'"
            type="button"
            class="absolute right-3 top-3 inline-flex min-h-8 items-center gap-1.5 rounded-control border-2 border-ink bg-surface px-2.5 text-[11px] font-semibold hover:bg-surface-hover"
            data-testid="edge-delete"
            @click="removeSelection"
          >
            <AppIcon name="trash" :size="13" /> Remove connection
          </button>
        </div>

        <div
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize the side panel"
          :aria-valuenow="sideWidth"
          :aria-valuemin="SIDE_MIN"
          :aria-valuemax="SIDE_MAX"
          tabindex="0"
          title="Drag to resize (double-click to reset)"
          class="hidden w-2.5 shrink-0 cursor-col-resize items-center justify-center border-l border-line hover:bg-surface-hover focus-visible:bg-surface-hover md:flex"
          data-icon-exempt="drag handle, not a button"
          data-testid="side-resizer"
          @pointerdown.prevent="startResize"
          @keydown="onResizeKey"
          @dblclick="resetWidth"
        >
          <AppIcon name="grip-vertical" :size="12" class="text-muted" />
        </div>
        <aside class="hidden shrink-0 space-y-5 overflow-auto p-4 md:block" :style="{ width: `${sideWidth}px` }" data-testid="side-panel">
          <p v-if="activeRun?.error" class="flex items-start gap-1.5 rounded-control border-2 border-dashed border-ink p-2 text-xs font-semibold" data-testid="run-failure"><AppIcon name="x-circle" :size="14" class="mt-0.5" /> {{ activeRun.error }}</p>
          <CardInspector
            v-if="selectedNode"
            :key="selectedNode.id"
            :node="selectedNode"
            :info="cards.byType[selectedNode.type]"
            :icon="cards.iconFor(selectedNode.type)"
            :run="selectedNodeRun"
            :data="nodeData"
            :run-input="inspectorRunInput"
            :incoming="incoming"
            v-model:tab="inspectorTab"
            :now="now"
            @apply="applyExample"
            @rename="selectedNode.name = $event"
            @param="setParam"
            @remove="removeSelection"
          />
          <p v-else class="flex items-center gap-1.5 text-xs text-muted" data-testid="inspector-hint"><AppIcon name="info" :size="13" /> Select a card to edit it</p>
          <RunPanel :runs="flowRuns" :active-id="activeRun?.id ?? null" :now="now" @select="chosenRunId = $event" @cancel="runs.cancel($event)" @remove="runs.remove($event)" @clear="runs.clear(id)" />
        </aside>
      </div>
    </template>

    <DeleteFlowDialog v-if="confirmingDelete && flow" :flow-name="flow.name" :busy="deleteBusy" :error="deleteError" @confirm="confirmDelete" @cancel="confirmingDelete = false" />
  </section>
</template>
