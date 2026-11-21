import { describe, expect, it } from 'vitest'
import { flattenFields, previewOf, typeOf } from '../fields'
import { pickNodeRun } from '../runHelpers'
import type { FlowRun } from '@/services/runApi'

describe('typeOf / previewOf', () => {
  it('names the types in plain words', () => {
    expect(['a', 1, true, null, undefined, [], {}].map(typeOf)).toEqual(['text', 'number', 'boolean', 'empty', 'empty', 'list', 'object'])
  })
  it('previews counts for containers, text for text, and cuts long text', () => {
    expect(previewOf([1, 2, 3])).toBe('3 items')
    expect(previewOf([1])).toBe('1 item')
    expect(previewOf({ a: 1, b: 2 })).toBe('2 fields')
    expect(previewOf({ a: 1 })).toBe('1 field')
    expect(previewOf(null)).toBe('empty')
    expect(previewOf(42)).toBe('42')
    expect(previewOf(false)).toBe('false')
    expect(previewOf('line1\nline2')).toBe('line1\\nline2')
    expect(previewOf('x'.repeat(200))).toHaveLength(81)
    expect(previewOf('x'.repeat(200)).endsWith('…')).toBe(true)
  })
})

describe('flattenFields', () => {
  const data = { stdout: 'hi\n', exitCode: 0, json: { name: 'Sok', tags: ['a', 'b'], deep: { x: { y: { z: 1 } } } } }

  it('lists the root and every field with the template to copy', () => {
    const { rows } = flattenFields(data, 'input')
    expect(rows[0]).toMatchObject({ path: 'input', template: '{{input}}', type: 'object', depth: 0 })
    const byPath = Object.fromEntries(rows.map((r) => [r.path, r]))
    expect(byPath['input.stdout']).toMatchObject({ template: '{{input.stdout}}', type: 'text', preview: 'hi\\n', label: 'stdout', depth: 1 })
    expect(byPath['input.exitCode']).toMatchObject({ type: 'number', preview: '0' })
    expect(byPath['input.json.name']).toMatchObject({ template: '{{input.json.name}}', depth: 2 })
    expect(byPath['input.json.tags']).toMatchObject({ type: 'list', preview: '2 items' })
    expect(byPath['input.json.tags[0]']).toMatchObject({ template: '{{input.json.tags[0]}}', type: 'text', preview: 'a', label: '[0]' })
  })

  it('keeps a parent before its children (so the list reads like a tree)', () => {
    const paths = flattenFields(data).rows.map((r) => r.path)
    expect(paths.indexOf('input.json')).toBeLessThan(paths.indexOf('input.json.name'))
    expect(paths.indexOf('input.json.tags')).toBeLessThan(paths.indexOf('input.json.tags[0]'))
  })

  it('uses the root name it is given (the run input, or one of several connections)', () => {
    expect(flattenFields({ city: 'Phnom Penh' }, 'run.input').rows.map((r) => r.template)).toEqual(['{{run.input}}', '{{run.input.city}}'])
    expect(flattenFields({ a: 1 }, 'input[1]').rows[1].template).toBe('{{input[1].a}}')
  })

  it('a plain value is just the root; a list shows its first item', () => {
    expect(flattenFields('hello').rows).toHaveLength(1)
    expect(flattenFields('hello').rows[0]).toMatchObject({ template: '{{input}}', type: 'text', preview: 'hello' })
    expect(flattenFields(42).rows[0].type).toBe('number')
    const list = flattenFields([{ id: 1 }, { id: 2 }]).rows.map((r) => r.template)
    expect(list).toEqual(['{{input}}', '{{input[0]}}', '{{input[0].id}}'])
    expect(flattenFields([]).rows).toHaveLength(1)
  })

  it('null and missing values are "empty" rows, not crashes', () => {
    const { rows } = flattenFields({ a: null, b: undefined })
    expect(rows.filter((r) => r.type === 'empty').map((r) => r.label)).toEqual(['a', 'b'])
    expect(flattenFields(null).rows[0]).toMatchObject({ type: 'empty', preview: 'empty' })
  })

  it('flags keys a template cannot address', () => {
    const { rows } = flattenFields({ 'a.b': 1, 'c[0]': 2, ok: 3, 'with space': 4, 'x|y': 5 })
    const flagged = Object.fromEntries(rows.slice(1).map((r) => [r.label, r.addressable]))
    expect(flagged).toEqual({ 'a.b': false, 'c[0]': false, ok: true, 'with space': true, 'x|y': false })
  })

  it('stops at the depth limit and says so', () => {
    const r = flattenFields({ a: { b: { c: { d: { e: 1 } } } } }, 'input', { maxDepth: 3 })
    expect(r.rows.map((x) => x.label)).toEqual(['input', 'a', 'b', 'c'])
    expect(r.truncated).toBe(true)
    expect(flattenFields({ a: 1 }).truncated).toBe(false)
  })

  it('stops at the row limit and says so', () => {
    const wide = Object.fromEntries(Array.from({ length: 50 }, (_, i) => [`k${i}`, i]))
    const r = flattenFields(wide, 'input', { maxRows: 10 })
    expect(r.rows).toHaveLength(10)
    expect(r.truncated).toBe(true)
  })

  it('keeps the whole value for the tooltip', () => {
    const long = 'y'.repeat(500)
    const row = flattenFields({ t: long }).rows[1]
    expect(row.preview.length).toBeLessThan(100)
    expect(row.full).toBe(long)
    expect(flattenFields({ o: { a: 1 } }).rows[1].full).toBe('{"a":1}')
  })
})

describe('pickNodeRun', () => {
  const run = (id: string, nodes: FlowRun['nodes']): FlowRun => ({ id, flowId: 'f', flowName: 'F', status: 'succeeded', startedAt: '2026-01-01T00:00:00Z', revision: 1, nodes })
  const ran = { nodeId: 'a', status: 'succeeded' as const, input: { v: 1 }, output: { w: 2 } }
  const skipped = { nodeId: 'a', status: 'skipped' as const }
  const newest = run('new', [skipped])
  const older = run('old', [ran])

  it('uses the selected run when the card ran in it', () => {
    expect(pickNodeRun([newest, older], 'old', 'a')).toMatchObject({ fromActive: true, run: { id: 'old' } })
    expect(pickNodeRun([older], null, 'a')).toMatchObject({ fromActive: true, run: { id: 'old' } })
  })
  it('falls back to the newest run where the card really ran, and says it is not the active one', () => {
    expect(pickNodeRun([newest, older], 'new', 'a')).toMatchObject({ fromActive: false, run: { id: 'old' } })
    expect(pickNodeRun([newest, older], null, 'a')).toMatchObject({ fromActive: false, run: { id: 'old' } })
  })
  it('a failed card counts as having run (its input is what you want to see)', () => {
    const failed = run('f1', [{ nodeId: 'a', status: 'failed', input: { v: 1 }, error: 'boom' }])
    expect(pickNodeRun([failed], null, 'a')).toMatchObject({ fromActive: true })
  })
  it('returns null when it never ran', () => {
    expect(pickNodeRun([newest], null, 'a')).toBeNull()
    expect(pickNodeRun([], null, 'a')).toBeNull()
    expect(pickNodeRun([older], null, 'zzz')).toBeNull()
  })
})
