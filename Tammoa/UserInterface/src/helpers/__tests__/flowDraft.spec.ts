import { describe, expect, it } from 'vitest'
import type { CardTypeInfo } from '@/services/cardApi'
import {
  canConnect, defaultParams, freeSpot, hasTrigger, makeNode, reaches, removeEdge, removeNode, toDefinition, toDraft, uniqueName, type Draft,
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
})

describe('toDraft / toDefinition', () => {
  it('reads tolerant input: missing arrays, names and positions', () => {
    expect(toDraft(undefined)).toEqual({ nodes: [], edges: [] })
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
  it('refuses self links, unknown cards, triggers as targets, duplicates and loops', () => {
    expect(canConnect(draft(), types, 'a', 'a').ok).toBe(false)
    expect(canConnect(draft(), types, 'a', 'zzz').ok).toBe(false)
    expect(canConnect(draft(), types, 'a', 't').ok).toBe(false)
    expect(canConnect(draft(), types, 't', 'a').ok).toBe(false)
    const loop = canConnect(draft(), types, 'b', 'a')
    expect(loop).toMatchObject({ ok: false })
    expect((loop as { reason: string }).reason).toMatch(/loop/i)
  })
})

describe('graph helpers', () => {
  it('reaches follows connections one way', () => {
    const d = draft()
    expect(reaches(d.edges, 't', 'b')).toBe(true)
    expect(reaches(d.edges, 'b', 't')).toBe(false)
    expect(reaches(d.edges, 'a', 'a')).toBe(true)
  })
  it('hasTrigger', () => {
    expect(hasTrigger(draft(), types)).toBe(true)
    expect(hasTrigger({ nodes: [node('a')], edges: [] }, types)).toBe(false)
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
  const d = (): Draft => ({ nodes: [node('t', 'trigger.manual'), node('i', 'logic.if'), node('a'), node('b')], edges: [e('e1', 't', 'i')] })

  it('reads and keeps the handle a connection leaves from', () => {
    const back = toDraft(toDefinition(undefined, { nodes: [node('i', 'logic.if'), node('a')], edges: [e('x', 'i', 'a', 'false')] }))
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
