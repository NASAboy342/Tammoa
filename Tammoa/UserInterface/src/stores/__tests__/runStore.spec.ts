import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { FlowRun } from '@/services/runApi'

const start = vi.fn()
vi.mock('@/services/runApi', () => ({ runApi: { start: (...a: unknown[]) => start(...a), listForFlow: vi.fn(async () => []), cancel: vi.fn() } }))
vi.mock('@/services/eventsApi', () => ({ connectRunEvents: vi.fn(() => () => {}) }))

import { useRunStore } from '../runStore'

const run = (revision: number, status: FlowRun['status'] = 'running', nodeStatus: 'pending' | 'succeeded' = 'pending'): FlowRun => ({
  id: 'r1', flowId: 'f', flowName: 'F', status, startedAt: '2026-01-01T00:00:00Z', revision,
  nodes: [{ nodeId: 't', status: nodeStatus }],
})

describe('runStore snapshots', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    start.mockReset()
  })

  it('keeps the newest revision whatever the arrival order', () => {
    const store = useRunStore()
    store.upsert(run(5, 'running', 'succeeded'))
    store.upsert(run(1)) // an old snapshot arriving late
    expect(store.runs.r1.revision).toBe(5)
    expect(store.runs.r1.nodes[0].status).toBe('succeeded')
    store.upsert(run(6, 'succeeded', 'succeeded'))
    expect(store.runs.r1.status).toBe('succeeded')
  })

  it('ignores a repeated revision and never lets a stale running snapshot replace a finished run', () => {
    const store = useRunStore()
    store.upsert(run(9, 'failed', 'succeeded'))
    store.upsert(run(9, 'running'))
    store.upsert(run(2, 'running'))
    expect(store.runs.r1.status).toBe('failed')
  })

  it("the reply to 'start' (the oldest snapshot) does not overwrite state already pushed to us", async () => {
    const store = useRunStore()
    start.mockImplementation(async () => {
      store.upsert(run(4, 'running', 'succeeded')) // pushed while the request was in flight
      return run(0) // the reply carries the first snapshot
    })
    await store.start('f')
    expect(store.runs.r1.revision).toBe(4)
    expect(store.runs.r1.nodes[0].status).toBe('succeeded')
  })

  it("lists a flow's runs newest first", () => {
    const store = useRunStore()
    store.upsert({ ...run(1), id: 'old', startedAt: '2026-01-01T00:00:00Z' })
    store.upsert({ ...run(1), id: 'new', startedAt: '2026-01-02T00:00:00Z' })
    store.upsert({ ...run(1), id: 'other', flowId: 'g' })
    expect(store.forFlow('f').map((r) => r.id)).toEqual(['new', 'old'])
  })
})
