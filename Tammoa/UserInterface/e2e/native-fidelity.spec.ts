import { expect, test, type Page } from '@playwright/test'
import { card, edge, makeFlow, node, open, trigger } from './helpers'

/**
 * Runs in WebKit (the macOS desktop window is a WKWebView; Windows uses WebView2/Chromium).
 * Chromium honours padding on a native <select>; WebKit does not, so an icon placed over the select's left padding lands
 * on top of the text. These checks fail in the engine where the bug shows.
 */
async function geometry(page: Page, testId: string) {
  return page.evaluate((id) => {
    const select = document.querySelector<HTMLSelectElement>(`[data-testid="${id}"]`)!
    const wrap = select.parentElement!
    const icon = wrap.querySelector('svg[data-icon]')!.getBoundingClientRect()
    const chevron = wrap.querySelectorAll('svg[data-icon]')[1]!.getBoundingClientRect()
    const s = select.getBoundingClientRect()
    const cs = getComputedStyle(select)
    return {
      appearance: cs.appearance,
      paddingLeft: parseFloat(cs.paddingLeft),
      paddingRight: parseFloat(cs.paddingRight),
      iconRightOffset: icon.right - s.left,
      chevronLeftOffset: chevron.left - s.left,
      width: s.width,
    }
  }, testId)
}

async function openWithCards(page: Page, request: import('@playwright/test').APIRequestContext) {
  const flow = await makeFlow(
    request,
    [trigger(), card('i', 'logic.if', {}, 260, 0, 'Decide'), card('c', 'script.shell', { command: 'echo' }, 560, 0, 'Script'), card('a', 'ai.ollama', {}, 560, 120, 'Ask')],
    [edge('t', 'i')],
  )
  await open(page, flow.id)
}

test('selects use their own arrow so padding (and the leading icon) is honoured', async ({ page, request }) => {
  await openWithCards(page, request)
  for (const [cardId, ids] of [['i', ['param-input-operator']], ['c', ['param-input-language']], ['a', ['param-input-connection', 'param-input-model']]] as const) {
    await node(page, cardId).click()
    for (const id of ids) {
      await expect(page.getByTestId(id)).toBeVisible()
      const g = await geometry(page, id)
      expect(g.appearance, `${id} appearance`).toBe('none')
      expect(g.paddingLeft, `${id} paddingLeft`).toBeGreaterThanOrEqual(g.iconRightOffset) // the text starts after the icon
      expect(g.paddingRight, `${id} paddingRight`).toBeGreaterThanOrEqual(g.width - g.chevronLeftOffset) // and stops before the chevron
    }
  }
})

test('the connection dialog select keeps its text clear of the icon', async ({ page }) => {
  await page.goto('/#/connections')
  await page.getByTestId('new-connection').click()
  await expect(page.getByTestId('conn-provider')).toBeVisible()
  const g = await geometry(page, 'conn-provider')
  expect(g.appearance).toBe('none')
  expect(g.paddingLeft).toBeGreaterThanOrEqual(g.iconRightOffset)
})

test('a select is as tall as the text inputs next to it (no squashed native control)', async ({ page, request }) => {
  await openWithCards(page, request)
  await node(page, 'c').click()
  const h = async (id: string) => (await page.getByTestId(id).boundingBox())!.height
  expect(Math.abs((await h('param-input-language')) - (await h('param-input-workingDirectory')))).toBeLessThanOrEqual(4)
})

test('the code editor renders in WebKit: line numbers, typing, brackets, the large dialog', async ({ page, request }) => {
  await openWithCards(page, request)
  await node(page, 'c').click()
  const field = page.getByTestId('param-input-command')
  await expect(field.locator('.cm-lineNumbers')).toBeVisible()
  await field.locator('.cm-content').first().click()
  await page.keyboard.press('ControlOrMeta+a')
  await page.keyboard.type('echo (')
  await expect(field.locator('.cm-content').first()).toContainText('echo ()')
  await page.getByTestId('code-expand').click()
  await expect(page.getByTestId('code-dialog-editor').locator('.cm-content')).toContainText('echo ()')
  await page.screenshot({ path: 'test-results/webkit-code-dialog.png' })
})

test('the side panel resizes in WebKit and the canvas draws cards and connections', async ({ page, request }) => {
  await openWithCards(page, request)
  const width = async () => Math.round((await page.getByTestId('side-panel').boundingBox())!.width)
  const before = await width()
  const h = (await page.getByTestId('side-resizer').boundingBox())!
  await page.mouse.move(h.x + h.width / 2, h.y + 40)
  await page.mouse.down()
  await page.mouse.move(h.x + h.width / 2 - 120, h.y + 40, { steps: 6 })
  await page.mouse.up()
  expect(await width()).toBeGreaterThan(before + 100)
  await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(1)
  await expect(node(page, 'i').getByTestId('handle-label')).toHaveText(['true', 'false'])
  await page.screenshot({ path: 'test-results/webkit-editor.png' })
})

test('Input, Output and Guide tabs, the field list and the example snippets render in WebKit', async ({ page, request }) => {
  const flow = await makeFlow(request, [trigger(), card('a', 'script.shell', { command: `echo '{"name":"Sok","items":["a","b"]}'` }, 280), card('b', 'script.shell', { language: 'javascript', command: 'console.log(input.json.name)' }, 560)], [edge('t', 'a'), edge('a', 'b')])
  await open(page, flow.id)
  await page.getByTestId('run-input').fill('hello')
  await page.getByTestId('run').click()
  await expect(node(page, 'b')).toHaveAttribute('data-status', 'succeeded')
  await node(page, 'b').click()
  await expect(page.getByTestId('code-hint')).toContainText('input.json.name') // language-specific hint under the editor
  await page.getByTestId('inspector-tab-input').click()
  await expect(page.locator('[data-path="input.json.name"]')).toBeVisible()
  await expect(page.locator('[data-path="input.json.items[0]"]')).toBeVisible()
  await expect(page.locator('[data-path="run.input"]').getByTestId('field-preview')).toHaveText('hello')
  await page.screenshot({ path: 'test-results/webkit-input-tab.png' })
  await page.getByTestId('inspector-tab-guide').click()
  await expect(page.getByTestId('guide-example')).not.toHaveCount(0)
  await expect(page.getByTestId('guide-example').first().locator('.cm-content')).toContainText('const name = input.json.name')
  await page.screenshot({ path: 'test-results/webkit-guide-tab.png' })
})

test('live run state arrives over server-sent events in WebKit', async ({ page, request }) => {
  const flow = await makeFlow(request, [trigger(), card('s', 'script.shell', { command: 'echo webkit' })], [edge('t', 's')])
  await open(page, flow.id)
  await page.getByTestId('run').click()
  await expect(node(page, 's')).toHaveAttribute('data-status', 'succeeded')
  await expect(page.getByTestId('runs')).toContainText('Succeeded')
})

test('smoke in WebKit: no horizontal overflow on the main screens', async ({ page, request }) => {
  const flow = await makeFlow(request, [trigger()])
  for (const route of ['/#/', '/#/connections', `/#/flows/${flow.id}`]) {
    await page.goto(route)
    await page.waitForLoadState('networkidle')
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow, route).toBeLessThanOrEqual(0)
  }
})
