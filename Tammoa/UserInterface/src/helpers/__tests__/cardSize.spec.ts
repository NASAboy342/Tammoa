import { describe, expect, it } from 'vitest'
import { DEFAULT_SIZE } from '../canvasMath'
import { MAX_AUTO_W, MAX_MANUAL, MIN_MANUAL, autoSize, clampManual, sizeOf, titleLines } from '../cardSize'

const fixed = (px: number) => () => px // a measurer: every title is this wide

describe('autoSize: just big enough for the title', () => {
  it('a short title keeps the default size', () => expect(autoSize('Add', 1, fixed(30))).toEqual(DEFAULT_SIZE))

  it('a longer title makes the card wider, in steps of 4, up to the maximum width', () => {
    const s = autoSize('x', 1, fixed(200))
    expect(s.w).toBeGreaterThan(DEFAULT_SIZE.w)
    expect(s.w % 4).toBe(0)
    expect(s.h).toBe(DEFAULT_SIZE.h)
    expect(autoSize('x', 1, fixed(250)).w).toBeGreaterThan(s.w)
    expect(autoSize('x', 1, fixed(250)).w).toBeLessThanOrEqual(MAX_AUTO_W)
  })

  it('a title wider than the maximum wraps: the card stops growing wider and grows taller', () => {
    const one = autoSize('x', 1, fixed(600))
    expect(one.w).toBe(MAX_AUTO_W)
    expect(one.h).toBeGreaterThan(DEFAULT_SIZE.h)
    const more = autoSize('x', 1, fixed(1200))
    expect(more.w).toBe(MAX_AUTO_W)
    expect(more.h).toBeGreaterThan(one.h)
  })

  it('never more than four lines tall', () => {
    expect(autoSize('x', 1, fixed(100000)).h).toBe(autoSize('x', 1, fixed(2000)).h)
  })

  it('cards with several outputs keep room for the output labels', () => {
    expect(autoSize('x', 2, fixed(200)).w).toBeGreaterThan(autoSize('x', 1, fixed(200)).w)
  })

  it('the width grows with the title, so a longer name never gives a narrower card', () => {
    let last = 0
    for (const px of [10, 60, 120, 180, 240, 300, 400, 800]) {
      const s = autoSize('x', 1, fixed(px))
      expect(s.w).toBeGreaterThanOrEqual(last)
      last = s.w
    }
  })

  it('uses the real measurer when given text (a longer title measures wider)', () => {
    const perChar = (t: string) => t.length * 7
    expect(autoSize('A very long card title that does not fit in the default width', 1, perChar).w).toBeGreaterThan(autoSize('Short', 1, perChar).w)
  })
})

describe('sizeOf', () => {
  it('a manual size wins over the title, within the limits', () => {
    expect(sizeOf({ name: 'x'.repeat(200), size: { w: 300, h: 90 } }, 1, fixed(2000))).toEqual({ w: 300, h: 90 })
    expect(sizeOf({ name: 'x', size: { w: 5, h: 5 } })).toEqual(MIN_MANUAL)
    expect(sizeOf({ name: 'x', size: { w: 99999, h: 99999 } })).toEqual(MAX_MANUAL)
  })
  it('without a manual size it follows the title', () => expect(sizeOf({ name: 'x' }, 1, fixed(300)).w).toBeGreaterThan(DEFAULT_SIZE.w))
})

describe('clampManual / titleLines', () => {
  it('rounds to whole pixels and clamps', () => expect(clampManual({ w: 200.6, h: 80.2 })).toEqual({ w: 201, h: 80 }))
  it('a taller card shows more title lines (at least one)', () => {
    expect(titleLines(56)).toBe(1)
    expect(titleLines(DEFAULT_SIZE.h)).toBe(1)
    expect(titleLines(120)).toBeGreaterThan(titleLines(DEFAULT_SIZE.h))
    expect(titleLines(10)).toBe(1)
  })
})
