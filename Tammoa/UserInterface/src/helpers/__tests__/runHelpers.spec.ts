import { describe, expect, it } from 'vitest'
import { formatDuration, nodeStatusIcon, parseRunInput, prettyOutput, roundsOf, runStatusIcon, statusLabel } from '../runHelpers'
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

describe('roundsOf: a card that ran several times in a loop', () => {
  it('a card that ran once has one round, which is the current one', () => {
    const rounds = roundsOf({ nodeId: 'a', status: 'succeeded', input: { n: 1 }, output: { n: 2 } })
    expect(rounds).toHaveLength(1)
    expect(rounds[0]).toMatchObject({ iteration: 1, current: true, input: { n: 1 }, output: { n: 2 }, status: 'succeeded' })
  })

  it('earlier rounds come first (oldest to newest), then the current one', () => {
    const rounds = roundsOf({
      nodeId: 'a', status: 'running', iteration: 3, input: { n: 2 },
      previous: [
        { iteration: 1, status: 'succeeded', input: { n: 0 }, output: { n: 1 } },
        { iteration: 2, status: 'succeeded', input: { n: 1 }, output: { n: 2 } },
      ],
    })
    expect(rounds.map((r) => [r.iteration, r.current])).toEqual([[1, false], [2, false], [3, true]])
    expect(rounds[0].output).toEqual({ n: 1 })
    expect(rounds[2].status).toBe('running')
  })

  it('a failed earlier round keeps its error', () => {
    const rounds = roundsOf({ nodeId: 'a', status: 'succeeded', iteration: 2, previous: [{ iteration: 1, status: 'failed', error: 'boom' }] })
    expect(rounds[0]).toMatchObject({ status: 'failed', error: 'boom', current: false })
  })
})
