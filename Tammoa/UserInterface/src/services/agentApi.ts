import { requestJson, requestText } from './httpClient'

export interface AgentAccess {
  enabled: boolean
  allowRun: boolean
  /** The address an agent talks to (not the page's own address: in development the UI is served by Vite). */
  url: string
  token: string
}

export const agentApi = {
  get: () => requestJson<AgentAccess>('/api/agent/access'),
  update: (value: { enabled?: boolean; allowRun?: boolean }) => requestJson<AgentAccess>('/api/agent/access', { method: 'PUT', body: JSON.stringify(value) }),
  newToken: () => requestJson<AgentAccess>('/api/agent/access/token', { method: 'POST' }),
  /** The skill as Markdown, always the version of the running app. */
  skill: () => requestText('/api/agent/skill'),
}
