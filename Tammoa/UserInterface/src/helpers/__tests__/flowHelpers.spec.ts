import { describe, expect, it } from 'vitest'
import { canConfirmDelete, countCards, countConnections, formatWhen } from '../flowHelpers'

describe('canConfirmDelete', () => {
  it('needs the exact name', () => {
    expect(canConfirmDelete('Daily report', 'Daily report')).toBe(true)
  })
  it.each(['', 'daily report', 'Daily report ', ' Daily report', 'Daily'])('rejects %j', (typed) => {
    expect(canConfirmDelete('Daily report', typed)).toBe(false)
  })
  it('never confirms an empty flow name', () => {
    expect(canConfirmDelete('', '')).toBe(false)
  })
})

describe('definition counts', () => {
  it('counts nodes and edges, tolerating missing or malformed data', () => {
    expect(countCards({ nodes: [1, 2] })).toBe(2)
    expect(countConnections({ edges: [1] })).toBe(1)
    expect(countCards(undefined)).toBe(0)
    expect(countCards({ nodes: 5 as unknown as unknown[] })).toBe(0)
    expect(countConnections({})).toBe(0)
  })
})

describe('formatWhen', () => {
  it('formats a valid date and returns empty for garbage', () => {
    expect(formatWhen('2026-01-01T12:00:00Z')).not.toBe('')
    expect(formatWhen('nope')).toBe('')
  })
})
