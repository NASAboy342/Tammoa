import { describe, expect, it } from 'vitest'
import { MAX_ZOOM, MIN_ZOOM, NODE_H, NODE_W, edgePath, inputPort, loopEdgePath, outputPort, screenToWorld, zoomAt } from '../canvasMath'

describe('canvas math', () => {
  it('maps screen to world through pan and zoom', () => {
    expect(screenToWorld({ x: 140, y: 90 }, { x: 40, y: 10, k: 2 })).toEqual({ x: 50, y: 40 })
    expect(screenToWorld({ x: 5, y: 5 }, { x: 0, y: 0, k: 1 })).toEqual({ x: 5, y: 5 })
  })

  it('zooming keeps the world point under the cursor fixed', () => {
    const v = { x: 30, y: -20, k: 1 }
    const anchor = { x: 200, y: 150 }
    const before = screenToWorld(anchor, v)
    const after = screenToWorld(anchor, zoomAt(v, 1.5, anchor))
    expect(after.x).toBeCloseTo(before.x)
    expect(after.y).toBeCloseTo(before.y)
  })

  it('clamps zoom', () => {
    expect(zoomAt({ x: 0, y: 0, k: 1 }, 1000, { x: 0, y: 0 }).k).toBe(MAX_ZOOM)
    expect(zoomAt({ x: 0, y: 0, k: 1 }, 0.0001, { x: 0, y: 0 }).k).toBe(MIN_ZOOM)
  })

  it('ports sit on the left/right middle of a card', () => {
    expect(inputPort({ x: 10, y: 20 })).toEqual({ x: 10, y: 20 + NODE_H / 2 })
    expect(outputPort({ x: 10, y: 20 })).toEqual({ x: 10 + NODE_W, y: 20 + NODE_H / 2 })
  })

  it('several output handles are spread evenly down the right edge', () => {
    const at = { x: 0, y: 0 }
    expect(outputPort(at, 0, 1).y).toBe(NODE_H / 2)
    const [a, b] = [outputPort(at, 0, 2), outputPort(at, 1, 2)]
    expect(a.y).toBeCloseTo(NODE_H / 3)
    expect(b.y).toBeCloseTo((NODE_H * 2) / 3)
    expect(a.x).toBe(NODE_W)
  })

  it('ports follow the size of the card: right edge and middle height of a bigger card', () => {
    expect(outputPort({ x: 10, y: 20 }, 0, 1, { w: 300, h: 100 })).toEqual({ x: 310, y: 70 })
    expect(inputPort({ x: 10, y: 20 }, { w: 300, h: 100 })).toEqual({ x: 10, y: 70 })
    const [a, b] = [outputPort({ x: 0, y: 0 }, 0, 2, { w: 200, h: 120 }), outputPort({ x: 0, y: 0 }, 1, 2, { w: 200, h: 120 })]
    expect([a.y, b.y]).toEqual([40, 80])
  })

  it('a loop-back leaves the right of a later card, passes UNDER the cards, and enters the left of the earlier one', () => {
    const from = { x: 700, y: 30 }
    const to = { x: 250, y: 30 }
    const path = loopEdgePath(from, to, 60)
    expect(path).toMatch(/^M 700 30 C /)
    expect(path).toMatch(/250 30$/)
    const ys = [...path.matchAll(/ (\d+(?:\.\d+)?)(?:,|$)/g)].map((m) => Number(m[1]))
    expect(Math.max(...ys)).toBeGreaterThan(60) // below the lowest card edge given
    expect(loopEdgePath(from, to, 60, 1)).not.toBe(path) // a second loop-back runs in its own lane
    expect(Number(loopEdgePath(from, to, 60, 1).match(/ (\d+) (\d+), /)?.[2])).toBeGreaterThan(0)
  })

  it('edge path starts at the first point and ends at the second, also for back-links', () => {
    expect(edgePath({ x: 0, y: 0 }, { x: 100, y: 50 })).toMatch(/^M 0 0 C .* 100 50$/)
    expect(edgePath({ x: 300, y: 0 }, { x: 0, y: 0 })).toMatch(/^M 300 0 C .* 0 0$/)
  })
})
