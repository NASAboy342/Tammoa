import { expect, test } from '@playwright/test'

test('header shows the backend version (frontend ↔ backend wiring)', async ({ page }) => {
  await page.goto('/#/')
  await expect(page.getByTestId('version')).toContainText('v')
})

test('theme toggle switches data-theme', async ({ page }) => {
  await page.goto('/#/')
  await page.getByTestId('theme-toggle').click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.getByTestId('theme-toggle').click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})

test('every route renders without console errors or failed /api calls', async ({ page, request }) => {
  const problems: string[] = []
  page.on('pageerror', (e) => problems.push(`pageerror: ${e.message}`))
  page.on('console', (m) => m.type() === 'error' && problems.push(`console: ${m.text()}`))
  page.on('response', (r) => r.url().includes('/api/') && r.status() >= 400 && problems.push(`${r.status()} ${r.url()}`))
  const flow = await (await request.post('/api/flows', { data: { name: `smoke-${Date.now()}`, description: '' } })).json()
  for (const [route, id] of [['/#/', 'flows'], [`/#/flows/${flow.id}`, 'flow-edit']]) {
    await page.goto(route)
    await expect(page.getByTestId(id)).toBeVisible()
  }
  expect(problems).toEqual([])
})
