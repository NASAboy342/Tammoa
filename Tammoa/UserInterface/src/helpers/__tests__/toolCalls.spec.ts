import { describe, expect, it } from 'vitest'
import { DECISION_ICON, DECISION_LABEL, PHASE_ICON, TOOL_ICON, callDetail, decisionLabel, inFlight, phaseElapsed, phaseOf, phaseText, formatToolDuration, paramVisible, summarizeArgs, summarizeCalls, toggleValue, toolCallsOf, toolIcon, type ToolCall } from '../toolCalls'
import { ICONS } from '@/components/icons/icons'

const call = (decision: ToolCall['decision'], extra: Partial<ToolCall> = {}): ToolCall => ({ step: 1, name: 'write_file', decision, ...extra })

describe('icons', () => {
  it('every tool and decision icon exists', () => {
    for (const n of [...Object.values(TOOL_ICON), ...Object.values(DECISION_ICON), toolIcon('unknown')]) expect(n in ICONS, n).toBe(true)
  })
})

describe('toolCallsOf', () => {
  it('reads the list from an output or live object, ignores anything else', () => {
    expect(toolCallsOf({ toolCalls: [call('ran')] })).toHaveLength(1)
    expect(toolCallsOf({ toolCalls: 'x' })).toEqual([])
    expect(toolCallsOf(null)).toEqual([])
    expect(toolCallsOf({ text: 'a' })).toEqual([])
    expect(toolCallsOf({ toolCalls: [null, 3, { name: 'a' }, call('failed')] })).toHaveLength(1)
  })
})

describe('summarizeArgs', () => {
  it('joins key: value and flattens whitespace', () => {
    expect(summarizeArgs({ path: 'a.txt', content: 'one\ntwo' })).toBe('path: a.txt · content: one two')
    expect(summarizeArgs({ n: 3, o: { a: 1 } })).toBe('n: 3 · o: {"a":1}')
    expect(summarizeArgs(undefined)).toBe('')
  })
  it('cuts long text with an ellipsis', () => {
    const s = summarizeArgs({ content: 'x'.repeat(200) }, 30)
    expect(s.length).toBe(30)
    expect(s.endsWith('…')).toBe(true)
  })
})

describe('formatToolDuration / summarizeCalls / callDetail', () => {
  it('formats durations', () => {
    expect(formatToolDuration(12.4)).toBe('12 ms')
    expect(formatToolDuration(1500)).toBe('1.5 s')
    expect(formatToolDuration(undefined)).toBe('')
  })
  it('summarises the decisions', () => {
    expect(summarizeCalls([])).toBe('No tool calls')
    expect(summarizeCalls([call('ran')])).toBe('1 call: 1 ran')
    expect(summarizeCalls([call('ran'), call('rejected'), call('failed'), call('ran')])).toBe('4 calls: 2 ran, 1 refused, 1 failed')
  })
  it('shows the result of a call that ran and the reason of one that did not', () => {
    expect(callDetail(call('ran', { result: 'ok', reason: 'x' }))).toBe('ok')
    expect(callDetail(call('rejected', { reason: 'outside' }))).toBe('outside')
    expect(callDetail(call('failed'))).toBe('')
  })
})

describe('decisionLabel', () => {
  it('says who refused', () => {
    expect(decisionLabel(call('ran'))).toBe('Ran')
    expect(decisionLabel(call('failed'))).toBe('Failed')
    expect(decisionLabel(call('rejected'))).toBe('Refused')
    expect(decisionLabel(call('rejected', { by: 'rule' }))).toBe('Refused (rule)')
    expect(decisionLabel(call('rejected', { by: 'approver' }))).toBe('Refused (approver)')
    expect(decisionLabel(call('ran', { by: 'rule' }))).toBe('Ran')
  })
})

describe('paramVisible', () => {
  it('shows the tool settings only when relevant; other params always', () => {
    expect(paramVisible('prompt', {})).toBe(true)
    for (const k of ['toolRoot', 'allowedHosts', 'maxSteps', 'approverModel']) expect(paramVisible(k, {}), k).toBe(false)
    expect(paramVisible('toolRoot', { tools: ['read_file'] })).toBe(true)
    expect(paramVisible('toolRoot', { tools: ['http_request'] })).toBe(false)
    expect(paramVisible('allowedHosts', { tools: ['http_request'] })).toBe(true)
    expect(paramVisible('allowedHosts', { tools: ['write_file'] })).toBe(false)
    expect(paramVisible('maxSteps', { tools: ['http_request'] })).toBe(true)
    expect(paramVisible('approverModel', { tools: ['run_script'] })).toBe(true)
    expect(paramVisible('toolRoot', { tools: 'read_file' })).toBe(false) // not an array: nothing selected
  })
  it('shows the approver instructions only when an approver model is chosen', () => {
    expect(paramVisible('approverPrompt', {})).toBe(false)
    expect(paramVisible('approverPrompt', { tools: ['read_file'] })).toBe(false)
    expect(paramVisible('approverPrompt', { tools: ['read_file'], approverModel: '  ' })).toBe(false)
    expect(paramVisible('approverPrompt', { tools: ['read_file'], approverModel: 'qwen3:0.6b' })).toBe(true)
    expect(paramVisible('approverPrompt', { tools: [], approverModel: 'qwen3:0.6b' })).toBe(false)
  })
})

describe('toggleValue', () => {
  const order = ['a', 'b', 'c']
  it('adds in option order, removes, and tolerates a missing value', () => {
    expect(toggleValue(undefined, 'b', order)).toEqual(['b'])
    expect(toggleValue(['c'], 'a', order)).toEqual(['a', 'c'])
    expect(toggleValue(['a', 'b'], 'a', order)).toEqual(['b'])
    expect(toggleValue('junk', 'c', order)).toEqual(['c'])
  })
  it('keeps a saved value the options no longer offer', () => expect(toggleValue(['old'], 'a', order)).toEqual(['a', 'old']))
})

describe('live stages', () => {
  it('every stage has an icon that exists and a label', () => {
    for (const d of ['queued', 'approving', 'running', 'ran', 'rejected', 'failed'] as const) {
      expect(DECISION_ICON[d] in ICONS, d).toBe(true)
      expect(DECISION_LABEL[d].length, d).toBeGreaterThan(0)
    }
    for (const n of Object.values(PHASE_ICON)) expect(n in ICONS, n).toBe(true)
  })
  it('knows which calls are still going on', () => {
    expect(['queued', 'approving', 'running'].every((d) => inFlight({ decision: d as ToolCall['decision'] }))).toBe(true)
    expect(['ran', 'rejected', 'failed'].some((d) => inFlight({ decision: d as ToolCall['decision'] }))).toBe(false)
  })
  it('summarises calls that are still going on', () => {
    expect(summarizeCalls([call('ran'), call('running'), call('queued'), call('queued')])).toBe('4 calls: 1 ran, 1 running, 2 queued')
    expect(summarizeCalls([call('approving')])).toBe('1 call: 0 ran, 1 asking approver')
  })
  it('has no detail text for a call that is not finished', () => expect(callDetail(call('running', { reason: 'x', result: 'y' }))).toBe(''))
  it('labels a running call', () => expect(decisionLabel(call('running'))).toBe('Running'))
})

describe('phase', () => {
  const since = '2026-01-01T00:00:00.000Z'
  it('reads a phase from a live object and ignores anything malformed', () => {
    expect(phaseOf({ phase: { kind: 'model', step: 2, since } })).toEqual({ kind: 'model', step: 2, name: undefined, since })
    expect(phaseOf({ phase: { kind: 'tool', step: 1, name: 'write_file', since } })?.name).toBe('write_file')
    expect(phaseOf({ phase: { kind: 'nope', since } })).toBeNull()
    expect(phaseOf({ phase: { kind: 'model' } })).toBeNull()
    expect(phaseOf({})).toBeNull()
    expect(phaseOf(null)).toBeNull()
    expect(phaseOf({ phase: { kind: 'model', since } })?.step).toBe(1)
  })
  it('says what the card is waiting for', () => {
    expect(phaseText({ kind: 'model', step: 1, since })).toBe('Waiting for the model')
    expect(phaseText({ kind: 'model', step: 3, since })).toBe('Waiting for the model (step 3)')
    expect(phaseText({ kind: 'approver', step: 1, name: 'write_file', since })).toBe('Asking the approver about write_file')
    expect(phaseText({ kind: 'tool', step: 1, name: 'run_script', since })).toBe('Running run_script')
  })
  it('measures how long it has been waiting', () => {
    expect(phaseElapsed({ kind: 'model', step: 1, since }, new Date('2026-01-01T00:00:12.000Z').getTime())).toBe('12.0 s')
    expect(phaseElapsed({ kind: 'model', step: 1, since: 'garbage' }, 5)).toBe('')
  })
})
