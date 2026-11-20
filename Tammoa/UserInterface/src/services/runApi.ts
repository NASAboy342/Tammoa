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
  cancel: (runId: string) => requestJson<FlowRun>(`/api/runs/${runId}/cancel`, { method: 'POST' }),
}
