import { describe, expect, it } from 'vitest'
import { formatDuration, nodeStatusIcon, parseRunInput, prettyOutput, runStatusIcon, statusLabel } from '../runHelpers'
import { ICONS } from '@/components/icons/icons'

describe('status icons', () => {
  it('every status maps to an icon that exists', () => {
    for (const name of [...Object.values(nodeStatusIcon), ...Object.values(runStatusIcon)]) expect(name in ICONS, name).toBe(true)
  })
  it('labels are capitalised', () => expect(statusLabel('succeeded')).toBe('Succeeded'))
})

describe('formatDuration', () => {
  const t0 = '2026-01-01T00:00:00.000Z'
  it('formats ms, seconds and minutes', () => {
    expect(formatDuration(t0, '2026-01-01T00:00:00.250Z')).toBe('250 ms')
    expect(formatDuration(t0, '2026-01-01T00:00:03.400Z')).toBe('3.4 s')
    expect(formatDuration(t0, '2026-01-01T00:02:05.000Z')).toBe('2 min 5 s')
  })
  it('uses "now" for a running run and tolerates bad input', () => {
    expect(formatDuration(t0, undefined, new Date('2026-01-01T00:00:02Z').getTime())).toBe('2.0 s')
    expect(formatDuration(undefined)).toBe('')
    expect(formatDuration('nope')).toBe('')
  })
})

describe('parseRunInput', () => {
  it('empty means no input', () => {
    expect(parseRunInput('')).toBeUndefined()
    expect(parseRunInput('   ')).toBeUndefined()
  })
  it('valid JSON keeps its type, anything else stays text', () => {
    expect(parseRunInput('{"a":1}')).toEqual({ a: 1 })
    expect(parseRunInput('5')).toBe(5)
    expect(parseRunInput('true')).toBe(true)
    expect(parseRunInput('hello world')).toBe('hello world')
    expect(parseRunInput('{broken')).toBe('{broken')
  })
})

describe('prettyOutput', () => {
  it('pretty-prints objects, passes strings, truncates long text', () => {
    expect(prettyOutput({ a: 1 })).toBe('{\n  "a": 1\n}')
    expect(prettyOutput('plain')).toBe('plain')
    expect(prettyOutput('x'.repeat(50), 10)).toContain('40 more characters')
  })
})
