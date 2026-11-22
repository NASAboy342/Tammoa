import type { IconName } from '@/components/icons/icons'

/** One tool call the AI card made, as the backend reports it (`toolCalls` in the card output and in the live view). */
export interface ToolCall {
  step: number
  name: string
  arguments?: Record<string, unknown>
  decision: 'ran' | 'rejected' | 'failed'
  /** For a refused call: a safety rule (folder, host, script check) or the approver model. */
  by?: 'rule' | 'approver'
  reason?: string
  result?: string
  durationMs?: number
}

export const TOOL_ICON: Record<string, IconName> = {
  read_file: 'file-text',
  write_file: 'file-plus',
  http_request: 'globe',
  run_script: 'terminal',
}
export const toolIcon = (name: string): IconName => TOOL_ICON[name] ?? 'zap'

export const DECISION_ICON: Record<ToolCall['decision'], IconName> = { ran: 'check-circle', rejected: 'stop', failed: 'alert' }
export const DECISION_LABEL: Record<ToolCall['decision'], string> = { ran: 'Ran', rejected: 'Refused', failed: 'Failed' }

/** "Ran", "Failed", or for a refusal who refused: "Refused (rule)" / "Refused (approver)". */
export function decisionLabel(c: Pick<ToolCall, 'decision' | 'by'>): string {
  return c.decision === 'rejected' && c.by ? `${DECISION_LABEL.rejected} (${c.by})` : DECISION_LABEL[c.decision]
}

/** The tool calls inside a card output / live object, or [] when there are none (any other shape is ignored). */
export function toolCallsOf(value: unknown): ToolCall[] {
  const list = (value as { toolCalls?: unknown } | null | undefined)?.toolCalls
  if (!Array.isArray(list)) return []
  return list.filter((c): c is ToolCall => !!c && typeof c === 'object' && typeof (c as ToolCall).name === 'string' && typeof (c as ToolCall).decision === 'string')
}

/** One line for the arguments: `path: a.txt · content: hello…`. Long values are cut, multi-line values collapse to one line. */
export function summarizeArgs(args: Record<string, unknown> | undefined, max = 80): string {
  if (!args) return ''
  const parts = Object.entries(args).map(([k, v]) => `${k}: ${typeof v === 'string' ? v : JSON.stringify(v)}`.replace(/\s+/g, ' '))
  const line = parts.join(' · ')
  return line.length > max ? line.slice(0, max - 1).trimEnd() + '…' : line
}

export function formatToolDuration(ms: number | undefined): string {
  if (ms === undefined || !Number.isFinite(ms)) return ''
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(1)} s`
}

/** Short summary of a list of calls: "3 calls: 2 ran, 1 refused". */
export function summarizeCalls(calls: ToolCall[]): string {
  if (!calls.length) return 'No tool calls'
  const count = (d: ToolCall['decision']) => calls.filter((c) => c.decision === d).length
  const bits = [`${count('ran')} ran`, count('rejected') ? `${count('rejected')} refused` : '', count('failed') ? `${count('failed')} failed` : ''].filter(Boolean)
  return `${calls.length} ${calls.length === 1 ? 'call' : 'calls'}: ${bits.join(', ')}`
}

/** The full text of a call to read: the reason when it did not run, else the result. */
export const callDetail = (c: ToolCall): string => (c.decision === 'ran' ? (c.result ?? '') : (c.reason ?? ''))

// ---- which settings of the AI card matter for the tools ticked
// TODO(backend-enhance): move this into the param descriptors (e.g. `showWhen`) so the UI stays free of card-specific code.
const FILE_TOOLS = ['read_file', 'write_file', 'run_script']
const selected = (values: Record<string, unknown>) => (Array.isArray(values.tools) ? (values.tools as unknown[]).filter((t): t is string => typeof t === 'string') : [])

/** Whether a param is worth showing for the current values. Everything not tool-related is always shown. */
export function paramVisible(key: string, values: Record<string, unknown>): boolean {
  const tools = selected(values)
  if (key === 'toolRoot') return tools.some((t) => FILE_TOOLS.includes(t))
  if (key === 'allowedHosts') return tools.includes('http_request')
  if (key === 'maxSteps' || key === 'approverModel') return tools.length > 0
  if (key === 'approverPrompt') return tools.length > 0 && typeof values.approverModel === 'string' && values.approverModel.trim() !== '' // only when an approver is chosen
  return true
}

/** Adds or removes one value from a multi-select value, keeping the order of the options. */
export function toggleValue(current: unknown, option: string, optionOrder: string[]): string[] {
  const set = new Set(Array.isArray(current) ? (current as unknown[]).filter((v): v is string => typeof v === 'string') : [])
  if (set.has(option)) set.delete(option)
  else set.add(option)
  return [...optionOrder.filter((o) => set.has(o)), ...[...set].filter((o) => !optionOrder.includes(o))]
}
