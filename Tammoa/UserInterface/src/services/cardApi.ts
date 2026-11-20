import { requestJson } from './httpClient'

export type ParamKind = 'text' | 'textarea' | 'number' | 'bool'

export interface CardParamInfo {
  key: string
  label: string
  kind: ParamKind
  help?: string
  required?: boolean
  defaultValue?: unknown
  placeholder?: string
}

export interface CardTypeInfo {
  type: string
  name: string
  description: string
  category: 'trigger' | 'action' | 'data'
  icon: string
  isTrigger: boolean
  params: CardParamInfo[]
}

/** The backend owns the card catalogue; the UI draws cards and their forms from this and has no card-specific code. */
export const cardApi = {
  listTypes: () => requestJson<CardTypeInfo[]>('/api/cards/types'),
}
