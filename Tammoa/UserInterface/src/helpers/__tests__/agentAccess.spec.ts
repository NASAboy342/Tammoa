import { describe, expect, it } from 'vitest'
import { agentInstructions, maskToken } from '../agentAccess'

const base = { url: 'http://localhost:8000', token: 'abc123TOKEN', enabled: true, allowRun: false }

describe('agentInstructions', () => {
  it('has the address, the token, the header, and where to read the skill', () => {
    const text = agentInstructions(base)
    expect(text).toContain('Address: http://localhost:8000')
    expect(text).toContain('Token: abc123TOKEN')
    expect(text).toContain('Authorization: Bearer abc123TOKEN')
    expect(text).toContain('GET http://localhost:8000/api/agent/skill')
  })
  it('tells the agent whether it may run flows', () => {
    expect(agentInstructions(base)).toContain('may NOT run flows')
    expect(agentInstructions({ ...base, allowRun: true })).toContain('only when the user asks')
  })
})

describe('maskToken', () => {
  it('shows only the start of a long token', () => {
    const m = maskToken('abcdefghijklmnop')
    expect(m.startsWith('abcd')).toBe(true)
    expect(m).not.toContain('efgh')
  })
  it('hides a short one completely', () => {
    expect(maskToken('abc')).toBe('•••')
  })
})
