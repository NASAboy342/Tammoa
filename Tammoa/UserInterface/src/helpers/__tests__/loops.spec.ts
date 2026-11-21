import { describe, expect, it } from 'vitest'
import type { CardTypeInfo } from '@/services/cardApi'
import type { Draft, DraftEdge, DraftNode } from '../flowDraft'
import { findLoops, loopHasCondition } from '../loops'

const node = (id: string, type = 'act'): DraftNode => ({ id, type, name: id, position: { x: 0, y: 0 }, params: {} })
const edge = (id: string, source: string, target: string, sourceHandle = 'main'): DraftEdge => ({ id, source, target, sourceHandle })
const draft = (ids: string[], ...edges: [string, string][]): Draft => ({ nodes: ids.map((i) => node(i)), edges: edges.map(([s, t], i) => edge(`e${i}`, s, t)), groups: [] })

describe('findLoops (the same classification the backend engine uses)', () => {
  it('a plain flow and a diamond have no loops', () => {
    expect(findLoops(draft(['t', 'a', 'b'], ['t', 'a'], ['a', 'b'])).backEdgeIds.size).toBe(0)
    expect(findLoops(draft(['t', 'a', 'b', 'j'], ['t', 'a'], ['t', 'b'], ['a', 'j'], ['b', 'j'])).loops).toEqual([])
  })

  it('the connection back to an earlier card is the loop-back; the cards between form the loop', () => {
    const d = draft(['t', 'a', 'b', 'c', 'x'], ['t', 'a'], ['a', 'b'], ['b', 'c'], ['c', 'a'], ['c', 'x'])
    const info = findLoops(d)
    expect([...info.backEdgeIds]).toEqual(['e3']) // c -> a
    expect(info.loops).toEqual([['a', 'b', 'c']])
    expect([...info.loopNodeIds].sort()).toEqual(['a', 'b', 'c'])
  })

  it('a loop-back may jump over several cards, or go back just one', () => {
    expect([...findLoops(draft(['t', 'a', 'b', 'c'], ['t', 'a'], ['a', 'b'], ['b', 'c'], ['c', 'a'])).backEdgeIds]).toEqual(['e3'])
    expect([...findLoops(draft(['t', 'a', 'b', 'c'], ['t', 'a'], ['a', 'b'], ['b', 'c'], ['c', 'b'])).backEdgeIds]).toEqual(['e3'])
  })

  it('two loops and a nested loop', () => {
    const two = findLoops(draft(['t', 'a', 'b', 'c', 'd'], ['t', 'a'], ['a', 'b'], ['b', 'a'], ['b', 'c'], ['c', 'd'], ['d', 'c']))
    expect(two.loops).toEqual([['a', 'b'], ['c', 'd']])
    expect(two.backEdgeIds.size).toBe(2)
    const nested = findLoops(draft(['t', 'a', 'b', 'c'], ['t', 'a'], ['a', 'b'], ['b', 'c'], ['c', 'b'], ['c', 'a']))
    expect(nested.loops).toEqual([['a', 'b', 'c']])
    expect(nested.backEdgeIds.size).toBe(2)
  })

  it('two connections between the same pair (e.g. an IF true and false) are both loop-backs', () => {
    const d: Draft = { nodes: ['t', 'a', 'i'].map((i) => node(i)), edges: [edge('1', 't', 'a'), edge('2', 'a', 'i'), edge('3', 'i', 'a', 'true'), edge('4', 'i', 'a', 'false')], groups: [] }
    expect([...findLoops(d).backEdgeIds].sort()).toEqual(['3', '4'])
  })

  it('ignores connections to unknown cards, and finds a loop nothing starts from', () => {
    expect(findLoops({ nodes: [node('a')], edges: [edge('1', 'a', 'ghost'), edge('2', 'ghost', 'a')] }).loops).toEqual([])
    expect(findLoops(draft(['a', 'b'], ['a', 'b'], ['b', 'a'])).loops).toEqual([['a', 'b']])
  })

  it('loop members keep the flow order', () => {
    expect(findLoops(draft(['t', 'c', 'a', 'b'], ['t', 'a'], ['a', 'b'], ['b', 'c'], ['c', 'a'])).loops).toEqual([['c', 'a', 'b']])
  })
})

describe('loopHasCondition', () => {
  const iff: CardTypeInfo = { type: 'if', name: 'IF', description: '', category: 'logic', icon: 'split', isTrigger: false, params: [], outputs: [{ key: 'true', label: 'true' }, { key: 'false', label: 'false' }] }
  const act: CardTypeInfo = { ...iff, type: 'act', name: 'Act', outputs: [{ key: 'main', label: '' }] }
  const types = { if: iff, act }

  it('a loop with an IF has a condition; one without does not', () => {
    const withIf: Draft = { nodes: [node('a'), node('i', 'if')], edges: [], groups: [] }
    const without: Draft = { nodes: [node('a'), node('b')], edges: [], groups: [] }
    expect(loopHasCondition(['a', 'i'], withIf, types)).toBe(true)
    expect(loopHasCondition(['a', 'b'], without, types)).toBe(false)
  })
})
