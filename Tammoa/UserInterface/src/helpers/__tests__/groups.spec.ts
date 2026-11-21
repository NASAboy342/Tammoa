import { describe, expect, it } from 'vitest'
import { toDefinition, toDraft, removeNodes, type Draft, type DraftNode } from '../flowDraft'
import {
  connectionsTouching, copyCards, copyGroup, copyName, groupBox, groupNodes, nodesInRect, removeGroupWithCards, renameGroup, ungroup, uniqueGroupName,
} from '../groups'

const node = (id: string, x = 0, y = 0, type = 'script.shell', name = id): DraftNode => ({ id, type, name, position: { x, y }, params: { command: `echo ${id}`, nested: { list: [1] } } })
const e = (id: string, source: string, target: string) => ({ id, source, sourceHandle: 'main', target })
const size = () => ({ w: 200, h: 60 })
const base = (): Draft => ({
  nodes: [node('a', 0, 0), node('b', 300, 0), node('c', 600, 0), node('d', 0, 200)],
  edges: [e('e1', 'a', 'b'), e('e2', 'b', 'c'), e('e3', 'c', 'd')],
  groups: [],
})

describe('grouping', () => {
  it('groups two or more cards, with a default name; one card is not a group', () => {
    expect(groupNodes(base(), ['a']).groupId).toBeNull()
    const r = groupNodes(base(), ['a', 'b'])
    expect(r.draft.groups).toEqual([{ id: r.groupId, name: 'Group', nodeIds: ['a', 'b'] }])
    expect(groupNodes(r.draft, ['c', 'd']).draft.groups.map((g) => g.name)).toEqual(['Group', 'Group 2'])
  })

  it('a card that joins a new group leaves its old one; an emptied group disappears', () => {
    const first = groupNodes(base(), ['a', 'b']).draft
    const second = groupNodes(first, ['a', 'c'])
    expect(second.draft.groups.map((g) => g.nodeIds)).toEqual([['b'], ['a', 'c']])
    const third = groupNodes(second.draft, ['b', 'd'])
    expect(third.draft.groups.map((g) => g.nodeIds)).toEqual([['a', 'c'], ['b', 'd']])
  })

  it('ungroup removes only the group; rename trims to the limit', () => {
    const r = groupNodes(base(), ['a', 'b'])
    expect(ungroup(r.draft, r.groupId!).groups).toEqual([])
    expect(ungroup(r.draft, r.groupId!).nodes).toHaveLength(4)
    expect(renameGroup(r.draft, r.groupId!, 'x'.repeat(80)).groups[0].name).toHaveLength(60)
    expect(uniqueGroupName([{ id: '1', name: 'Group', nodeIds: [] }])).toBe('Group 2')
  })

  it('the frame wraps the member cards with room for the name, and follows them', () => {
    const g = { id: 'g', name: 'G', nodeIds: ['a', 'b'] }
    const box = groupBox(g, base().nodes, size)!
    expect(box).toEqual({ x: -16, y: -34, w: 300 + 200 + 32, h: 60 + 34 + 16 })
    expect(groupBox({ id: 'g', name: 'G', nodeIds: [] }, base().nodes, size)).toBeNull()
  })

  it('deleting a card takes it out of its group; deleting the last card removes the group', () => {
    const d = groupNodes(base(), ['a', 'b']).draft
    expect(removeNodes(d, ['a']).groups[0].nodeIds).toEqual(['b'])
    expect(removeNodes(d, ['a', 'b']).groups).toEqual([])
  })

  it('deleting a group deletes its cards and their connections', () => {
    const r = groupNodes(base(), ['b', 'c'])
    const d = removeGroupWithCards(r.draft, r.groupId!)
    expect(d.nodes.map((n) => n.id)).toEqual(['a', 'd'])
    expect(d.edges).toEqual([])
    expect(d.groups).toEqual([])
  })

  it('counts the connections a delete would take', () => {
    expect(connectionsTouching(base(), ['b'])).toBe(2)
    expect(connectionsTouching(base(), ['a', 'b', 'c', 'd'])).toBe(3)
    expect(connectionsTouching(base(), [])).toBe(0)
  })
})

describe('selection rectangle', () => {
  it('selects every card it touches, even partly', () => {
    expect(nodesInRect(base().nodes, { x: 250, y: -10, w: 100, h: 30 }, size)).toEqual(['b'])
    expect(nodesInRect(base().nodes, { x: -10, y: -10, w: 2000, h: 100 }, size)).toEqual(['a', 'b', 'c'])
    expect(nodesInRect(base().nodes, { x: 1000, y: 1000, w: 10, h: 10 }, size)).toEqual([])
  })
})

describe('copying cards', () => {
  it('copies the card with its settings, a new id and a new name, and NO connection', () => {
    const r = copyCards(base(), ['b'])
    expect(r.newIds).toHaveLength(1)
    const copy = r.draft.nodes.find((n) => n.id === r.newIds[0])!
    expect(copy).toMatchObject({ type: 'script.shell', name: 'b 2', position: { x: 340, y: 40 }, params: { command: 'echo b' } })
    expect(copy.id).not.toBe('b')
    expect(r.draft.edges).toEqual(base().edges) // nothing connected to the copy
    expect(r.draft.nodes).toHaveLength(5)
  })

  it('the copy does not share its settings with the original', () => {
    const r = copyCards(base(), ['a'])
    const copy = r.draft.nodes.find((n) => n.id === r.newIds[0])!
    ;(copy.params.nested as { list: number[] }).list.push(2)
    expect((base().nodes[0].params.nested as { list: number[] }).list).toEqual([1])
    const original = r.draft.nodes.find((n) => n.id === 'a')!
    expect((original.params.nested as { list: number[] }).list).toEqual([1])
  })

  it('copies several cards at once with unique names, keeps a manual size, and the copies are not in the group', () => {
    const d = groupNodes({ ...base(), nodes: base().nodes.map((n) => (n.id === 'a' ? { ...n, size: { w: 300, h: 90 } } : n)) }, ['a', 'b']).draft
    const r = copyCards(d, ['a', 'b'])
    expect(r.draft.nodes.slice(4).map((n) => n.name)).toEqual(['a 2', 'b 2'])
    expect(r.draft.nodes[4].size).toEqual({ w: 300, h: 90 })
    expect(r.draft.groups[0].nodeIds).toEqual(['a', 'b'])
    expect(copyCards(r.draft, ['a']).draft.nodes.at(-1)!.name).toBe('a 3')
  })

  it('names stay inside the limits (a Data card name is limited to 60) and numbers count up', () => {
    expect(copyName(['Script'], 'Script', 'script.shell')).toBe('Script 2')
    expect(copyName(['Script', 'Script 2'], 'Script 2', 'script.shell')).toBe('Script 3')
    const long = 'n'.repeat(60)
    expect(copyName([long], long, 'data.store').length).toBeLessThanOrEqual(60)
    expect(copyName([long], long, 'data.store')).not.toBe(long)
  })
})

describe('copying a group', () => {
  it('copies the cards, a new group, and the connections INSIDE it only', () => {
    const r0 = groupNodes(base(), ['a', 'b', 'c'], 'Prepare')
    const r = copyGroup(r0.draft, r0.groupId!, { x: 0, y: 150 })
    expect(r.newIds).toHaveLength(3)
    expect(r.draft.groups.map((g) => g.name)).toEqual(['Prepare', 'Prepare copy'])
    const copies = r.draft.nodes.slice(4)
    expect(copies.map((n) => n.position.y)).toEqual([150, 150, 150])
    const newEdges = r.draft.edges.slice(3)
    expect(newEdges).toHaveLength(2) // a→b and b→c; c→d leaves the group and is not copied
    const [x, y, z] = r.newIds
    expect(newEdges.map((i) => [i.source, i.target])).toEqual([[x, y], [y, z]])
    expect(new Set(r.draft.edges.map((i) => i.id)).size).toBe(5) // new ids
    expect(r.draft.edges.slice(0, 3)).toEqual(base().edges) // originals untouched
    expect(r.draft.groups[1].nodeIds).toEqual(r.newIds)
  })

  it('keeps the output a connection leaves from, and a second copy gets its own name', () => {
    const d = base()
    d.edges[0] = { ...d.edges[0], sourceHandle: 'true' }
    const g = groupNodes(d, ['a', 'b'])
    const once = copyGroup(g.draft, g.groupId!, { x: 0, y: 100 })
    expect(once.draft.edges.at(-1)!.sourceHandle).toBe('true')
    const twice = copyGroup(once.draft, g.groupId!, { x: 0, y: 200 })
    expect(twice.draft.groups.map((i) => i.name)).toEqual(['Group', 'Group copy', 'Group copy 2'])
  })

  it('an unknown group changes nothing', () => {
    const r = copyGroup(base(), 'nope', { x: 0, y: 0 })
    expect(r.groupId).toBeNull()
    expect(r.draft).toEqual(base())
  })
})

describe('saving groups', () => {
  it('writes them into the definition and reads them back; no groups = no key', () => {
    const r = groupNodes(base(), ['a', 'b'], 'Pair')
    const def = toDefinition({ schemaVersion: 1 }, r.draft)
    expect(def.groups).toEqual([{ id: r.groupId, name: 'Pair', nodeIds: ['a', 'b'] }])
    expect(toDraft(def).groups).toEqual(r.draft.groups)
    expect('groups' in toDefinition(def, base())).toBe(false)
  })

  it('drops what points at missing cards, and a card claimed by two groups stays in the first', () => {
    const def = {
      nodes: [node('a'), node('b')],
      edges: [],
      groups: [{ id: 'g1', name: 'One', nodeIds: ['a', 'ghost'] }, { id: 'g2', name: 'Two', nodeIds: ['a', 'b'] }, { id: 'bad' }, 'junk'],
    }
    expect(toDraft(def).groups).toEqual([{ id: 'g1', name: 'One', nodeIds: ['a'] }, { id: 'g2', name: 'Two', nodeIds: ['b'] }])
  })
})
