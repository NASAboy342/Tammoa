<script setup lang="ts">
/**
 * The card canvas: pan (drag the background or scroll), zoom (Ctrl/Cmd + scroll or the buttons), drag cards,
 * connect by dragging from a card's output handle onto another card (also back onto an EARLIER card: that makes a loop, drawn dashed
 * under the cards), resize a card by its bottom-right corner, click to select (Shift/Ctrl/Cmd-click adds or removes a card, Shift-drag on the
 * background draws a selection rectangle, Ctrl/Cmd+A selects all), drag a selected card to move every selected card, click a group's header to
 * select the group. Delete asks to remove the selection (the page shows the warning), Ctrl/Cmd+D copies it.
 * A card is as big as its title needs unless the user dragged it to a size.
 * Presentational: cards, connections and run states come in as props; every change goes out as an event.
 * It never executes anything: states are whatever the backend reported.
 */
import { computed, reactive, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'
import type { NodeRunStatus } from '@/services/runApi'
import type { DraftEdge, DraftGroup, DraftNode } from '@/helpers/flowDraft'
import { NODE_H, NODE_W, edgePath, inputPort, loopEdgePath, outputPort, screenToWorld, zoomAt, type Point, type Size, type View } from '@/helpers/canvasMath'
import { MAX_MANUAL, MIN_MANUAL, sizeOf, titleLines } from '@/helpers/cardSize'
import { findLoops } from '@/helpers/loops'
import { groupBox, nodesInRect, type Box } from '@/helpers/groups'
import { nodeStatusIcon, statusLabel } from '@/helpers/runHelpers'

export type Selection = { kind: 'node' | 'edge' | 'group'; id: string }
export interface Move {
  id: string
  x: number
  y: number
}

const props = defineProps<{
  nodes: DraftNode[]
  edges: DraftEdge[]
  groups: DraftGroup[]
  /** Every selected card (one, several, or the cards of the selected group). */
  selectedIds: string[]
  types: Record<string, CardTypeInfo>
  iconFor: (type: string) => IconName
  states: Record<string, NodeRunStatus>
  /** How many times each card has run in the shown run (shown as ×N when it ran more than once, i.e. in a loop). */
  rounds?: Record<string, number>
  selection: Selection | null
}>()
const emit = defineEmits<{
  move: [moves: Move[]]
  select: [selection: Selection | null]
  selectMany: [ids: string[]]
  copy: []
  connect: [source: string, target: string, handle: string]
  resize: [id: string, w: number, h: number]
  fit: [id: string]
  remove: []
}>()

const el = ref<HTMLElement | null>(null)
const view = reactive<View>({ x: 40, y: 40, k: 1 })
const link = ref<{ source: string; handle: string; to: Point } | null>(null)

const byId = computed(() => new Map(props.nodes.map((n) => [n.id, n])))
/** The output handles of a card, in display order (most cards have just "main"). */
const outputsOf = (node: DraftNode) => props.types[node.type]?.outputs?.length ? props.types[node.type].outputs : [{ key: 'main', label: '' }]
/** A card is as big as its title needs, or the size the user gave it. */
const sizeFor = (node: DraftNode): Size => sizeOf(node, outputsOf(node).length)
const portOf = (node: DraftNode, handle: string) => {
  const outs = outputsOf(node)
  return outputPort(node.position, Math.max(0, outs.findIndex((o) => o.key === handle)), outs.length, sizeFor(node))
}

const selectedSet = computed(() => new Set(props.selectedIds))
/** A frame around each group's cards (it follows them when they move or change size). */
const frames = computed(() =>
  props.groups.flatMap((g) => {
    const box = groupBox(g, props.nodes, sizeFor)
    return box ? [{ group: g, box }] : []
  }),
)
const marquee = ref<Box | null>(null)

const loops = computed(() => findLoops({ nodes: props.nodes, edges: props.edges }))
interface DrawnEdge {
  id: string
  d: string
  loop: boolean
  label: Point | null
}
const paths = computed<DrawnEdge[]>(() => {
  let lane = 0
  return props.edges.flatMap((e): DrawnEdge[] => {
    const s = byId.value.get(e.source)
    const t = byId.value.get(e.target)
    if (!s || !t) return []
    const from = portOf(s, e.sourceHandle ?? 'main')
    const to = inputPort(t.position, sizeFor(t))
    if (loops.value.backEdgeIds.has(e.id)) {
      // a loop-back goes under the cards: below the lowest edge of the two cards it joins
      const bottom = Math.max(s.position.y + sizeFor(s).h, t.position.y + sizeFor(t).h)
      const l = lane++
      const y = bottom + 34 + l * 16
      return [{ id: e.id, d: loopEdgePath(from, to, bottom, l), loop: true, label: { x: (from.x + to.x) / 2, y: y - 6 } }]
    }
    return [{ id: e.id, d: edgePath(from, to), loop: false, label: null }]
  })
})
const linkPath = computed(() => {
  const l = link.value
  const s = l && byId.value.get(l.source)
  return l && s ? edgePath(portOf(s, l.handle), l.to) : ''
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
  if (e.shiftKey) return onMarqueeDown(e)
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

/** Shift + drag on the background: a rectangle that selects every card it touches. */
function onMarqueeDown(e: PointerEvent) {
  const from = worldAt(e)
  marquee.value = { x: from.x, y: from.y, w: 0, h: 0 }
  gesture(
    (m) => {
      const to = worldAt(m)
      const box = { x: Math.min(from.x, to.x), y: Math.min(from.y, to.y), w: Math.abs(to.x - from.x), h: Math.abs(to.y - from.y) }
      marquee.value = box
      emit('selectMany', nodesInRect(props.nodes, box, sizeFor))
    },
    () => {
      marquee.value = null
    },
  )
}

/** Drags these cards together: each keeps its place relative to the others. */
function dragCards(e: PointerEvent, ids: string[], onClick?: () => void) {
  const starts = props.nodes.filter((n) => ids.includes(n.id)).map((n) => ({ id: n.id, x: n.position.x, y: n.position.y }))
  let moved = false
  gesture(
    (m) => {
      const dx = (m.clientX - e.clientX) / view.k
      const dy = (m.clientY - e.clientY) / view.k
      if (!moved && Math.hypot(m.clientX - e.clientX, m.clientY - e.clientY) < 3) return
      moved = true
      emit('move', starts.map((s) => ({ id: s.id, x: Math.round(s.x + dx), y: Math.round(s.y + dy) })))
    },
    () => {
      if (!moved) onClick?.()
    },
  )
}

function onNodeDown(e: PointerEvent, node: DraftNode) {
  el.value?.focus()
  if (e.shiftKey || e.ctrlKey || e.metaKey) {
    // add the card to the selection, or take it out
    emit('selectMany', selectedSet.value.has(node.id) ? props.selectedIds.filter((i) => i !== node.id) : [...props.selectedIds, node.id])
    return
  }
  if (selectedSet.value.has(node.id) && props.selectedIds.length > 1) {
    // part of a multi-selection: the whole selection moves; a plain click (no movement) selects just this card
    return dragCards(e, props.selectedIds, () => emit('select', { kind: 'node', id: node.id }))
  }
  emit('select', { kind: 'node', id: node.id })
  dragCards(e, [node.id])
}

/** Click a group's name bar to select the group; drag it to move the group's cards together. */
function onGroupDown(e: PointerEvent, group: DraftGroup) {
  el.value?.focus()
  emit('select', { kind: 'group', id: group.id })
  dragCards(e, group.nodeIds)
}

function onOutputDown(e: PointerEvent, node: DraftNode, handle: string) {
  emit('select', { kind: 'node', id: node.id })
  link.value = { source: node.id, handle, to: worldAt(e) }
  gesture(
    (m) => {
      if (link.value) link.value = { source: node.id, handle, to: worldAt(m) }
    },
    (up) => {
      const target = (document.elementFromPoint(up.clientX, up.clientY) as HTMLElement | null)?.closest<HTMLElement>('[data-node-card]')
      link.value = null
      if (target?.dataset.nodeId && target.dataset.nodeId !== node.id) emit('connect', node.id, target.dataset.nodeId, handle)
    },
  )
}

/** Drag the bottom-right corner to give a card a size of its own; double-click it to let the card fit its title again. */
function onResizeDown(e: PointerEvent, node: DraftNode) {
  emit('select', { kind: 'node', id: node.id })
  const start = { x: e.clientX, y: e.clientY, size: sizeFor(node) }
  gesture(
    (m) =>
      emit(
        'resize',
        node.id,
        Math.round(Math.min(MAX_MANUAL.w, Math.max(MIN_MANUAL.w, start.size.w + (m.clientX - start.x) / view.k))),
        Math.round(Math.min(MAX_MANUAL.h, Math.max(MIN_MANUAL.h, start.size.h + (m.clientY - start.y) / view.k))),
      ),
    () => {},
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
  if (e.target === el.value && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a') {
    e.preventDefault()
    emit('selectMany', props.nodes.map((n) => n.id))
    return
  }
  if (e.target === el.value && (e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
    e.preventDefault()
    emit('copy')
    return
  }
  if ((e.key === 'Delete' || e.key === 'Backspace') && (props.selection || props.selectedIds.length > 0) && e.target === el.value) {
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
    selectedSet.value.has(node.id) ? 'border-2 border-ink' : 'border border-line-strong',
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
      <div
        v-for="f in frames"
        :key="f.group.id"
        class="pointer-events-none absolute rounded-card"
        :class="selection?.kind === 'group' && selection.id === f.group.id ? 'border-2 border-ink' : 'border border-dashed border-line-strong'"
        :style="{ left: `${f.box.x}px`, top: `${f.box.y}px`, width: `${f.box.w}px`, height: `${f.box.h}px` }"
        :data-testid="`group-${f.group.id}`"
        :data-group-name="f.group.name"
      >
        <div
          class="pointer-events-auto flex h-[26px] max-w-full cursor-grab items-center gap-1.5 truncate px-2.5 text-[11px] font-semibold text-ink-soft active:cursor-grabbing"
          :title="`Group: ${f.group.name} (click to select, drag to move)`"
          :data-testid="`group-head-${f.group.id}`"
          @pointerdown.stop="onGroupDown($event, f.group)"
        >
          <AppIcon name="layers" :size="12" class="shrink-0" />
          <span class="truncate" data-testid="group-title">{{ f.group.name }}</span>
        </div>
      </div>

      <div
        v-if="marquee"
        class="pointer-events-none absolute border border-dashed border-ink bg-ink/5"
        :style="{ left: `${marquee.x}px`, top: `${marquee.y}px`, width: `${marquee.w}px`, height: `${marquee.h}px` }"
        data-testid="marquee"
      />

      <svg class="absolute left-0 top-0 overflow-visible" width="1" height="1" data-testid="canvas-edges">
        <g v-for="p in paths" :key="p.id" :data-testid="`edge-${p.id}`" :data-loop="p.loop ? 'true' : undefined">
          <path :d="p.d" fill="none" stroke="var(--ink)" :stroke-width="selection?.kind === 'edge' && selection.id === p.id ? 3 : 1.5" :stroke-dasharray="p.loop ? '7 5' : undefined" stroke-linecap="round" />
          <text v-if="p.label" :x="p.label.x" :y="p.label.y" text-anchor="middle" font-size="10" font-weight="600" fill="var(--ink-soft)" class="pointer-events-none select-none" data-testid="loop-label">loop</text>
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
        :title="`${node.name} (${types[node.type]?.name ?? node.type})`"
        class="group pointer-events-auto absolute flex cursor-grab items-center gap-2.5 rounded-card bg-surface pl-3 active:cursor-grabbing"
        :class="[nodeClass(node), outputsOf(node).length > 1 ? 'pr-12' : 'pr-3']" 
        :style="{ left: `${node.position.x}px`, top: `${node.position.y}px`, width: `${sizeFor(node).w}px`, height: `${sizeFor(node).h}px` }"
        :data-size="`${sizeFor(node).w}x${sizeFor(node).h}`"
        :data-manual-size="node.size ? 'true' : undefined"
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
          <span
            class="block break-words text-xs font-semibold leading-4"
            :style="{ display: '-webkit-box', WebkitBoxOrient: 'vertical', WebkitLineClamp: titleLines(sizeFor(node).h), overflow: 'hidden' }"
            data-testid="node-name"
          >{{ node.name }}</span>
          <span class="block truncate text-[10px] text-muted">{{ types[node.type]?.name ?? node.type }}</span>
        </span>
        <span v-if="(rounds?.[node.id] ?? 1) > 1" class="shrink-0 rounded border border-line-strong px-1 text-[10px] font-semibold" :title="`Ran ${rounds![node.id]} times in this run (a loop)`" data-testid="node-rounds">×{{ rounds![node.id] }}</span>
        <AppIcon
          v-if="states[node.id] && states[node.id] !== 'pending'"
          :name="nodeStatusIcon[states[node.id]!]"
          :size="15"
          :class="states[node.id] === 'running' ? 'animate-spin' : ''"
          :title="statusLabel(states[node.id]!)"
          data-testid="node-status"
        />
        <template v-for="(o, i) in outputsOf(node)" :key="o.key">
          <span
            v-if="outputsOf(node).length > 1"
            class="pointer-events-none absolute right-3.5 -translate-y-1/2 rounded bg-surface px-1 text-[9px] font-semibold uppercase tracking-wide text-ink-soft"
            :style="{ top: `${((i + 1) / (outputsOf(node).length + 1)) * 100}%` }"
            data-testid="handle-label"
          >{{ o.label || o.key }}</span>
          <span
            data-handle="out"
            :data-handle-key="o.key"
            class="absolute -right-[7px] h-3.5 w-3.5 -translate-y-1/2 cursor-crosshair rounded-full border-[1.5px] border-ink bg-ink"
            :style="{ top: `${((i + 1) / (outputsOf(node).length + 1)) * 100}%` }"
            data-icon-exempt="connection handle, not a button"
            :title="outputsOf(node).length > 1 ? `Output: ${o.label || o.key}` : 'Drag to connect'"
            :data-testid="`out-${node.id}-${o.key}`"
            @pointerdown.stop.prevent="onOutputDown($event, node, o.key)"
          />
        </template>
        <span
          class="absolute bottom-0.5 right-0.5 inline-flex h-3.5 w-3.5 cursor-nwse-resize items-center justify-center rounded-sm text-muted transition-opacity group-hover:opacity-100"
          :class="selection?.kind === 'node' && selection.id === node.id ? 'opacity-100' : 'opacity-0'"
          title="Drag to resize. Double-click to fit the title again."
          data-icon-exempt="resize handle, not a button"
          :data-testid="`resize-${node.id}`"
          @pointerdown.stop.prevent="onResizeDown($event, node)"
          @dblclick.stop="emit('fit', node.id)"
        >
          <AppIcon name="resize" :size="10" />
        </span>
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
/* keyboard focus is shown, but as a hairline inside the canvas instead of a heavy frame around it */
.canvas-grid:focus-visible {
  outline: 1px solid var(--line-strong);
  outline-offset: -1px;
}
.canvas-grid {
  background-image: radial-gradient(var(--line-strong) 1px, transparent 1px);
  background-size: 22px 22px;
}
</style>
