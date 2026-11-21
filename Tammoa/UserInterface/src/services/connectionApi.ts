import { requestJson } from './httpClient'

export type ConnectionProvider = 'ollama'

/** What the backend sends: never the API key, only whether one is saved. */
export interface ConnectionInfo {
  id: string
  name: string
  provider: ConnectionProvider
  baseUrl: string
  hasKey: boolean
  createdAt: string
  updatedAt: string
}

export interface ProviderInfo {
  provider: ConnectionProvider
  name: string
  defaultBaseUrl: string
  keyRequired: boolean
  keyHelp: string
}

/** On update: apiKey empty/undefined keeps the saved key, a value replaces it, clearApiKey removes it. */
export interface SaveConnection {
  name: string
  provider: ConnectionProvider
  baseUrl: string
  apiKey?: string
  clearApiKey?: boolean
}

export interface ConnectionTestResult {
  ok: boolean
  version?: string
  models: string[]
  error?: string
}

const base = '/api/connections'

export const connectionApi = {
  providers: () => requestJson<ProviderInfo[]>(`${base}/providers`),
  list: () => requestJson<ConnectionInfo[]>(base),
  create: (c: SaveConnection) => requestJson<ConnectionInfo>(base, { method: 'POST', body: JSON.stringify(c) }),
  update: (id: string, c: SaveConnection) => requestJson<ConnectionInfo>(`${base}/${id}`, { method: 'PUT', body: JSON.stringify(c) }),
  remove: (id: string) => requestJson<void>(`${base}/${id}`, { method: 'DELETE' }),
  test: (id: string) => requestJson<ConnectionTestResult>(`${base}/${id}/test`, { method: 'POST' }),
}
