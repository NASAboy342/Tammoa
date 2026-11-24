import type { FlowRun } from './runApi'

export interface RunRemoved {
  runId: string
  flowId: string
}

/**
 * Subscribes to the backend's server-sent events: every change of any run arrives as a complete snapshot ("run"),
 * deleted runs as "run-removed", and "data-changed" when the stored text of a Data card changed (by a run or by someone saving it). EventSource reconnects by itself; `onOpen` fires on every (re)connect so callers
 * can refresh what they missed. Returns a function that closes the connection.
 */
export interface DataChanged {
  flowId: string
  cardId: string
}

export function connectRunEvents(
  onRun: (run: FlowRun) => void,
  onOpen: () => void,
  onRemoved: (removed: RunRemoved) => void = () => {},
  onDataChanged: (changed: DataChanged) => void = () => {},
  onLost: () => void = () => {},
): () => void {
  const source = new EventSource('/api/events')
  source.addEventListener('run', (e) => onRun(JSON.parse((e as MessageEvent<string>).data) as FlowRun))
  source.addEventListener('run-removed', (e) => onRemoved(JSON.parse((e as MessageEvent<string>).data) as RunRemoved))
  source.addEventListener('data-changed', (e) => onDataChanged(JSON.parse((e as MessageEvent<string>).data) as DataChanged))
  source.onopen = () => onOpen()
  source.onerror = () => onLost() // the browser keeps trying; `onOpen` fires again when the backend is back
  return () => source.close()
}
