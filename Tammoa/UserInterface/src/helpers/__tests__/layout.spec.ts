import { describe, expect, it } from 'vitest'
import { SIDE_DEFAULT, SIDE_MAX, SIDE_MIN, clampSideWidth, editorLanguage, widthAfterDrag } from '../layout'

describe('clampSideWidth', () => {
  it('keeps a sensible width as is', () => expect(clampSideWidth(400, 1400)).toBe(400))
  it('never goes below the minimum or above the maximum', () => {
    expect(clampSideWidth(50, 1400)).toBe(SIDE_MIN)
    expect(clampSideWidth(5000, 3000)).toBe(SIDE_MAX)
  })
  it('leaves room for the canvas on a narrow window', () => {
    expect(clampSideWidth(800, 900)).toBe(580)
    expect(clampSideWidth(800, 500)).toBe(SIDE_MIN) // even then the panel stays usable
  })
  it('falls back to the default for garbage', () => {
    expect(clampSideWidth(Number.NaN, 1400)).toBe(SIDE_DEFAULT)
    expect(clampSideWidth(Number.POSITIVE_INFINITY, 1400)).toBe(SIDE_DEFAULT)
  })
  it('rounds to whole pixels', () => expect(clampSideWidth(400.6, 1400)).toBe(401))
})

describe('widthAfterDrag', () => {
  it('dragging the handle left widens the right-hand panel, right narrows it', () => {
    expect(widthAfterDrag(340, 500, 450)).toBe(390)
    expect(widthAfterDrag(340, 500, 560)).toBe(280)
    expect(widthAfterDrag(340, 500, 500)).toBe(340)
  })
})

describe('editorLanguage', () => {
  it('uses the param it follows, falling back to shell', () => {
    expect(editorLanguage({ codeLanguageParam: 'language' }, { language: 'python' })).toBe('python')
    expect(editorLanguage({ codeLanguageParam: 'language' }, {})).toBe('shell')
    expect(editorLanguage({ codeLanguageParam: 'language' }, { language: '' })).toBe('shell')
  })
  it('uses a fixed language, or plain text', () => {
    expect(editorLanguage({ codeLanguage: 'json' }, {})).toBe('json')
    expect(editorLanguage({}, {})).toBe('text')
  })
})
