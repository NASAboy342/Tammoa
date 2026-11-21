import { describe, expect, it } from 'vitest'
import { DATA_NAME_MAX, dataNameProblem } from '../dataNames'

describe('dataNameProblem (mirrors the backend rule)', () => {
  it.each(['Style', 'Last answer', 'my-notes_2', 'ខ្មែរ'])('accepts %j', (name) => expect(dataNameProblem(name)).toBeNull())

  it.each(['', '   ', 'a.b', 'a[0]', 'a|b', '{a}', 'x}'])('refuses %j and says why', (name) => expect(dataNameProblem(name)).toEqual(expect.any(String)))

  it('names the character that is the problem', () => expect(dataNameProblem('a.b')).toContain('"."'))
  it('limits the length', () => {
    expect(dataNameProblem('a'.repeat(DATA_NAME_MAX))).toBeNull()
    expect(dataNameProblem('a'.repeat(DATA_NAME_MAX + 1))).toContain('limited')
  })
  it('ignores spaces around the name', () => expect(dataNameProblem('  Style  ')).toBeNull())
})
