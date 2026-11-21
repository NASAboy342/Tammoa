import { requestJson } from './httpClient'

export type ParamKind = 'text' | 'textarea' | 'number' | 'bool' | 'select' | 'code' | 'connection' | 'stored'
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
  /** Code: one-line "how do I read the input" hint per language, shown under the editor. */
  codeHelp?: Record<string, string>
  /** Code: starter text per language, shown while the editor is empty. */
  codePlaceholders?: Record<string, string>
}

/** One worked example of a card's tutorial. */
export interface GuideExample {
  title: string
  description: string
  /** Settings that "Use this example" applies to the card. */
  params?: Record<string, unknown>
  code?: string
  language?: string
  /** The input the example assumes (what the previous card output). */
  sampleInput?: unknown
  /** The Run input the example assumes. */
  runInput?: unknown
  /** The Data cards the example assumes (name -> value). */
  sampleData?: Record<string, unknown>
  /** What the card then outputs and how the next card can use it. */
  result?: string
}

/** A card's tutorial, written by the backend and shown as is by the UI. */
export interface CardGuide {
  overview: string
  inputNote: string
  /** Also the "fields this card will provide" shown to the card connected after it, before anything has run. */
  outputExample?: unknown
  examples: GuideExample[]
  tips?: string[]
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
  guide?: CardGuide
}

/** The backend owns the card catalogue; the UI draws cards and their forms from this and has no card-specific code. */
export const cardApi = {
  listTypes: () => requestJson<CardTypeInfo[]>('/api/cards/types'),
  /** Choices of a dynamic select, e.g. the models a connection's server offers. */
  options: (source: string, connectionId: string) =>
    requestJson<ParamOption[]>(`/api/cards/options/${encodeURIComponent(source)}?connectionId=${encodeURIComponent(connectionId)}`),
}
