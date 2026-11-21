import { requestJson } from './httpClient'

export interface FlowDefinition {
  schemaVersion?: number
  nodes?: unknown[]
  edges?: unknown[]
  settings?: { loopLimit?: number; [key: string]: unknown }
  [key: string]: unknown
}

export interface Flow {
  id: string
  name: string
  description: string
  enabled: boolean
  definition: FlowDefinition
  createdAt: string
  updatedAt: string
}

export interface FlowSummary {
  id: string
  name: string
  description: string
  enabled: boolean
  cardCount: number
  createdAt: string
  updatedAt: string
}

/** Portable file format (export/import). UI for it comes later; the contract is ready. */
export interface FlowDocument {
  format: string
  version: number
  name: string
  description: string
  definition: FlowDefinition
}

const base = '/api/flows'
const post = <T>(url: string, body?: unknown) =>
  requestJson<T>(url, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const flowApi = {
  list: () => requestJson<FlowSummary[]>(base),
  get: (id: string) => requestJson<Flow>(`${base}/${id}`),
  create: (name: string, description: string) => post<Flow>(base, { name, description }),
  update: (id: string, flow: { name: string; description: string; definition: FlowDefinition }) =>
    requestJson<Flow>(`${base}/${id}`, { method: 'PUT', body: JSON.stringify(flow) }),
  /** The backend only deletes when confirmName equals the flow name exactly. */
  remove: (id: string, confirmName: string) =>
    requestJson<void>(`${base}/${id}`, { method: 'DELETE', body: JSON.stringify({ confirmName }) }),
  enable: (id: string) => post<Flow>(`${base}/${id}/enable`),
  disable: (id: string) => post<Flow>(`${base}/${id}/disable`),
  duplicate: (id: string) => post<Flow>(`${base}/${id}/duplicate`),
  exportDocument: (id: string) => requestJson<FlowDocument>(`${base}/${id}/export`),
  importDocument: (doc: FlowDocument) => post<Flow>(`${base}/import`, doc),
}
