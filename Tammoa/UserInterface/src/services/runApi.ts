import { requestJson } from './httpClient'

export type RunStatus = 'running' | 'succeeded' | 'failed' | 'cancelled'
export type NodeRunStatus = 'pending' | 'running' | 'succeeded' | 'failed' | 'skipped' | 'cancelled'

export interface NodeRun {
  nodeId: string
  status: NodeRunStatus
  startedAt?: string
  finishedAt?: string
  output?: unknown
  error?: string
  /** What the card received (absent for a trigger and for cards that did not run). */
  input?: unknown
  /** What a RUNNING card has produced so far (e.g. the answer an AI model is still writing). Gone when the card finishes. */
  live?: LiveOutput
  /** Which round of a loop this is (1 for a card that ran once). */
  iteration?: number
  /** Earlier rounds of a loop (the newest 10), oldest first. */
  previous?: NodeIteration[]
}

export interface NodeIteration {
  iteration: number
  status: NodeRunStatus
  startedAt?: string
  finishedAt?: string
  input?: unknown
  output?: unknown
  error?: string
}

export interface LiveOutput {
  text?: string
  thinking?: string
  /** The tool calls an AI card has made so far (see helpers/toolCalls). */
  toolCalls?: unknown[]
}

/** A complete snapshot of a run. The backend owns the state; the UI only displays snapshots. */
export interface FlowRun {
  id: string
  flowId: string
  flowName: string
  status: RunStatus
  startedAt: string
  finishedAt?: string
  input?: unknown
  error?: string
  nodes: NodeRun[]
  /** Grows with every change; the newest snapshot of a run is the one with the highest revision. */
  revision: number
}

export const runApi = {
  start: (flowId: string, input?: unknown) =>
    requestJson<FlowRun>(`/api/flows/${flowId}/runs`, { method: 'POST', body: JSON.stringify({ input }) }),
  listForFlow: (flowId: string) => requestJson<FlowRun[]>(`/api/flows/${flowId}/runs`),
  get: (runId: string) => requestJson<FlowRun>(`/api/runs/${runId}`),
  /** Deletes one finished run (409 while it is running). */
  remove: (runId: string) => requestJson<void>(`/api/runs/${runId}`, { method: 'DELETE' }),
  /** Deletes every finished run of the flow; running ones stay. Returns the removed ids. */
  clear: (flowId: string) => requestJson<{ deleted: string[] }>(`/api/flows/${flowId}/runs`, { method: 'DELETE' }),
  cancel: (runId: string) => requestJson<FlowRun>(`/api/runs/${runId}/cancel`, { method: 'POST' }),
}
