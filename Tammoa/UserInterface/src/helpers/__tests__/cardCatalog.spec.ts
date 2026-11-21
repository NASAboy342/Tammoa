import { describe, expect, it } from 'vitest'
import type { CardCategory, CardTypeInfo } from '@/services/cardApi'
import { CATEGORY_ORDER, filterCards, groupCards } from '../cardCatalog'

const card = (type: string, name: string, category: CardCategory, description = ''): CardTypeInfo => ({
  type, name, description, category, icon: 'zap', isTrigger: category === 'trigger', params: [], outputs: [{ key: 'main', label: '' }],
})
const all = [
  card('script.shell', 'Script', 'action', 'Runs code'),
  card('ai.ollama', 'Ollama', 'ai', 'Asks a local model'),
  card('trigger.manual', 'Manual trigger', 'trigger', 'Starts the flow'),
  card('net.http', 'HTTP Request', 'action', 'Calls a web API'),
  card('logic.if', 'IF', 'logic', 'Splits the flow'),
]

describe('groupCards', () => {
  it('uses the fixed section order, sorts cards by name and drops empty sections', () => {
    const groups = groupCards(all)
    expect(groups.map((g) => g.category)).toEqual(['trigger', 'logic', 'action', 'ai'])
    expect(groups.find((g) => g.category === 'action')!.cards.map((c) => c.name)).toEqual(['HTTP Request', 'Script'])
    expect(groups.map((g) => g.label)).toEqual(['Triggers', 'Logic', 'Actions', 'AI'])
  })
  it('knows every category', () => expect(new Set(CATEGORY_ORDER).size).toBe(5))
})

describe('filterCards', () => {
  it('matches name, description and type, ignoring case', () => {
    expect(filterCards(all, 'ollama').map((c) => c.type)).toEqual(['ai.ollama'])
    expect(filterCards(all, 'WEB api').map((c) => c.type)).toEqual(['net.http'])
    expect(filterCards(all, 'script.shell').map((c) => c.type)).toEqual(['script.shell'])
    expect(filterCards(all, 'splits').map((c) => c.type)).toEqual(['logic.if'])
  })
  it('an empty or blank query keeps everything; no match gives nothing', () => {
    expect(filterCards(all, '')).toHaveLength(5)
    expect(filterCards(all, '   ')).toHaveLength(5)
    expect(filterCards(all, 'zzz')).toEqual([])
  })
})
