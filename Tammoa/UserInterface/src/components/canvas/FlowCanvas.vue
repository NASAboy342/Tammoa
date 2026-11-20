<script setup lang="ts">
/**
 * The card canvas: pan (drag the background or scroll), zoom (Ctrl/Cmd + scroll or the buttons), drag cards,
 * connect by dragging from a card's output handle onto another card, click to select, Delete removes the selection.
 * Presentational: cards, connections and run states come in as props; every change goes out as an event.
 * It never executes anything: states are whatever the backend reported.
 */
import { computed, reactive, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'
import type { NodeRunStatus } from '@/services/runApi'
import type { DraftEdge, DraftNode } from '@/helpers/flowDraft'
import { NODE_H, NODE_W, edgePath, inputPort, outputPort, screenToWorld, zoomAt, type Point, type View } from '@/helpers/canvasMath'
import { nodeStatusIcon, statusLabel } from '@/helpers/runHelpers'

export type Selection = { kind: 'node' | 'edge'; id: string }

const props = defineProps<{
  nodes: DraftNode[]
  edges: DraftEdge[]
  types: Record<string, CardTypeInfo>
  iconFor: (type: string) => IconName
  states: Record<string, NodeRunStatus>
  selection: Selection | null
}>()
const emit = defineEmits<{
  move: [id: string, x: number, y: number]
  select: [selection: Selection | null]
  connect: [source: string, target: string]
  remove: []
}>()

const el = ref<HTMLElement | null>(null)
const view = reactive<View>({ x: 40, y: 40, k: 1 })
const link = ref<{ source: string; to: Point } | null>(null)

const byId = computed(() => new Map(props.nodes.map((n) => [n.id, n])))
const paths = computed(() =>
  props.edges.flatMap((e) => {
    const s = byId.value.get(e.source)
    const t = byId.value.get(e.target)
    return s && t ? [{ id: e.id, d: edgePath(outputPort(s.position), inputPort(t.position)) }] : []
  }),
)
const linkPath = computed(() => {
  const l = link.value
  const s = l && byId.value.get(l.source)
  return l && s ? edgePath(outputPort(s.position), l.to) : ''
})

function worldAt(e: { clientX: number; clientY: number }): Point {
  const r = el.value!.getBoundingClientRect()
  return screenToWorld({ x: e.clientX - r.left, y: e.clientY - r.top }, view)
}
function applyView(v: View) {
  view.x = v.x
  view.y = v.y
  view.k = v.k
}

/** Runs a pointer gesture: `move` on every pointermove, `end` once on release. */
function gesture(move: (e: PointerEvent) => void, end: (e: PointerEvent) => void) {
  const up = (e: PointerEvent) => {
    window.removeEventListener('pointermove', move)
    window.removeEventListener('pointerup', up)
    end(e)
  }
  window.addEventListener('pointermove', move)
  window.addEventListener('pointerup', up)
}

function onBackgroundDown(e: PointerEvent) {
  el.value?.focus()
  emit('select', null)
  const start = { x: e.clientX, y: e.clientY, vx: view.x, vy: view.y }
  gesture(
    (m) => {
      view.x = start.vx + (m.clientX - start.x)
      view.y = start.vy + (m.clientY - start.y)
    },
    () => {},
  )
}

function onNodeDown(e: PointerEvent, node: DraftNode) {
  el.value?.focus()
  emit('select', { kind: 'node', id: node.id })
  const start = { x: e.clientX, y: e.clientY, nx: node.position.x, ny: node.position.y }
  gesture(
    (m) => emit('move', node.id, Math.round(start.nx + (m.clientX - start.x) / view.k), Math.round(start.ny + (m.clientY - start.y) / view.k)),
    () => {},
  )
}

function onOutputDown(e: PointerEvent, node: DraftNode) {
  emit('select', { kind: 'node', id: node.id })
  link.value = { source: node.id, to: worldAt(e) }
  gesture(
    (m) => {
      if (link.value) link.value = { source: node.id, to: worldAt(m) }
    },
    (up) => {
      const target = (document.elementFromPoint(up.clientX, up.clientY) as HTMLElement | null)?.closest<HTMLElement>('[data-node-card]')
      link.value = null
      if (target?.dataset.nodeId && target.dataset.nodeId !== node.id) emit('connect', node.id, target.dataset.nodeId)
    },
  )
}

function onWheel(e: WheelEvent) {
  if (e.ctrlKey || e.metaKey) {
    const r = el.value!.getBoundingClientRect()
    applyView(zoomAt(view, Math.exp(-e.deltaY * 0.01), { x: e.clientX - r.left, y: e.clientY - r.top }))
  } else {
    view.x -= e.deltaX
    view.y -= e.deltaY
  }
}

function zoomBy(factor: number) {
  const r = el.value!.getBoundingClientRect()
  applyView(zoomAt(view, factor, { x: r.width / 2, y: r.height / 2 }))
}

function onKey(e: KeyboardEvent) {
  if ((e.key === 'Delete' || e.key === 'Backspace') && props.selection && e.target === el.value) {
    e.preventDefault()
    emit('remove')
  }
}

defineExpose({
  /** Where a newly added card should go: the middle of what the user is looking at. */
  centerWorld(): Point {
    const r = el.value!.getBoundingClientRect()
    const p = screenToWorld({ x: r.width / 2, y: r.height / 2 }, view)
    return { x: p.x - NODE_W / 2, y: p.y - NODE_H / 2 }
  },
})

const toolBtn = 'inline-flex h-8 w-8 items-center justify-center rounded-control border border-line-strong bg-surface hover:bg-surface-hover'

function nodeClass(node: DraftNode) {
  const st = props.states[node.id]
  return [
    props.selection?.kind === 'node' && props.selection.id === node.id ? 'border-2 border-ink' : 'border border-line-strong',
    st === 'running' ? 'border-dashed' : '',
    st === 'failed' ? 'border-2 border-ink' : '',
    st === 'skipped' || st === 'cancelled' ? 'opacity-55' : '',
  ]
}
</script>

<template>
  <div
    ref="el"
    tabindex="0"
    class="canvas-grid relative h-full w-full touch-none select-none overflow-hidden outline-none"
    data-testid="canvas"
    @pointerdown.self="onBackgroundDown"
    @wheel.prevent="onWheel"
    @keydown="onKey"
  >
    <div class="pointer-events-none absolute left-0 top-0 origin-top-left" data-testid="canvas-layer" :style="{ transform: `translate(${view.x}px, ${view.y}px) scale(${view.k})` }">
      <svg class="absolute left-0 top-0 overflow-visible" width="1" height="1" data-testid="canvas-edges">
        <g v-for="p in paths" :key="p.id" :data-testid="`edge-${p.id}`">
          <path :d="p.d" fill="none" stroke="var(--ink)" :stroke-width="selection?.kind === 'edge' && selection.id === p.id ? 3 : 1.5" stroke-linecap="round" />
          <path :d="p.d" fill="none" stroke="transparent" stroke-width="16" style="pointer-events: stroke; cursor: pointer" @pointerdown.stop="emit('select', { kind: 'edge', id: p.id })" />
        </g>
        <path v-if="linkPath" :d="linkPath" fill="none" stroke="var(--ink)" stroke-width="1.5" stroke-dasharray="5 4" />
      </svg>

      <div
        v-for="node in nodes"
        :key="node.id"
        data-node-card
        :data-node-id="node.id"
        :data-testid="`node-${node.id}`"
        :data-status="states[node.id] ?? 'idle'"
        role="group"
        :aria-label="node.name"
        class="pointer-events-auto absolute flex cursor-grab items-center gap-2.5 rounded-card bg-surface px-3 active:cursor-grabbing"
        :class="nodeClass(node)"
        :style="{ left: `${node.position.x}px`, top: `${node.position.y}px`, width: `${NODE_W}px`, height: `${NODE_H}px` }"
        @pointerdown.stop="onNodeDown($event, node)"
      >
        <span
          v-if="!types[node.type]?.isTrigger"
          data-handle="in"
          class="absolute -left-[7px] top-1/2 h-3.5 w-3.5 -translate-y-1/2 rounded-full border-[1.5px] border-ink bg-surface"
          data-icon-exempt="connection handle, not a button"
        />
        <span class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-control border border-line-strong">
          <AppIcon :name="iconFor(node.type)" :size="16" />
        </span>
        <span class="min-w-0 flex-1">
          <span class="block truncate text-xs font-semibold" data-testid="node-name">{{ node.name }}</span>
          <span class="block truncate text-[10px] text-muted">{{ types[node.type]?.name ?? node.type }}</span>
        </span>
        <AppIcon
          v-if="states[node.id] && states[node.id] !== 'pending'"
          :name="nodeStatusIcon[states[node.id]!]"
          :size="15"
          :class="states[node.id] === 'running' ? 'animate-spin' : ''"
          :title="statusLabel(states[node.id]!)"
          data-testid="node-status"
        />
        <span
          data-handle="out"
          class="absolute -right-[7px] top-1/2 h-3.5 w-3.5 -translate-y-1/2 cursor-crosshair rounded-full border-[1.5px] border-ink bg-ink"
          data-icon-exempt="connection handle, not a button"
          :data-testid="`out-${node.id}`"
          @pointerdown.stop.prevent="onOutputDown($event, node)"
        />
      </div>
    </div>

    <div v-if="!nodes.length" class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center gap-2 text-muted" data-testid="canvas-empty">
      <AppIcon name="layers" :size="40" />
      <p>Add a card to start</p>
    </div>

    <div class="absolute bottom-3 left-3 flex gap-1.5" @pointerdown.stop>
      <button type="button" :class="toolBtn" title="Zoom in" aria-label="Zoom in" data-testid="zoom-in" @click="zoomBy(1.2)"><AppIcon name="plus" :size="15" /></button>
      <button type="button" :class="toolBtn" title="Zoom out" aria-label="Zoom out" data-testid="zoom-out" @click="zoomBy(1 / 1.2)"><AppIcon name="minus" :size="15" /></button>
      <button type="button" :class="toolBtn" title="Reset view" aria-label="Reset view" data-testid="zoom-reset" @click="applyView({ x: 40, y: 40, k: 1 })"><AppIcon name="crosshair" :size="15" /></button>
    </div>
  </div>
</template>

<style scoped>
.canvas-grid {
  background-image: radial-gradient(var(--line-strong) 1px, transparent 1px);
  background-size: 22px 22px;
}
</style>
