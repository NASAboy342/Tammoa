import { DEFAULT_SIZE, type Size } from './canvasMath'

/**
 * How big a card is: the size the user dragged it to, or else just big enough for its title. The title makes the card wider first
 * (up to MAX_AUTO_W), then wraps onto more lines and makes it taller.
 */
export const MAX_AUTO_W = 420
export const MIN_MANUAL = { w: 140, h: 56 }
export const MAX_MANUAL = { w: 900, h: 700 }

const LINE_HEIGHT = 16
// space a card needs around its title: left padding + icon box + gaps + status icon + right padding (+ room for the labels of several outputs)
const CHROME_W = 12 + 32 + 10 + 10 + 15 + 12
const MULTI_OUTPUT_W = 40
const BASE_H = 24 + 14 // two text rows' padding + the type label row

export type TextMeasure = (text: string) => number

const cache = new Map<string, number>()

/** Width in pixels of a card title (12px, semibold) in the app's own font; falls back to an estimate where there is no canvas (tests). */
export const measureTitle: TextMeasure = (text) => {
  const hit = cache.get(text)
  if (hit !== undefined) return hit
  let width = text.length * 6.9
  try {
    const ctx = document.createElement('canvas').getContext('2d')
    if (ctx) {
      ctx.font = `600 12px ${getComputedStyle(document.body).fontFamily || 'sans-serif'}`
      const m = ctx.measureText(text).width
      if (m > 0) width = m
    }
  } catch {
    /* no canvas: the estimate is fine */
  }
  cache.set(text, width)
  return width
}

export function autoSize(title: string, outputs = 1, measure: TextMeasure = measureTitle): Size {
  const textW = Math.ceil(measure(title)) + 4 // a little slack: fonts differ between measuring and drawing
  const chrome = CHROME_W + (outputs > 1 ? MULTI_OUTPUT_W : 0)
  const wanted = textW + chrome
  if (wanted <= MAX_AUTO_W) return { w: Math.max(DEFAULT_SIZE.w, roundUp(wanted)), h: DEFAULT_SIZE.h }
  const inner = MAX_AUTO_W - chrome
  const lines = Math.min(4, Math.ceil((textW * 1.1) / inner)) // 10% for words that do not fill a line
  return { w: MAX_AUTO_W, h: Math.max(DEFAULT_SIZE.h, roundUp(BASE_H + lines * LINE_HEIGHT)) }
}

const roundUp = (n: number) => Math.ceil(n / 4) * 4

/** The size a card is drawn at. */
export function sizeOf(node: { name: string; size?: Size }, outputs = 1, measure: TextMeasure = measureTitle): Size {
  return node.size ? clampManual(node.size) : autoSize(node.name, outputs, measure)
}

export function clampManual(size: Size): Size {
  return {
    w: Math.round(Math.min(MAX_MANUAL.w, Math.max(MIN_MANUAL.w, size.w))),
    h: Math.round(Math.min(MAX_MANUAL.h, Math.max(MIN_MANUAL.h, size.h))),
  }
}

/** How many title lines fit in a card of this height (the rest is cut with an ellipsis, the full title stays in the tooltip). */
export const titleLines = (height: number) => Math.max(1, Math.floor((height - BASE_H) / LINE_HEIGHT))
