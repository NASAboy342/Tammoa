import type { CardCategory, CardTypeInfo } from '@/services/cardApi'

export const CATEGORY_ORDER: CardCategory[] = ['trigger', 'logic', 'data', 'action', 'ai']

export const CATEGORY_LABEL: Record<CardCategory, string> = {
  trigger: 'Triggers',
  logic: 'Logic',
  data: 'Data',
  action: 'Actions',
  ai: 'AI',
}

/** Case-insensitive match on name and description; an empty query keeps everything. */
export function filterCards(types: CardTypeInfo[], query: string): CardTypeInfo[] {
  const q = query.trim().toLowerCase()
  if (!q) return types
  return types.filter((t) => t.name.toLowerCase().includes(q) || t.description.toLowerCase().includes(q) || t.type.toLowerCase().includes(q))
}

/** Palette sections in a fixed order, cards sorted by name, empty sections dropped. */
export function groupCards(types: CardTypeInfo[]): { category: CardCategory; label: string; cards: CardTypeInfo[] }[] {
  return CATEGORY_ORDER.map((category) => ({
    category,
    label: CATEGORY_LABEL[category],
    cards: types.filter((t) => t.category === category).sort((a, b) => a.name.localeCompare(b.name)),
  })).filter((g) => g.cards.length > 0)
}
