import type { RestoreResult, UpdateInfo } from '@/services/supportApi'

/** One line for the result of an update check. */
export function updateLine(info: UpdateInfo | null): { icon: 'check-circle' | 'alert' | 'info'; text: string } | null {
  if (!info) return null
  if (info.error) return { icon: 'info', text: info.error }
  if (info.updateAvailable) return { icon: 'alert', text: `Version ${info.latestVersion} is available (you have ${info.currentVersion}).` }
  return { icon: 'check-circle', text: `You have the latest version (${info.currentVersion}).` }
}

/** Plain-words lines for what a restore did. */
export function restoreLines(r: RestoreResult): string[] {
  const lines: string[] = []
  const n = (count: number, one: string, many = one + 's') => `${count} ${count === 1 ? one : many}`
  if (r.flowsAdded) lines.push(`${n(r.flowsAdded, 'flow')} restored (disabled until you switch them on).`)
  if (r.connectionsAdded) lines.push(`${n(r.connectionsAdded, 'connection')} restored.`)
  if (r.sharedTextsAdded) lines.push(`${n(r.sharedTextsAdded, 'shared text')} restored.`)
  const skipped = r.flowsSkipped + r.connectionsSkipped
  if (skipped) lines.push(`${skipped} already here, left as they are.`)
  if (r.connectionsNeedingKey.length) lines.push(`Type the key again for: ${r.connectionsNeedingKey.join(', ')} (Connections > Edit). Keys are never in a backup.`)
  if (!lines.length && !r.problems.length) lines.push('Nothing to restore: everything in this file is already here.')
  return lines
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** A quick look at a chosen file before it is sent: is it JSON, and does it say it is a Tammoa backup? The backend checks it again. */
export function readBackupText(text: string): { ok: true; document: unknown } | { ok: false; error: string } {
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return { ok: false, error: 'That file is not a Tammoa backup (it is not readable JSON).' }
  }
  if (typeof parsed !== 'object' || parsed === null || (parsed as { format?: unknown }).format !== 'tammoa.backup')
    return { ok: false, error: 'That file is not a Tammoa backup. Choose a file made with "Back up now".' }
  return { ok: true, document: parsed }
}
