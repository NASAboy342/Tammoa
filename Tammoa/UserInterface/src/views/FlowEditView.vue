<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import FlowSwitch from '@/components/FlowSwitch.vue'
import DeleteFlowDialog from '@/components/DeleteFlowDialog.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import FlowCanvas, { type Move, type Selection } from '@/components/canvas/FlowCanvas.vue'
import CardPalette from '@/components/canvas/CardPalette.vue'
import CardInspector, { type InspectorTab } from '@/components/canvas/CardInspector.vue'
import RunPanel from '@/components/canvas/RunPanel.vue'
import { flowApi, type Flow } from '@/services/flowApi'
import type { GuideExample } from '@/services/cardApi'
import type { NodeRunStatus } from '@/services/runApi'
import { useCardTypeStore } from '@/stores/cardTypeStore'
import { useRunStore } from '@/stores/runStore'
import { canConnect, freeSpot, hasTrigger, makeNode, newId, removeEdge, removeNodes, toDefinition, toDraft, type Draft } from '@/helpers/flowDraft'
import { connectionsTouching, copyCards, copyGroup, groupBox, groupNodes, renameGroup, removeGroupWithCards, ungroup } from '@/helpers/groups'
import { sizeOf } from '@/helpers/cardSize'
import { parseRunInput, pickNodeRun } from '@/helpers/runHelpers'
import { findLoops, loopHasCondition } from '@/helpers/loops'
import { SIDE_DEFAULT, SIDE_MAX, SIDE_MIN, clampSideWidth, widthAfterDrag } from '@/helpers/layout'

const route = useRoute()
const router = useRouter()
const id = String(route.params.id)
const cards = useCardTypeStore()
const runs = useRunStore()

const flow = ref<Flow | null>(null)
const name = ref('')
const description = ref('')
const draft = reactive<Draft>({ nodes: [], edges: [], groups: [] })
const savedSnapshot = ref('')
const selection = ref<Selection | null>(null)
const selectedIds = ref<string[]>([]) // every selected card: one, several (Shift-click, rectangle) or the cards of the selected group
const pendingDelete = ref<{ ids: string[]; groupId?: string; title: string; message: string } | null>(null)
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
// how many times each card ran in the shown run (a card in a loop runs several times)
const rounds = computed(() => {
  const r: Record<string, number> = {}
  for (const n of activeRun.value?.nodes ?? []) if ((n.iteration ?? 1) > 1) r[n.nodeId] = n.iteration!
  return r
})
// loops in the drawn flow: the limit field only shows when there is one, and a loop with nothing to end it gets a hint
const loops = computed(() => findLoops(draft))
const loopsWithoutCondition = computed(() => loops.value.loops.filter((l) => !loopHasCondition(l, draft, cards.byType)))
const loopLimit = computed({
  get: () => draft.loopLimit ?? '',
  set: (v: number | string) => {
    const n = typeof v === 'number' ? v : Number(v)
    draft.loopLimit = v === '' || !Number.isFinite(n) ? undefined : Math.min(1000, Math.max(1, Math.round(n)))
  },
})

const sizeInView = (n: Draft['nodes'][number]) => sizeOf(n, cards.byType[n.type]?.outputs?.length ?? 1)
const selectedGroup = computed(() => (selection.value?.kind === 'group' ? draft.groups.find((g) => g.id === selection.value!.id) ?? null : null))
const hasSelection = computed(() => selectedIds.value.length > 0)
const selectionLabel = computed(() => {
  const n = selectedIds.value.length
  const cardsText = `${n} card${n === 1 ? '' : 's'}`
  return selectedGroup.value ? `Group · ${cardsText}` : cardsText
})

const selectedNode = computed(() => (selection.value?.kind === 'node' ? draft.nodes.find((n) => n.id === selection.value!.id) ?? null : null))
const selectedNodeRun = computed(() => activeRun.value?.nodes.find((n) => n.nodeId === selectedNode.value?.id))
// the data tabs show the selected run's data for this card, or else the newest run in which it actually ran
const nodeData = computed(() => (selectedNode.value ? pickNodeRun(flowRuns.value, activeRun.value?.id ?? null, selectedNode.value.id) : null))
// a Data card keeps its text through the backend, which looks the card up in the SAVED flow
const savedIds = computed(() => new Set(((flow.value?.definition.nodes ?? []) as { id: string }[]).map((n) => n.id)))
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
  draft.groups = d.groups
  draft.loopLimit = d.loopLimit // the flow's loop limit travels with the draft (undefined = the engine's default)
  savedSnapshot.value = snapshot()
  if (selection.value && !draft.nodes.some((n) => n.id === selection.value!.id) && !draft.edges.some((e) => e.id === selection.value!.id) && !draft.groups.some((g) => g.id === selection.value!.id)) selection.value = null
  selectedIds.value = selectedIds.value.filter((i) => draft.nodes.some((n) => n.id === i))
}

/** The pure helpers return a whole new draft; the view keeps its reactive object and takes the parts over. */
function apply(next: Draft) {
  draft.nodes = next.nodes
  draft.edges = next.edges
  draft.groups = next.groups
}

function select(sel: Selection | null) {
  selection.value = sel
  if (sel?.kind === 'node') selectedIds.value = [sel.id]
  else if (sel?.kind === 'group') selectedIds.value = draft.groups.find((g) => g.id === sel.id)?.nodeIds ?? []
  else selectedIds.value = []
}
function selectMany(ids: string[]) {
  selectedIds.value = ids
  selection.value = ids.length === 1 ? { kind: 'node', id: ids[0] } : null
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
  select({ kind: 'node', id: node.id })
}

function resizeNode(nodeId: string, w: number, h: number) {
  const n = draft.nodes.find((i) => i.id === nodeId)
  if (n) n.size = { w, h }
}
function fitNode(nodeId: string) {
  const n = draft.nodes.find((i) => i.id === nodeId)
  if (n) delete n.size // back to "as big as the title needs"
}

function moveNodes(moves: Move[]) {
  for (const m of moves) {
    const n = draft.nodes.find((i) => i.id === m.id)
    if (n) n.position = { x: m.x, y: m.y }
  }
}

function groupSelection() {
  const result = groupNodes(draft, selectedIds.value)
  if (!result.groupId) return say('Select two or more cards to group them.')
  apply(result.draft)
  select({ kind: 'group', id: result.groupId })
  say('Grouped. Name it in the toolbar; click its name bar on the canvas to select the group again.')
}
function ungroupSelection() {
  const g = selectedGroup.value
  if (!g) return
  apply(ungroup(draft, g.id))
  selectMany([...g.nodeIds])
  say('Ungrouped: the cards stay where they are.')
}
function renameSelectedGroup(text: string) {
  const g = selectedGroup.value
  if (g && text.trim()) apply(renameGroup(draft, g.id, text))
}
/** A group is copied with the connections inside it; cards are copied alone, without any connection. */
function copySelection() {
  const g = selectedGroup.value
  if (g) {
    const box = groupBox(g, draft.nodes, sizeInView)
    const inside = draft.edges.filter((e) => g.nodeIds.includes(e.source) && g.nodeIds.includes(e.target)).length
    const result = copyGroup(draft, g.id, { x: 0, y: (box?.h ?? 120) + 24 })
    if (!result.groupId) return
    apply(result.draft)
    selectedIds.value = result.newIds
    selection.value = { kind: 'group', id: result.groupId }
    return say(`Copied the group with its ${inside} connection${inside === 1 ? '' : 's'}.`)
  }
  if (!selectedIds.value.length) return
  const result = copyCards(draft, selectedIds.value)
  apply(result.draft)
  selectMany(result.newIds)
  say(`Copied ${result.newIds.length} card${result.newIds.length === 1 ? '' : 's'}, without connections.`)
}

function connect(source: string, target: string, handle: string) {
  const result = canConnect(draft, cards.byType, source, target, handle)
  if (!result.ok) return say(result.reason)
  const edge = { id: newId('e'), source, sourceHandle: handle, target }
  draft.edges.push(edge)
  if (findLoops(draft).backEdgeIds.has(edge.id)) {
    const loop = findLoops(draft).loops.find((l) => l.includes(source) && l.includes(target)) ?? []
    say(
      loopHasCondition(loop, draft, cards.byType)
        ? 'Loop created: the earlier card runs again with this card\'s output, until the IF sends the flow the other way.'
        : 'Loop created, but nothing in it decides when to stop: it will repeat until the loop limit. Add an IF card to end it.',
    )
  }
  select({ kind: 'edge', id: draft.edges[draft.edges.length - 1].id })
}

/** A connection goes at once; a card or a group asks first (it takes its connections with it). */
function removeSelection() {
  const s = selection.value
  if (s?.kind === 'edge') {
    draft.edges = removeEdge(draft, s.id).edges
    return select(null)
  }
  const g = selectedGroup.value
  const ids = g ? [...g.nodeIds] : [...selectedIds.value]
  if (!ids.length) return
  const links = connectionsTouching(draft, ids)
  const linkText = links === 0 ? 'It has no connections.' : `${links} connection${links === 1 ? '' : 's'} go${links === 1 ? 'es' : ''} with ${ids.length === 1 ? 'it' : 'them'}.`
  const quote = (id: string) => `"${draft.nodes.find((n) => n.id === id)?.name ?? id}"`
  if (g) {
    pendingDelete.value = { ids, groupId: g.id, title: 'Delete group', message: `Delete the group "${g.name}" and its ${ids.length} card${ids.length === 1 ? '' : 's'}? ${linkText} To keep the cards, use Ungroup instead.` }
  } else {
    pendingDelete.value = {
      ids,
      title: ids.length === 1 ? 'Delete card' : 'Delete cards',
      message: ids.length === 1 ? `Delete the card ${quote(ids[0])}? ${linkText}` : `Delete these ${ids.length} cards? ${linkText}`,
    }
  }
}
function confirmRemove() {
  const p = pendingDelete.value
  pendingDelete.value = null
  if (!p) return
  apply(p.groupId ? removeGroupWithCards(draft, p.groupId) : removeNodes(draft, p.ids))
  select(null)
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

const selBtn = 'inline-flex h-9 w-9 items-center justify-center rounded-control border border-line-strong bg-surface hover:bg-surface-hover disabled:opacity-40'
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
          <span v-if="loops.loops.length" class="relative block w-36" title="How many times one card may run in one run. A loop that never ends stops here.">
            <AppIcon name="refresh" :size="14" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
            <input v-model="loopLimit" type="number" min="1" max="1000" aria-label="Loop limit" placeholder="Loop limit (50)" :class="inputCls" data-testid="loop-limit" />
          </span>
          <p v-if="loopsWithoutCondition.length" class="flex min-w-0 items-center gap-1.5 text-xs font-semibold" data-testid="loop-hint"><AppIcon name="alert" :size="13" class="shrink-0" /> A loop has no IF to end it: it repeats until the loop limit.</p>
          <div v-if="hasSelection" class="flex items-center gap-1.5" data-testid="selection-bar">
            <span class="flex items-center gap-1 text-xs font-semibold text-ink-soft" data-testid="selection-count"><AppIcon name="layers" :size="13" /> {{ selectionLabel }}</span>
            <span v-if="selectedGroup" class="relative block w-44">
              <AppIcon name="edit" :size="14" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
              <input type="text" maxlength="60" aria-label="Group name" placeholder="Group name" :value="selectedGroup.name" :class="inputCls" data-testid="group-name" autocomplete="off" @input="renameSelectedGroup(($event.target as HTMLInputElement).value)" @blur="($event.target as HTMLInputElement).value = selectedGroup.name" />
            </span>
            <button v-if="!selectedGroup" type="button" :disabled="selectedIds.length < 2" :class="selBtn" title="Group the selected cards" aria-label="Group the selected cards" data-testid="sel-group" @click="groupSelection"><AppIcon name="layers" :size="15" /></button>
            <button v-else type="button" :class="selBtn" title="Ungroup (the cards stay)" aria-label="Ungroup" data-testid="sel-ungroup" @click="ungroupSelection"><AppIcon name="ungroup" :size="15" /></button>
            <button type="button" :class="selBtn" :title="selectedGroup ? 'Copy the group with the connections inside it' : 'Copy the selected cards (connections are not copied)'" aria-label="Copy" data-testid="sel-copy" @click="copySelection"><AppIcon name="copy" :size="15" /></button>
            <button type="button" :class="[selBtn, 'border-ink']" :title="selectedGroup ? 'Delete the group and its cards' : 'Delete the selected cards'" aria-label="Delete" data-testid="sel-delete" @click="removeSelection"><AppIcon name="trash" :size="15" /></button>
          </div>
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
            :groups="draft.groups"
            :selected-ids="selectedIds"
            :types="cards.byType"
            :icon-for="cards.iconFor"
            :states="nodeStates"
            :selection="selection"
            :rounds="rounds"
            @move="moveNodes"
            @resize="resizeNode"
            @fit="fitNode"
            @select="select"
            @select-many="selectMany"
            @copy="copySelection"
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
            :flow-id="id"
            :saved="savedIds.has(selectedNode.id)"
            :saved-key="flow?.updatedAt"
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

    <ConfirmDialog v-if="pendingDelete" :title="pendingDelete.title" :message="pendingDelete.message" confirm-label="Delete" confirm-icon="trash" danger @confirm="confirmRemove" @cancel="pendingDelete = null" />
    <DeleteFlowDialog v-if="confirmingDelete && flow" :flow-name="flow.name" :busy="deleteBusy" :error="deleteError" @confirm="confirmDelete" @cancel="confirmingDelete = false" />
  </section>
</template>
