import type { CardTypeInfo } from '@/services/cardApi'
import type { FlowDefinition } from '@/services/flowApi'
import { NODE_H, NODE_W, type Size } from './canvasMath'

/** The editable copy of a flow's cards and connections. Pure functions: the canvas view owns the state. */
export interface DraftNode {
  id: string
  type: string
  name: string
  position: { x: number; y: number }
  /** Only when the user dragged the card to a size; otherwise it is as big as its title needs. */
  size?: Size
  params: Record<string, unknown>
}
export interface DraftEdge {
  id: string
  source: string
  /** Which output of the source card the connection leaves from (default 'main'). */
  sourceHandle: string
  target: string
}
/** A named set of cards that is drawn as one frame and moves together. It only organises the canvas; the engine ignores it. */
export interface DraftGroup {
  id: string
  name: string
  nodeIds: string[]
}
export interface Draft {
  nodes: DraftNode[]
  edges: DraftEdge[]
  groups: DraftGroup[]
  /** How often one card may run in one run (flow setting); undefined = the engine's default (50). */
  loopLimit?: number
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
      ...(n.size && Number.isFinite(Number(n.size.w)) && Number.isFinite(Number(n.size.h)) ? { size: { w: Number(n.size.w), h: Number(n.size.h) } } : {}),
      params: { ...(n.params ?? {}) },
    })),
    groups: toGroups(def?.groups, new Set(nodes.map((n) => String(n.id)))),
    ...(typeof def?.settings?.loopLimit === 'number' ? { loopLimit: def.settings.loopLimit } : {}),
    edges: edges.map((e) => ({ id: String(e.id ?? newId('e')), source: String(e.source), sourceHandle: String(e.sourceHandle ?? 'main'), target: String(e.target) })),
  }
}

/** Groups from a stored definition; anything that points at a missing card, or a card already in a group, is dropped. */
function toGroups(raw: unknown, cardIds: Set<string>): DraftGroup[] {
  if (!Array.isArray(raw)) return []
  const used = new Set<string>()
  const groups: DraftGroup[] = []
  for (const g of raw as Partial<DraftGroup>[]) {
    if (!g || typeof g.id !== 'string' || typeof g.name !== 'string') continue
    const nodeIds = (Array.isArray(g.nodeIds) ? g.nodeIds : []).map(String).filter((id) => cardIds.has(id) && !used.has(id))
    nodeIds.forEach((id) => used.add(id))
    groups.push({ id: g.id, name: g.name, nodeIds })
  }
  return groups
}

/** Writes the draft back over the stored definition, keeping keys the UI doesn't know (schemaVersion, future fields). */
export function toDefinition(base: FlowDefinition | undefined, draft: Draft): FlowDefinition {
  const definition: FlowDefinition = { ...(base ?? {}), nodes: draft.nodes.map((n) => ({ ...n })), edges: draft.edges.map((e) => ({ ...e })) }
  if (draft.groups.length > 0) definition.groups = draft.groups.map((g) => ({ ...g, nodeIds: [...g.nodeIds] }))
  else delete definition.groups
  const settings = { ...((base?.settings as Record<string, unknown> | undefined) ?? {}) }
  if (draft.loopLimit === undefined) delete settings.loopLimit
  else settings.loopLimit = draft.loopLimit
  if (Object.keys(settings).length > 0) definition.settings = settings
  else delete definition.settings
  return definition
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
  return removeNodes(draft, [id])
}

/** Removes cards with their connections; they also leave their groups, and a group left without cards disappears. */
export function removeNodes(draft: Draft, ids: string[]): Draft {
  const gone = new Set(ids)
  return {
    ...draft,
    nodes: draft.nodes.filter((n) => !gone.has(n.id)),
    edges: draft.edges.filter((e) => !gone.has(e.source) && !gone.has(e.target)),
    groups: draft.groups.map((g) => ({ ...g, nodeIds: g.nodeIds.filter((id) => !gone.has(id)) })).filter((g) => g.nodeIds.length > 0),
  }
}

export function removeEdge(draft: Draft, id: string): Draft {
  return { ...draft, edges: draft.edges.filter((e) => e.id !== id) }
}

export type ConnectResult = { ok: true } | { ok: false; reason: string }

/** Mirrors the backend rules so the UI refuses early; the backend still validates on save. A connection back to an earlier card is allowed (a loop). */
export function canConnect(draft: Draft, types: Record<string, CardTypeInfo>, source: string, target: string, handle = 'main'): ConnectResult {
  if (source === target) return { ok: false, reason: 'A card cannot connect to itself.' }
  const t = draft.nodes.find((n) => n.id === target)
  const s = draft.nodes.find((n) => n.id === source)
  if (!s || !t) return { ok: false, reason: 'Unknown card.' }
  if (types[t.type]?.isTrigger) return { ok: false, reason: 'A trigger has no input.' }
  if (!(types[s.type]?.outputs ?? [{ key: 'main' }]).some((o) => o.key === handle)) return { ok: false, reason: 'That output does not exist.' }
  if (draft.edges.some((e) => e.source === source && e.sourceHandle === handle && e.target === target)) return { ok: false, reason: 'Already connected.' }
  // a connection back to an earlier card is allowed: it makes a loop
  return { ok: true }
}

export function hasTrigger(draft: Draft, types: Record<string, CardTypeInfo>): boolean {
  return draft.nodes.some((n) => types[n.type]?.isTrigger)
}
