import type { CardTypeInfo } from '@/services/cardApi'
import type { FlowDefinition } from '@/services/flowApi'
import { NODE_H, NODE_W } from './canvasMath'

/** The editable copy of a flow's cards and connections. Pure functions: the canvas view owns the state. */
export interface DraftNode {
  id: string
  type: string
  name: string
  position: { x: number; y: number }
  params: Record<string, unknown>
}
export interface DraftEdge {
  id: string
  source: string
  target: string
}
export interface Draft {
  nodes: DraftNode[]
  edges: DraftEdge[]
}

export function newId(prefix: string): string {
  const rand = typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID().replace(/-/g, '') : Math.random().toString(16).slice(2)
  return `${prefix}-${rand.slice(0, 8)}`
}

export function toDraft(def: FlowDefinition | undefined): Draft {
  const nodes = (Array.isArray(def?.nodes) ? def.nodes : []) as Partial<DraftNode>[]
  const edges = (Array.isArray(def?.edges) ? def.edges : []) as Partial<DraftEdge>[]
  return {
    nodes: nodes.map((n) => ({
      id: String(n.id),
      type: String(n.type),
      name: typeof n.name === 'string' ? n.name : String(n.type),
      position: { x: Number(n.position?.x ?? 0), y: Number(n.position?.y ?? 0) },
      params: { ...(n.params ?? {}) },
    })),
    edges: edges.map((e) => ({ id: String(e.id ?? newId('e')), source: String(e.source), target: String(e.target) })),
  }
}

/** Writes the draft back over the stored definition, keeping keys the UI doesn't know (schemaVersion, future fields). */
export function toDefinition(base: FlowDefinition | undefined, draft: Draft): FlowDefinition {
  return { ...(base ?? {}), nodes: draft.nodes.map((n) => ({ ...n })), edges: draft.edges.map((e) => ({ ...e })) }
}

/** "Script", "Script 2", "Script 3"… so cards of one type stay distinguishable. */
export function uniqueName(nodes: DraftNode[], base: string): string {
  const taken = new Set(nodes.map((n) => n.name))
  if (!taken.has(base)) return base
  let i = 2
  while (taken.has(`${base} ${i}`)) i++
  return `${base} ${i}`
}

export function defaultParams(info: CardTypeInfo): Record<string, unknown> {
  const params: Record<string, unknown> = {}
  for (const p of info.params) if (p.defaultValue !== undefined && p.defaultValue !== null) params[p.key] = p.defaultValue
  return params
}

export function makeNode(info: CardTypeInfo, nodes: DraftNode[], at: { x: number; y: number }): DraftNode {
  return { id: newId('n'), type: info.type, name: uniqueName(nodes, info.name), position: { x: Math.round(at.x), y: Math.round(at.y) }, params: defaultParams(info) }
}

/** First spot at or after `start` where a new card overlaps no existing one: walks right, then wraps to the next row. */
export function freeSpot(nodes: DraftNode[], start: { x: number; y: number }): { x: number; y: number } {
  const gapX = NODE_W + 48
  const gapY = NODE_H + 40
  const overlaps = (p: { x: number; y: number }) =>
    nodes.some((n) => Math.abs(n.position.x - p.x) < NODE_W + 24 && Math.abs(n.position.y - p.y) < NODE_H + 24)
  for (let row = 0; row < 50; row++) {
    for (let col = 0; col < 4; col++) {
      const p = { x: start.x + col * gapX, y: start.y + row * gapY }
      if (!overlaps(p)) return p
    }
  }
  return start
}

export function removeNode(draft: Draft, id: string): Draft {
  return { nodes: draft.nodes.filter((n) => n.id !== id), edges: draft.edges.filter((e) => e.source !== id && e.target !== id) }
}

export function removeEdge(draft: Draft, id: string): Draft {
  return { nodes: draft.nodes, edges: draft.edges.filter((e) => e.id !== id) }
}

export type ConnectResult = { ok: true } | { ok: false; reason: string }

/** Mirrors the backend rules so the UI refuses early; the backend still validates on save. */
export function canConnect(draft: Draft, types: Record<string, CardTypeInfo>, source: string, target: string): ConnectResult {
  if (source === target) return { ok: false, reason: 'A card cannot connect to itself.' }
  const t = draft.nodes.find((n) => n.id === target)
  const s = draft.nodes.find((n) => n.id === source)
  if (!s || !t) return { ok: false, reason: 'Unknown card.' }
  if (types[t.type]?.isTrigger) return { ok: false, reason: 'A trigger has no input.' }
  if (draft.edges.some((e) => e.source === source && e.target === target)) return { ok: false, reason: 'Already connected.' }
  if (reaches(draft.edges, target, source)) return { ok: false, reason: 'That would make a loop (not supported yet).' }
  return { ok: true }
}

/** Is `to` reachable from `from` following connections? */
export function reaches(edges: DraftEdge[], from: string, to: string): boolean {
  const seen = new Set<string>()
  const stack = [from]
  while (stack.length) {
    const id = stack.pop()!
    if (id === to) return true
    if (seen.has(id)) continue
    seen.add(id)
    for (const e of edges) if (e.source === id) stack.push(e.target)
  }
  return false
}

export function hasTrigger(draft: Draft, types: Record<string, CardTypeInfo>): boolean {
  return draft.nodes.some((n) => types[n.type]?.isTrigger)
}
