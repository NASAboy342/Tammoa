import { describe, expect, it } from 'vitest'
import { formatBytes, readBackupText, restoreLines, updateLine } from '../support'
import type { RestoreResult } from '@/services/supportApi'

const result = (over: Partial<RestoreResult> = {}): RestoreResult => ({
  flowsAdded: 0, flowsSkipped: 0, connectionsAdded: 0, connectionsSkipped: 0, sharedTextsAdded: 0, connectionsNeedingKey: [], problems: [], ...over,
})

describe('updateLine', () => {
  it('says nothing before a check', () => expect(updateLine(null)).toBeNull())
  it('reports a newer version', () =>
    expect(updateLine({ currentVersion: '0.1.0', latestVersion: '0.2.0', updateAvailable: true })).toEqual({ icon: 'alert', text: 'Version 0.2.0 is available (you have 0.1.0).' }))
  it('reports being up to date', () =>
    expect(updateLine({ currentVersion: '0.1.0', latestVersion: '0.1.0', updateAvailable: false })?.icon).toBe('check-circle'))
  it('shows the reason when the check could not be done', () =>
    expect(updateLine({ currentVersion: '0.1.0', updateAvailable: false, error: 'No release has been published yet.' })).toEqual({ icon: 'info', text: 'No release has been published yet.' }))
})

describe('restoreLines', () => {
  it('counts with the right plural', () => {
    expect(restoreLines(result({ flowsAdded: 1 }))).toEqual(['1 flow restored (disabled until you switch them on).'])
    expect(restoreLines(result({ flowsAdded: 3, connectionsAdded: 2 }))).toEqual(['3 flows restored (disabled until you switch them on).', '2 connections restored.'])
  })
  it('says what was left alone, and which keys to type again', () => {
    const lines = restoreLines(result({ flowsAdded: 1, flowsSkipped: 2, connectionsSkipped: 1, connectionsAdded: 1, connectionsNeedingKey: ['Home', 'Work'] }))
    expect(lines).toContain('3 already here, left as they are.')
    expect(lines.at(-1)).toBe('Type the key again for: Home, Work (Connections > Edit). Keys are never in a backup.')
  })
  it('says so when there was nothing to do', () => expect(restoreLines(result({ flowsSkipped: 0 }))).toEqual(['Nothing to restore: everything in this file is already here.']))
  it('stays quiet about "nothing" when there are problems (they are shown on their own)', () => expect(restoreLines(result({ problems: ['x'] }))).toEqual([]))
})

describe('formatBytes', () => {
  it.each([[500, '500 B'], [2048, '2.0 KB'], [5 * 1024 * 1024, '5.0 MB']])('%i -> %s', (n, text) => expect(formatBytes(n)).toBe(text))
})

describe('readBackupText', () => {
  it('accepts a backup document', () => expect(readBackupText('{"format":"tammoa.backup","version":1}')).toEqual({ ok: true, document: { format: 'tammoa.backup', version: 1 } }))
  it('refuses text that is not JSON', () => expect(readBackupText('hello')).toMatchObject({ ok: false, error: expect.stringContaining('not readable JSON') }))
  it('refuses JSON that is not a backup (a flow file, a list, null)', () => {
    for (const text of ['{"format":"tammoa.flow"}', '[]', 'null', '42']) expect(readBackupText(text)).toMatchObject({ ok: false, error: expect.stringContaining('not a Tammoa backup') })
  })
})
