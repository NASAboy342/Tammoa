import type { AgentAccess } from '@/services/agentApi'

/** The text the user pastes to an AI agent: where Tammoa is, the token, and where to read what it may do. */
export function agentInstructions(a: Pick<AgentAccess, 'url' | 'token' | 'enabled' | 'allowRun'>): string {
  return [
    'Tammoa agent access',
    `Address: ${a.url}`,
    `Token: ${a.token}`,
    `Send the header "Authorization: Bearer ${a.token}" on every request.`,
    `First read the skill: GET ${a.url}/api/agent/skill (it explains how to build flows and lists every route you may call).`,
    a.allowRun ? 'You may run flows only when the user asks you to.' : 'You may NOT run flows (the user keeps that switch off): create and edit them, and ask the user to press Run.',
  ].join('\n')
}

/** Shows the start of a secret and hides the rest ("Zx3k…"). */
export function maskToken(token: string): string {
  return token.length <= 8 ? '•'.repeat(token.length) : `${token.slice(0, 4)}${'•'.repeat(12)}`
}
