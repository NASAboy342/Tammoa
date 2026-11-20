import type { FlowRun } from './runApi'

/**
 * Subscribes to the backend's server-sent events: every change of any run arrives as a complete snapshot.
 * EventSource reconnects by itself; `onOpen` fires on every (re)connect so callers can refresh what they missed.
 * Returns a function that closes the connection.
 */
export function connectRunEvents(onRun: (run: FlowRun) => void, onOpen: () => void): () => void {
  const source = new EventSource('/api/events')
  source.addEventListener('run', (e) => onRun(JSON.parse((e as MessageEvent<string>).data) as FlowRun))
  source.onopen = () => onOpen()
  return () => source.close()
}
