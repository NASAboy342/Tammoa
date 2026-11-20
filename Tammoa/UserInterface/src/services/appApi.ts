import { requestJson } from './httpClient'

export interface AppVersionInfo {
  name: string
  version: string
}

export const appApi = {
  getVersion: () => requestJson<AppVersionInfo>('/api/app/version'),
}
