/**
 * Turns a piece of data into the list of "fields you can use" shown in the Input / Output tabs: for every field its path, its type,
 * a short preview, and the exact template to copy ({{input.json.name}}). Pure, so it is easy to test.
 */
export interface FieldRow {
  /** Where it is, e.g. input.json.items[0].name */
  path: string
  /** What to paste into a card setting, e.g. {{input.json.items[0].name}} */
  template: string
  /** The last part only (name or [0]), for indented display */
  label: string
  type: 'text' | 'number' | 'boolean' | 'list' | 'object' | 'empty'
  preview: string
  /** The whole value as text, for the tooltip */
  full: string
  depth: number
  /** False when the key contains characters a template cannot address (. [ ] | { }) */
  addressable: boolean
}

export interface FlattenResult {
  rows: FieldRow[]
  /** True when rows were left out (too many, or deeper than maxDepth) */
  truncated: boolean
}

const PREVIEW_CHARS = 80
const SAFE_KEY = /^[^.[\]|{}]+$/

/** `tpl('input.name')` is `{{input.name}}`. Vue templates cannot contain a literal double brace, so views call this. */
export const tpl = (path: string) => `{{${path}}}`

export function typeOf(value: unknown): FieldRow['type'] {
  if (value === null || value === undefined) return 'empty'
  if (Array.isArray(value)) return 'list'
  switch (typeof value) {
    case 'string':
      return 'text'
    case 'number':
      return 'number'
    case 'boolean':
      return 'boolean'
    default:
      return 'object'
  }
}

export function previewOf(value: unknown): string {
  switch (typeOf(value)) {
    case 'list':
      return `${(value as unknown[]).length} item${(value as unknown[]).length === 1 ? '' : 's'}`
    case 'object': {
      const n = Object.keys(value as object).length
      return `${n} field${n === 1 ? '' : 's'}`
    }
    case 'empty':
      return 'empty'
    case 'text': {
      const flat = (value as string).replace(/\r?\n/g, '\\n')
      return flat.length > PREVIEW_CHARS ? `${flat.slice(0, PREVIEW_CHARS)}…` : flat
    }
    default:
      return String(value)
  }
}

const fullText = (value: unknown) => (typeof value === 'string' ? value : (JSON.stringify(value) ?? ''))

/** `root` is how the data is called in a template: "input", "run.input" or "input[0]" (one of several connections). */
export function flattenFields(value: unknown, root = 'input', options: { maxDepth?: number; maxRows?: number } = {}): FlattenResult {
  const maxDepth = options.maxDepth ?? 4
  const maxRows = options.maxRows ?? 200
  const rows: FieldRow[] = []
  let truncated = false

  const add = (path: string, label: string, v: unknown, depth: number, addressable: boolean) => {
    if (rows.length >= maxRows) {
      truncated = true
      return false
    }
    rows.push({ path, template: `{{${path}}}`, label, type: typeOf(v), preview: previewOf(v), full: fullText(v), depth, addressable })
    return true
  }

  const walk = (v: unknown, path: string, depth: number) => {
    if (depth >= maxDepth) {
      if (typeOf(v) === 'object' || typeOf(v) === 'list') truncated = true
      return
    }
    if (Array.isArray(v)) {
      if (v.length > 0 && add(`${path}[0]`, '[0]', v[0], depth + 1, true)) walk(v[0], `${path}[0]`, depth + 1)
      return
    }
    if (v !== null && typeof v === 'object') {
      for (const [key, child] of Object.entries(v as Record<string, unknown>)) {
        const childPath = `${path}.${key}`
        if (!add(childPath, key, child, depth + 1, SAFE_KEY.test(key))) return
        walk(child, childPath, depth + 1)
      }
    }
  }

  // the root itself first, so a plain text/number input can be copied as {{input}}
  add(root, root, value, 0, true)
  walk(value, root, 0)
  return { rows, truncated }
}
