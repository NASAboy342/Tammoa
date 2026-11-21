import { ref } from 'vue'
import { defineStore } from 'pinia'
import { runApi, type FlowRun } from '@/services/runApi'
import { connectRunEvents } from '@/services/eventsApi'

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

/** Run state lives in the backend. This store only mirrors the snapshots it receives (server push + fetch). */
export const useRunStore = defineStore('runs', () => {
  const runs = ref<Record<string, FlowRun>>({})
  const error = ref<string | null>(null)
  const connected = ref(false)
  let watchedFlow: string | null = null
  let close: (() => void) | null = null

  /** A pushed event and a fetched snapshot (e.g. the reply to "start") can arrive in either order:
   *  keep whichever has the higher revision, never an older one. */
  function upsert(run: FlowRun) {
    const current = runs.value[run.id]
    if (current && run.revision <= current.revision) return
    runs.value = { ...runs.value, [run.id]: run }
  }

  function drop(runId: string) {
    if (!(runId in runs.value)) return
    const next = { ...runs.value }
    delete next[runId]
    runs.value = next
  }

  function forFlow(flowId: string): FlowRun[] {
    return Object.values(runs.value)
      .filter((r) => r.flowId === flowId)
      .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
  }

  async function loadForFlow(flowId: string) {
    watchedFlow = flowId
    try {
      for (const r of await runApi.listForFlow(flowId)) upsert(r)
    } catch (e) {
      error.value = message(e)
    }
  }

  function connect() {
    if (close) return
    close = connectRunEvents(
      upsert,
      () => {
        connected.value = true
        if (watchedFlow) void loadForFlow(watchedFlow) // catch up on anything missed while disconnected
      },
      (removed) => drop(removed.runId),
    )
  }

  function disconnect() {
    close?.()
    close = null
    connected.value = false
  }

  async function start(flowId: string, input?: unknown): Promise<FlowRun> {
    error.value = null
    try {
      const run = await runApi.start(flowId, input)
      upsert(run)
      return run
    } catch (e) {
      error.value = message(e)
      throw e
    }
  }

  /** Deletes all finished runs of a flow. The backend also announces each removal, so other windows follow. */
  async function clear(flowId: string) {
    error.value = null
    try {
      for (const id of (await runApi.clear(flowId)).deleted) drop(id)
    } catch (e) {
      error.value = message(e)
    }
  }

  async function remove(runId: string) {
    error.value = null
    try {
      await runApi.remove(runId)
      drop(runId)
    } catch (e) {
      error.value = message(e)
    }
  }

  async function cancel(runId: string) {
    try {
      upsert(await runApi.cancel(runId))
    } catch (e) {
      error.value = message(e)
    }
  }

  return { runs, error, connected, upsert, drop, forFlow, loadForFlow, connect, disconnect, start, cancel, clear, remove }
})
