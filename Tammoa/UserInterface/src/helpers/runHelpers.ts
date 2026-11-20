import type { IconName } from '@/components/icons/icons'
import type { NodeRunStatus, RunStatus } from '@/services/runApi'

export const nodeStatusIcon: Record<NodeRunStatus, IconName> = {
  pending: 'circle',
  running: 'loader',
  succeeded: 'check-circle',
  failed: 'x-circle',
  skipped: 'minus',
  cancelled: 'stop',
}

export const runStatusIcon: Record<RunStatus, IconName> = {
  running: 'loader',
  succeeded: 'check-circle',
  failed: 'x-circle',
  cancelled: 'stop',
}

export const statusLabel = (s: NodeRunStatus | RunStatus) => s.charAt(0).toUpperCase() + s.slice(1)

export function formatDuration(startedAt?: string, finishedAt?: string, now: number = Date.now()): string {
  if (!startedAt) return ''
  const start = new Date(startedAt).getTime()
  if (Number.isNaN(start)) return ''
  const end = finishedAt ? new Date(finishedAt).getTime() : now
  const ms = Math.max(0, end - start)
  if (ms < 1000) return `${ms} ms`
  const s = ms / 1000
  return s < 60 ? `${s.toFixed(1)} s` : `${Math.floor(s / 60)} min ${Math.round(s % 60)} s`
}

/** The Run box sends JSON when the text is valid JSON (so 5, true, {"a":1} keep their type), otherwise the raw text. Empty = no input. */
export function parseRunInput(text: string): unknown {
  const trimmed = text.trim()
  if (trimmed === '') return undefined
  try {
    return JSON.parse(trimmed)
  } catch {
    return text
  }
}

export function prettyOutput(value: unknown, max = 4000): string {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2) ?? ''
  return text.length > max ? `${text.slice(0, max)}\n… (${text.length - max} more characters)` : text
}
