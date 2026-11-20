/** Pure geometry for the card canvas (world = canvas coordinates, screen = pixels in the viewport). */
export const NODE_W = 208
export const NODE_H = 60
export const MIN_ZOOM = 0.3
export const MAX_ZOOM = 2

export interface View {
  x: number
  y: number
  k: number
}
export interface Point {
  x: number
  y: number
}

export const screenToWorld = (p: Point, v: View): Point => ({ x: (p.x - v.x) / v.k, y: (p.y - v.y) / v.k })

export const clampZoom = (k: number) => Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, k))

/** Zooms by `factor` keeping the world point under `anchor` (screen coordinates) fixed. */
export function zoomAt(v: View, factor: number, anchor: Point): View {
  const k = clampZoom(v.k * factor)
  const world = screenToWorld(anchor, v)
  return { k, x: anchor.x - world.x * k, y: anchor.y - world.y * k }
}

export const outputPort = (n: Point): Point => ({ x: n.x + NODE_W, y: n.y + NODE_H / 2 })
export const inputPort = (n: Point): Point => ({ x: n.x, y: n.y + NODE_H / 2 })

/** Cubic bezier from an output port to an input port; the pull grows with distance so back-links stay readable. */
export function edgePath(a: Point, b: Point): string {
  const pull = Math.max(40, Math.abs(b.x - a.x) / 2)
  return `M ${a.x} ${a.y} C ${a.x + pull} ${a.y}, ${b.x - pull} ${b.y}, ${b.x} ${b.y}`
}
