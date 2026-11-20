import type { FlowDefinition } from '@/services/flowApi'

/** Deleting needs the exact flow name: case-sensitive, no trimming (mirrors the backend rule). */
export function canConfirmDelete(flowName: string, typed: string): boolean {
  return flowName.length > 0 && typed === flowName
}

export function countCards(definition: FlowDefinition | undefined): number {
  return Array.isArray(definition?.nodes) ? definition.nodes.length : 0
}

export function countConnections(definition: FlowDefinition | undefined): number {
  return Array.isArray(definition?.edges) ? definition.edges.length : 0
}

export function formatWhen(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}
