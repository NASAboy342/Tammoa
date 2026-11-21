import { requestJson } from './httpClient'

export type ParamKind = 'text' | 'textarea' | 'number' | 'bool' | 'select' | 'code' | 'connection'
export type CardCategory = 'trigger' | 'logic' | 'data' | 'action' | 'ai'

export interface ParamOption {
  value: string
  label: string
}

export interface CardOutputInfo {
  key: string
  label: string
}

export interface CardParamInfo {
  key: string
  label: string
  kind: ParamKind
  help?: string
  required?: boolean
  defaultValue?: unknown
  placeholder?: string
  options?: ParamOption[]
  /** Select: choices come from GET /api/cards/options/{source}?connectionId=… */
  optionsSource?: string
  /** Select: key of the param whose value is sent as connectionId (reload when it changes). */
  optionsDependsOn?: string
  /** Code: shell | javascript | python | json | text */
  codeLanguage?: string
  /** Code: the language is the value of this other param. */
  codeLanguageParam?: string
  /** Connection: only connections of this provider. */
  provider?: string
}

export interface CardTypeInfo {
  type: string
  name: string
  description: string
  category: CardCategory
  icon: string
  isTrigger: boolean
  params: CardParamInfo[]
  /** Output handles; most cards have a single 'main', IF has 'true' and 'false'. */
  outputs: CardOutputInfo[]
}

/** The backend owns the card catalogue; the UI draws cards and their forms from this and has no card-specific code. */
export const cardApi = {
  listTypes: () => requestJson<CardTypeInfo[]>('/api/cards/types'),
  /** Choices of a dynamic select, e.g. the models a connection's server offers. */
  options: (source: string, connectionId: string) =>
    requestJson<ParamOption[]>(`/api/cards/options/${encodeURIComponent(source)}?connectionId=${encodeURIComponent(connectionId)}`),
}
