import { describe, expect, it } from 'vitest'
import type { CardTypeInfo } from '@/services/cardApi'
import {
  canConnect, defaultParams, freeSpot, hasTrigger, makeNode, removeEdge, removeNode, toDefinition, toDraft, uniqueName, type Draft,
} from '../flowDraft'

const info = (type: string, isTrigger = false, params: CardTypeInfo['params'] = []): CardTypeInfo => ({
  type, name: type === 'trigger.manual' ? 'Manual trigger' : 'Script', description: '', category: isTrigger ? 'trigger' : 'action', icon: 'zap', isTrigger, params, outputs: [{ key: 'main', label: '' }],
})
const types = { 'trigger.manual': info('trigger.manual', true), 'script.shell': info('script.shell') }
const node = (id: string, type = 'script.shell') => ({ id, type, name: id, position: { x: 0, y: 0 }, params: {} })
const e = (id: string, source: string, target: string, sourceHandle = 'main') => ({ id, source, sourceHandle, target })
const draft = (): Draft => ({
  nodes: [node('t', 'trigger.manual'), node('a'), node('b')],
  edges: [e('e1', 't', 'a'), e('e2', 'a', 'b')],
  groups: [],
})

describe('toDraft / toDefinition', () => {
  it('reads tolerant input: missing arrays, names and positions', () => {
    expect(toDraft(undefined)).toEqual({ nodes: [], edges: [], groups: [] })
    const d = toDraft({ nodes: [{ id: 'x', type: 'script.shell' }], edges: [{ source: 'x', target: 'y' }] })
    expect(d.nodes[0]).toMatchObject({ id: 'x', name: 'script.shell', position: { x: 0, y: 0 }, params: {} })
    expect(d.edges[0].id).toMatch(/^e-/)
    expect(d.edges[0].sourceHandle).toBe('main')
  })

  it('writes back keeping unknown keys such as schemaVersion', () => {
    const def = toDefinition({ schemaVersion: 1, future: true, nodes: [], edges: [] }, draft())
    expect(def.schemaVersion).toBe(1)
    expect(def.future).toBe(true)
    expect(def.nodes).toHaveLength(3)
    expect(def.edges).toHaveLength(2)
  })

  it('round-trips', () => {
    const d = draft()
    expect(toDraft(toDefinition(undefined, d))).toEqual(d)
  })
})

describe('nodes', () => {
  it('uniqueName numbers duplicates', () => {
    expect(uniqueName([], 'Script')).toBe('Script')
    expect(uniqueName([node('Script')], 'Script')).toBe('Script 2')
    expect(uniqueName([node('Script'), node('Script 2')], 'Script')).toBe('Script 3')
  })

  it('makeNode uses the type defaults, a unique name and rounded position', () => {
    const withDefaults = info('script.shell', false, [
      { key: 'timeoutSeconds', label: 't', kind: 'number', defaultValue: 30 },
      { key: 'command', label: 'c', kind: 'textarea' },
    ])
    expect(defaultParams(withDefaults)).toEqual({ timeoutSeconds: 30 })
    const n = makeNode(withDefaults, [node('Script')], { x: 10.6, y: 3.2 })
    expect(n.name).toBe('Script 2')
    expect(n.position).toEqual({ x: 11, y: 3 })
    expect(n.params).toEqual({ timeoutSeconds: 30 })
    expect(n.id).toMatch(/^n-/)
  })

  it('removing a card removes its connections; removing a connection keeps the cards', () => {
    const d = removeNode(draft(), 'a')
    expect(d.nodes.map((n) => n.id)).toEqual(['t', 'b'])
    expect(d.edges).toEqual([])
    const e = removeEdge(draft(), 'e1')
    expect(e.nodes).toHaveLength(3)
    expect(e.edges.map((x) => x.id)).toEqual(['e2'])
  })
})

describe('canConnect', () => {
  it('allows a normal link', () => expect(canConnect(draft(), types, 't', 'b')).toEqual({ ok: true }))
  it('refuses self links, unknown cards, triggers as targets and duplicates; a connection back to an earlier card is allowed (a loop)', () => {
    expect(canConnect(draft(), types, 'a', 'a').ok).toBe(false)
    expect(canConnect(draft(), types, 'a', 'zzz').ok).toBe(false)
    expect(canConnect(draft(), types, 'a', 't').ok).toBe(false)
    expect(canConnect(draft(), types, 't', 'a').ok).toBe(false)
    expect(canConnect(draft(), types, 'b', 'a')).toEqual({ ok: true }) // b -> a goes back: a loop
    expect(canConnect(draft(), types, 'b', 't').ok).toBe(false) // but never into a trigger
  })
})

describe('graph helpers', () => {
  it('hasTrigger', () => {
    expect(hasTrigger(draft(), types)).toBe(true)
    expect(hasTrigger({ nodes: [node('a')], edges: [], groups: [] }, types)).toBe(false)
  })
})

describe('card size and the loop limit are kept in the definition', () => {
  it('reads a manual size and the loop limit, ignores nonsense', () => {
    const d = toDraft({ settings: { loopLimit: 7 }, nodes: [{ id: 'a', type: 'x', size: { w: 300, h: 90 } }, { id: 'b', type: 'x', size: { w: 'wide' } }, { id: 'c', type: 'x' }] })
    expect(d.nodes[0].size).toEqual({ w: 300, h: 90 })
    expect(d.nodes[1].size).toBeUndefined()
    expect(d.nodes[2].size).toBeUndefined()
    expect(d.loopLimit).toBe(7)
    expect(toDraft({ nodes: [] }).loopLimit).toBeUndefined()
  })

  it('writes them back, and leaves settings out when there is nothing to say', () => {
    const d = draft()
    d.nodes[1].size = { w: 320, h: 80 }
    expect(toDefinition(undefined, d).nodes?.[1]).toMatchObject({ size: { w: 320, h: 80 } })
    expect((toDefinition(undefined, d).nodes?.[0] as { size?: unknown }).size).toBeUndefined()
    expect(toDefinition(undefined, d).settings).toBeUndefined()
    d.loopLimit = 12
    expect(toDefinition(undefined, d).settings).toEqual({ loopLimit: 12 })
  })

  it('keeps other settings the UI does not know, and removes the loop limit when it is cleared', () => {
    const base = { settings: { loopLimit: 9, future: true }, nodes: [], edges: [] }
    const cleared = toDefinition(base, { nodes: [], edges: [], groups: [] })
    expect(cleared.settings).toEqual({ future: true })
    expect(toDefinition(base, { nodes: [], edges: [], groups: [], loopLimit: 20 }).settings).toEqual({ future: true, loopLimit: 20 })
  })

  it('removing a card or a connection keeps the loop limit', () => {
    const d = { ...draft(), loopLimit: 5 }
    expect(removeNode(d, 'a').loopLimit).toBe(5)
    expect(removeEdge(d, 'e1').loopLimit).toBe(5)
  })

  it('round-trips a loop and a size', () => {
    const d = { ...draft(), loopLimit: 3 }
    d.nodes[2].size = { w: 250, h: 70 }
    d.edges.push(e('back', 'b', 'a'))
    expect(toDraft(toDefinition(undefined, d))).toEqual(d)
  })
})

describe('freeSpot', () => {
  it('returns the start when nothing is there', () => expect(freeSpot([], { x: 5, y: 7 })).toEqual({ x: 5, y: 7 }))
  it('moves right of an occupied spot, never overlapping, and wraps to a new row', () => {
    const first = freeSpot([], { x: 0, y: 0 })
    const nodes = [{ ...node('a'), position: first }]
    const second = freeSpot(nodes, { x: 0, y: 0 })
    expect(second.x).toBeGreaterThan(first.x + 208)
    expect(second.y).toBe(first.y)
    const many = [0, 1, 2, 3].map((i) => ({ ...node(`n${i}`), position: { x: i * 256, y: 0 } }))
    const wrapped = freeSpot(many, { x: 0, y: 0 })
    expect(wrapped).toEqual({ x: 0, y: 100 })
  })
})

describe('output handles', () => {
  const iff: CardTypeInfo = { ...info('logic.if'), name: 'IF', category: 'logic', outputs: [{ key: 'true', label: 'true' }, { key: 'false', label: 'false' }] }
  const typesWithIf = { ...types, 'logic.if': iff }
  const d = (): Draft => ({ nodes: [node('t', 'trigger.manual'), node('i', 'logic.if'), node('a'), node('b')], edges: [e('e1', 't', 'i')], groups: [] })

  it('reads and keeps the handle a connection leaves from', () => {
    const back = toDraft(toDefinition(undefined, { nodes: [node('i', 'logic.if'), node('a')], edges: [e('x', 'i', 'a', 'false')], groups: [] }))
    expect(back.edges[0].sourceHandle).toBe('false')
  })

  it('allows a connection from each handle, also to the same target, but not the same handle twice', () => {
    const draft = d()
    expect(canConnect(draft, typesWithIf, 'i', 'a', 'true')).toEqual({ ok: true })
    draft.edges.push(e('e2', 'i', 'a', 'true'))
    expect(canConnect(draft, typesWithIf, 'i', 'a', 'false')).toEqual({ ok: true })
    expect(canConnect(draft, typesWithIf, 'i', 'a', 'true')).toMatchObject({ ok: false, reason: 'Already connected.' })
  })

  it('refuses a handle the card does not have', () => {
    expect(canConnect(d(), typesWithIf, 'i', 'a', 'maybe')).toMatchObject({ ok: false })
    expect(canConnect(d(), typesWithIf, 'a', 'b', 'true')).toMatchObject({ ok: false })
  })
})
