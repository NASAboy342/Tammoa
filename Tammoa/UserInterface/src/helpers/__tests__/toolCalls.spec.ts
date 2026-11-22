import { describe, expect, it } from 'vitest'
import { DECISION_ICON, TOOL_ICON, callDetail, decisionLabel, formatToolDuration, paramVisible, summarizeArgs, summarizeCalls, toggleValue, toolCallsOf, toolIcon, type ToolCall } from '../toolCalls'
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
