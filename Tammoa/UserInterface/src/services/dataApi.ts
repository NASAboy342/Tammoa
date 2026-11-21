import { requestJson } from './httpClient'

/** The stored text of a Data card (a plain .txt file kept by the backend). */
export interface DataEntry {
  cardId: string
  name: string
  scope: 'flow' | 'shared'
  text: string
  path: string
  size: number
  updatedAt?: string
  exists: boolean
}

export interface DataCardSummary {
  cardId: string
  name: string
  scope: 'flow' | 'shared'
  preview: string
  size: number
  updatedAt?: string
  isJson: boolean
}

export const dataApi = {
  list: (flowId: string) => requestJson<DataCardSummary[]>(`/api/flows/${flowId}/data`),
  read: (flowId: string, cardId: string) => requestJson<DataEntry>(`/api/flows/${flowId}/data/${cardId}`),
  write: (flowId: string, cardId: string, text: string) => requestJson<DataEntry>(`/api/flows/${flowId}/data/${cardId}`, { method: 'PUT', body: JSON.stringify({ text }) }),
}
