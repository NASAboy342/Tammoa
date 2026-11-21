import type { IconName } from '@/components/icons/icons'
import type { FlowRun, NodeRun, NodeRunStatus, RunStatus } from '@/services/runApi'

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

/** Which run's data to show for a card: the selected run's, or else the newest run in which that card actually ran. */
export interface NodeRunPick {
  run: FlowRun
  nodeRun: NodeRun
  /** True when it comes from the selected (or latest) run, false when from an earlier one. */
  fromActive: boolean
}

export function pickNodeRun(runs: FlowRun[], activeId: string | null, nodeId: string): NodeRunPick | null {
  const ran = (r: FlowRun) => r.nodes.find((n) => n.nodeId === nodeId && (n.input !== undefined || n.output !== undefined || n.error !== undefined))
  const active = runs.find((r) => r.id === activeId) ?? runs[0]
  const own = active && ran(active)
  if (active && own) return { run: active, nodeRun: own, fromActive: true }
  for (const r of runs) {
    const n = ran(r)
    if (n) return { run: r, nodeRun: n, fromActive: false }
  }
  return null
}

/** One round of a card: what it received, produced, and how it ended (the current round is the last). */
export interface Round {
  iteration: number
  status: NodeRunStatus
  startedAt?: string
  finishedAt?: string
  input?: unknown
  output?: unknown
  error?: string
  /** True for the card's current round (earlier rounds are history). */
  current: boolean
}

/** All rounds of a card in a run, oldest first: the kept earlier ones, then the current one. A card that ran once has one round. */
export function roundsOf(node: NodeRun): Round[] {
  const earlier: Round[] = (node.previous ?? []).map((p) => ({ ...p, current: false }))
  const current: Round = { iteration: node.iteration ?? 1, status: node.status, startedAt: node.startedAt, finishedAt: node.finishedAt, input: node.input, output: node.output, error: node.error, current: true }
  return [...earlier, current]
}

export function prettyOutput(value: unknown, max = 4000): string {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2) ?? ''
  return text.length > max ? `${text.slice(0, max)}\n… (${text.length - max} more characters)` : text
}
