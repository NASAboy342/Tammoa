import { requestJson } from './httpClient'

/** Mirrors Api/AppSupportEndpoints.cs: settings, update check, backup and flow templates. */

export interface AppSettings {
  checkUpdatesAtStart: boolean
}

export interface UpdateInfo {
  currentVersion: string
  latestVersion?: string
  updateAvailable: boolean
  url?: string
  /** Why the check could not be done (offline, no release yet…). Never an app failure. */
  error?: string
}

export interface BackupFile {
  fileName: string
  path: string
  size: number
  createdAt: string
  flows: number
  connections: number
}

export interface BackupList {
  folder: string
  files: BackupFile[]
}

export interface RestoreResult {
  flowsAdded: number
  flowsSkipped: number
  connectionsAdded: number
  connectionsSkipped: number
  sharedTextsAdded: number
  connectionsNeedingKey: string[]
  problems: string[]
}

export interface FlowTemplate {
  id: string
  name: string
  description: string
  icon: string
  needsAi: boolean
  needs: string[]
  cardCount: number
}

const post = <T>(url: string, body?: unknown) =>
  requestJson<T>(url, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const supportApi = {
  getSettings: () => requestJson<AppSettings>('/api/settings'),
  saveSettings: (settings: Partial<AppSettings>) => requestJson<AppSettings>('/api/settings', { method: 'PUT', body: JSON.stringify(settings) }),
  checkUpdate: () => requestJson<UpdateInfo>('/api/app/update'),

  listBackups: () => requestJson<BackupList>('/api/backup'),
  createBackup: () => post<BackupFile>('/api/backup'),
  restoreDocument: (document: unknown) => post<RestoreResult>('/api/backup/restore', document),
  restoreFile: (fileName: string) => post<RestoreResult>('/api/backup/restore-file', { fileName }),
  openBackupFolder: () => post<void>('/api/backup/open-folder'),

  templates: () => requestJson<FlowTemplate[]>('/api/templates'),
  createFromTemplate: (id: string, choice: { connectionId?: string; model?: string }) => post<{ id: string; name: string }>(`/api/templates/${encodeURIComponent(id)}/create`, choice),
}
