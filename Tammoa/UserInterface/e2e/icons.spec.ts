import { expect, test, type Page } from '@playwright/test'

/**
 * Icon audit — house rule: every clickable thing has an icon.
 * Copy to e2e/icons.spec.ts, list your routes, and add a step for every STATE that shows different controls
 * (offline/connected, banners, dialogs, after an action). Fails with the offending controls listed.
 *
 * A control that is genuinely not a labelled button (e.g. a drag grip) opts out with data-icon-exempt="<reason>".
 * Native <select>/<input> are not buttons: give them a leading field icon (checked in the last test).
 */
const CLICKABLE = 'button, a[href], [role="radio"], [role="tab"], [role="menuitem"], [role="slider"], summary'

export async function withoutIcon(page: Page): Promise<string[]> {
  return page.evaluate((selector) => {
    const bad: string[] = []
    for (const el of Array.from(document.querySelectorAll<HTMLElement>(selector))) {
      const r = el.getBoundingClientRect()
      if (r.width === 0 || r.height === 0 || getComputedStyle(el).visibility === 'hidden') continue
      if (el.hasAttribute('data-icon-exempt') || el.querySelector('svg[data-icon]')) continue
      bad.push(`${el.tagName.toLowerCase()}[${el.getAttribute('data-testid') ?? el.textContent?.trim().slice(0, 30)}]`)
    }
    return bad
  }, CLICKABLE)
}

const ROUTES = ['/#/'] // every route of the app (the edit route needs an id: see below)

for (const route of ROUTES) {
  test(`${route}: every clickable has an icon`, async ({ page }) => {
    await page.goto(route)
    await page.getByTestId('flows').waitFor()
    expect(await withoutIcon(page)).toEqual([])
  })
}

test('flows with rows, the edit page and all three dialogs: every clickable has an icon', async ({ page, request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: `Icons ${Date.now()}`, description: 'audit' } })).json()
  await page.goto('/#/')
  await expect(page.getByTestId(`flow-row-${flow.id}`)).toBeVisible()
  expect(await withoutIcon(page), 'list with rows').toEqual([])

  await page.getByTestId('new-flow').click()
  await expect(page.getByTestId('create-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'create dialog').toEqual([])
  await page.getByTestId('create-cancel').click()

  await page.getByTestId(`flow-delete-${flow.id}`).click()
  await expect(page.getByTestId('delete-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'delete dialog').toEqual([])
  await page.getByTestId('delete-cancel').click()

  await page.goto(`/#/flows/${flow.id}`)
  await expect(page.getByTestId('flow-edit')).toBeVisible()
  expect(await withoutIcon(page), 'edit page').toEqual([])
  await page.getByTestId('edit-delete').click()
  await expect(page.getByTestId('delete-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'delete dialog on edit page').toEqual([])
})

test('the card editor (palette, selected card, run panel, running state): every clickable has an icon', async ({ page, request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: `Editor ${Date.now()}`, description: '' } })).json()
  const nodes = [
    { id: 't', type: 'trigger.manual', name: 'Manual trigger', position: { x: 0, y: 0 }, params: {} },
    { id: 'c', type: 'script.shell', name: 'Script', position: { x: 280, y: 0 }, params: { command: 'sleep 3' } },
  ]
  await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes, edges: [{ id: 'e1', source: 't', target: 'c' }] } } })
  await page.goto(`/#/flows/${flow.id}`)
  await expect(page.getByTestId('canvas')).toBeVisible()
  await expect(page.getByTestId('node-c')).toBeVisible()
  expect(await withoutIcon(page), 'editor, nothing selected').toEqual([])

  await page.getByTestId('palette-toggle').click()
  await expect(page.getByTestId('palette')).toBeVisible()
  expect(await withoutIcon(page), 'palette open').toEqual([])
  await page.keyboard.press('Escape')

  await page.getByTestId('node-c').click()
  await expect(page.getByTestId('inspector')).toBeVisible()
  expect(await withoutIcon(page), 'card selected (inspector with all param kinds)').toEqual([])

  await page.getByTestId('run').click()
  await expect(page.locator('[data-testid^="run-cancel-"]')).toBeVisible()
  expect(await withoutIcon(page), 'a run is running (cancel button, spinner)').toEqual([])
  await page.locator('[data-testid^="run-cancel-"]').click()
  await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'cancelled')
  expect(await withoutIcon(page), 'after the run').toEqual([])

  await page.locator('[data-testid="edge-e1"] path').last().click({ force: true })
  await expect(page.getByTestId('edge-delete')).toBeVisible()
  expect(await withoutIcon(page), 'connection selected').toEqual([])
})

test('the audit really looks at the controls (not an empty selection)', async ({ page }) => {
  await page.goto(ROUTES[0])
  await page.getByTestId('flows').waitFor()
  const visible = await page.locator(CLICKABLE).evaluateAll((els) => els.filter((e) => e.getBoundingClientRect().width > 0).length)
  expect(visible).toBeGreaterThanOrEqual(2) // brand, theme toggle, new flow (+ rows)
})

test('the audit itself catches an icon-less button', async ({ page }) => {
  await page.goto(ROUTES[0])
  await page.evaluate(() => {
    const b = document.createElement('button')
    b.textContent = 'Text only'
    b.dataset.testid = 'rogue'
    document.body.append(b)
  })
  expect(await withoutIcon(page)).toContain('button[rogue]')
})

test('form fields carry a leading icon', async ({ page }) => {
  await page.goto(ROUTES[0])
  await page.getByTestId('new-flow').click()
  for (const id of ['create-name', 'create-description']) {
    const count = await page.getByTestId(id).locator('xpath=..').locator('svg[data-icon]').count()
    expect(count, id).toBeGreaterThanOrEqual(1)
  }
})
