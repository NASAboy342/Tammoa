export const SIDE_MIN = 280
export const SIDE_MAX = 900
export const SIDE_DEFAULT = 340

/** Keeps the side panel usable: never narrower than SIDE_MIN, never wider than SIDE_MAX or leaving less than `reserve` px for the canvas. */
export function clampSideWidth(width: number, viewport: number, reserve = 320): number {
  if (!Number.isFinite(width)) return SIDE_DEFAULT
  const max = Math.max(SIDE_MIN, Math.min(SIDE_MAX, viewport - reserve))
  return Math.round(Math.min(max, Math.max(SIDE_MIN, width)))
}

/** The panel sits on the right, so dragging the handle LEFT widens it. */
export const widthAfterDrag = (startWidth: number, startX: number, currentX: number) => startWidth + (startX - currentX)

/** Language of a code editor from a card parameter: its own fixed language, or the value of the param it follows. */
export function editorLanguage(param: { codeLanguage?: string; codeLanguageParam?: string }, values: Record<string, unknown>): string {
  if (param.codeLanguageParam) {
    const v = values[param.codeLanguageParam]
    return typeof v === 'string' && v ? v : 'shell'
  }
  return param.codeLanguage ?? 'text'
}
