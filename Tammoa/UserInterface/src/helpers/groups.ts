import { newId, removeNodes, type Draft, type DraftGroup, type DraftNode } from './flowDraft'
import type { Point, Size } from './canvasMath'

/** Pure rules for selecting, grouping and copying cards. The canvas view owns the state and calls these. */
export const MAX_GROUP_NAME = 60
const MAX_CARD_NAME = 100
const MAX_DATA_CARD_NAME = 60 // a Data card's name is a reference: the backend limits it
const DATA_CARD = 'data.store'
export const GROUP_PAD = { x: 16, top: 34, bottom: 16 }

export interface Box {
  x: number
  y: number
  w: number
  h: number
}

/** Where the group frame is drawn: around its cards, with room for the name on top. Null when the group holds no card. */
export function groupBox(group: DraftGroup, nodes: DraftNode[], sizeFor: (n: DraftNode) => Size): Box | null {
  const members = nodes.filter((n) => group.nodeIds.includes(n.id))
  if (!members.length) return null
  const left = Math.min(...members.map((n) => n.position.x))
  const top = Math.min(...members.map((n) => n.position.y))
  const right = Math.max(...members.map((n) => n.position.x + sizeFor(n).w))
  const bottom = Math.max(...members.map((n) => n.position.y + sizeFor(n).h))
  return { x: left - GROUP_PAD.x, y: top - GROUP_PAD.top, w: right - left + GROUP_PAD.x * 2, h: bottom - top + GROUP_PAD.top + GROUP_PAD.bottom }
}

/** The cards whose box touches the rectangle (for the rubber band). */
export function nodesInRect(nodes: DraftNode[], rect: Box, sizeFor: (n: DraftNode) => Size): string[] {
  return nodes
    .filter((n) => {
      const s = sizeFor(n)
      return n.position.x < rect.x + rect.w && n.position.x + s.w > rect.x && n.position.y < rect.y + rect.h && n.position.y + s.h > rect.y
    })
    .map((n) => n.id)
}

export function groupOf(draft: Draft, nodeId: string): DraftGroup | undefined {
  return draft.groups.find((g) => g.nodeIds.includes(nodeId))
}

/** "Group", "Group 2", "Group 3"… */
export function uniqueGroupName(groups: DraftGroup[], base = 'Group'): string {
  const taken = new Set(groups.map((g) => g.name))
  if (!taken.has(base)) return base
  let i = 2
  while (taken.has(`${base} ${i}`)) i++
  return `${base} ${i}`
}

export function cleanGroupName(name: string): string {
  return name.trim().slice(0, MAX_GROUP_NAME)
}

/** Puts the cards in a new group. A card that was in another group moves over; a group left empty disappears. Returns the new group's id. */
export function groupNodes(draft: Draft, ids: string[], name?: string): { draft: Draft; groupId: string | null } {
  const members = draft.nodes.filter((n) => ids.includes(n.id)).map((n) => n.id)
  if (members.length < 2) return { draft, groupId: null }
  const rest = draft.groups.map((g) => ({ ...g, nodeIds: g.nodeIds.filter((id) => !members.includes(id)) })).filter((g) => g.nodeIds.length > 0)
  const group: DraftGroup = { id: newId('g'), name: cleanGroupName(name ?? '') || uniqueGroupName(rest), nodeIds: members }
  return { draft: { ...draft, groups: [...rest, group] }, groupId: group.id }
}

/** The cards stay; only the group goes. */
export function ungroup(draft: Draft, groupId: string): Draft {
  return { ...draft, groups: draft.groups.filter((g) => g.id !== groupId) }
}

export function renameGroup(draft: Draft, groupId: string, name: string): Draft {
  return { ...draft, groups: draft.groups.map((g) => (g.id === groupId ? { ...g, name: name.slice(0, MAX_GROUP_NAME) } : g)) }
}

/** Deleting a group deletes its cards (and every connection to them). */
export function removeGroupWithCards(draft: Draft, groupId: string): Draft {
  const g = draft.groups.find((i) => i.id === groupId)
  return g ? removeNodes(draft, g.nodeIds) : draft
}

/** The name for a copy: "Script" → "Script 2" (and "Script 2" → "Script 3"), kept inside the length the backend allows. */
export function copyName(taken: string[], name: string, type: string): string {
  const max = type === DATA_CARD ? MAX_DATA_CARD_NAME : MAX_CARD_NAME
  const base = name.replace(/ \d+$/, '') || name
  const used = new Set(taken)
  for (let i = 2; ; i++) {
    const suffix = ` ${i}`
    const candidate = base.slice(0, max - suffix.length) + suffix
    if (!used.has(candidate)) return candidate
  }
}

function cloneNode(n: DraftNode, names: string[], offset: Point): DraftNode {
  return {
    id: newId('n'),
    type: n.type,
    name: copyName(names, n.name, n.type),
    position: { x: n.position.x + offset.x, y: n.position.y + offset.y },
    ...(n.size ? { size: { ...n.size } } : {}),
    params: JSON.parse(JSON.stringify(n.params)),
  }
}

/** Copies of the cards, next to the originals and WITHOUT any connection. A copy of a card of a group is not in the group. */
export function copyCards(draft: Draft, ids: string[], offset: Point = { x: 40, y: 40 }): { draft: Draft; newIds: string[] } {
  const names = draft.nodes.map((n) => n.name)
  const copies: DraftNode[] = []
  for (const n of draft.nodes.filter((i) => ids.includes(i.id))) {
    const c = cloneNode(n, names, offset)
    names.push(c.name)
    copies.push(c)
  }
  return { draft: { ...draft, nodes: [...draft.nodes, ...copies] }, newIds: copies.map((c) => c.id) }
}

/** A copy of a group: new cards, new group, and the connections that run INSIDE the group (both ends in it). Nothing outside is connected. */
export function copyGroup(draft: Draft, groupId: string, offset: Point): { draft: Draft; groupId: string | null; newIds: string[] } {
  const group = draft.groups.find((g) => g.id === groupId)
  if (!group) return { draft, groupId: null, newIds: [] }
  const names = draft.nodes.map((n) => n.name)
  const mapping = new Map<string, string>()
  const copies: DraftNode[] = []
  for (const n of draft.nodes.filter((i) => group.nodeIds.includes(i.id))) {
    const c = cloneNode(n, names, offset)
    names.push(c.name)
    mapping.set(n.id, c.id)
    copies.push(c)
  }
  const edges = draft.edges
    .filter((e) => mapping.has(e.source) && mapping.has(e.target))
    .map((e) => ({ id: newId('e'), source: mapping.get(e.source)!, sourceHandle: e.sourceHandle, target: mapping.get(e.target)! }))
  const copy: DraftGroup = { id: newId('g'), name: uniqueGroupName(draft.groups, `${group.name} copy`.slice(0, MAX_GROUP_NAME)), nodeIds: copies.map((c) => c.id) }
  return { draft: { ...draft, nodes: [...draft.nodes, ...copies], edges: [...draft.edges, ...edges], groups: [...draft.groups, copy] }, groupId: copy.id, newIds: copies.map((c) => c.id) }
}

/** How many connections a delete would take with it (at least one end among the cards). */
export function connectionsTouching(draft: Draft, ids: string[]): number {
  const set = new Set(ids)
  return draft.edges.filter((e) => set.has(e.source) || set.has(e.target)).length
}
