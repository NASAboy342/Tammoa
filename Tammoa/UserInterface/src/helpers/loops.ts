import type { CardTypeInfo } from '@/services/cardApi'
import type { Draft } from './flowDraft'

/**
 * Loops in a flow: a connection that points back to a card that is still being walked (depth-first from the start cards) is a
 * loop-back. The backend engine classifies connections with the same algorithm; the canvas needs it to draw loop-backs differently.
 */
export interface LoopInfo {
  /** ids of the connections that go back to an earlier card */
  backEdgeIds: Set<string>
  /** ids of the cards that are part of some loop */
  loopNodeIds: Set<string>
  /** the cards of each loop (flow order) */
  loops: string[][]
}

export function findLoops(draft: Pick<Draft, 'nodes' | 'edges'>): LoopInfo {
  const ids = draft.nodes.map((n) => n.id)
  const known = new Set(ids)
  const next = new Map<string, string[]>(ids.map((i) => [i, []]))
  const hasIncoming = new Set<string>()
  for (const e of draft.edges) {
    if (!known.has(e.source) || !known.has(e.target)) continue
    if (!next.get(e.source)!.includes(e.target)) next.get(e.source)!.push(e.target)
    hasIncoming.add(e.target)
  }

  // loop-backs
  const state = new Map<string, 1 | 2>()
  const backPairs = new Set<string>()
  const walk = (u: string) => {
    state.set(u, 1)
    for (const v of next.get(u)!) {
      const sv = state.get(v)
      if (sv === undefined) walk(v)
      else if (sv === 1) backPairs.add(`${u}>${v}`)
    }
    state.set(u, 2)
  }
  for (const id of ids.filter((i) => !hasIncoming.has(i))) if (!state.has(id)) walk(id)
  for (const id of ids) if (!state.has(id)) walk(id)

  // loops = strongly connected components of more than one card (Tarjan)
  const index = new Map<string, number>()
  const low = new Map<string, number>()
  const onStack = new Set<string>()
  const stack: string[] = []
  let counter = 0
  const loops: string[][] = []
  const connect = (v: string) => {
    index.set(v, counter)
    low.set(v, counter)
    counter++
    stack.push(v)
    onStack.add(v)
    for (const w of next.get(v)!) {
      if (!index.has(w)) {
        connect(w)
        low.set(v, Math.min(low.get(v)!, low.get(w)!))
      } else if (onStack.has(w)) low.set(v, Math.min(low.get(v)!, index.get(w)!))
    }
    if (low.get(v) !== index.get(v)) return
    const component: string[] = []
    let x: string
    do {
      x = stack.pop()!
      onStack.delete(x)
      component.push(x)
    } while (x !== v)
    if (component.length > 1) loops.push(ids.filter((i) => component.includes(i)))
  }
  for (const id of ids) if (!index.has(id)) connect(id)

  loops.sort((a, b) => ids.indexOf(a[0]) - ids.indexOf(b[0])) // in flow order, whatever order the walk found them
  const backEdgeIds = new Set(draft.edges.filter((e) => backPairs.has(`${e.source}>${e.target}`)).map((e) => e.id))
  return { backEdgeIds, loopNodeIds: new Set(loops.flat()), loops }
}

/** Does anything in this loop decide when to stop (a card with more than one output, like IF)? */
export function loopHasCondition(loop: string[], draft: Pick<Draft, 'nodes'>, types: Record<string, CardTypeInfo>): boolean {
  return loop.some((id) => {
    const node = draft.nodes.find((n) => n.id === id)
    return (types[node?.type ?? '']?.outputs?.length ?? 1) > 1
  })
}
