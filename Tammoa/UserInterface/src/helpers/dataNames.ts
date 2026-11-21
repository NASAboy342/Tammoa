/** Mirrors the backend rule for the name of a Data card (it is how other cards refer to it: {{data.Name}}), so the UI can warn before saving. */
export const DATA_NAME_MAX = 60
const FORBIDDEN = ['.', '[', ']', '|', '{', '}']

/** @returns null when the name is fine, otherwise why not. */
export function dataNameProblem(name: string): string | null {
  const t = name.trim()
  if (!t) return 'A Data card needs a name: it is how other cards refer to it.'
  if (t.length > DATA_NAME_MAX) return `A Data card name is limited to ${DATA_NAME_MAX} characters.`
  const bad = [...t].find((c) => FORBIDDEN.includes(c))
  return bad ? `The name contains "${bad}". A reference cannot contain . [ ] | { or }.` : null
}
