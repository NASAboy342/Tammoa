import { expect, test, type Page } from '@playwright/test'

const unique = (label: string) => `${label} ${Date.now()}-${Math.floor(Math.random() * 1e4)}`

async function createViaUi(page: Page, name: string, description = '') {
  await page.getByTestId('new-flow').click()
  await page.getByTestId('create-name').fill(name)
  if (description) await page.getByTestId('create-description').fill(description)
  await page.getByTestId('create-submit').click()
  await expect(page.getByTestId('flow-edit')).toBeVisible() // lands on the edit page
}

const rowFor = (page: Page, name: string) => page.getByTestId('flow-list').locator('li', { hasText: name })

test.beforeEach(async ({ page }) => {
  await page.goto('/#/')
  await expect(page.getByTestId('flows')).toBeVisible()
})

test('create: validates, creates a disabled flow, lands on edit, shows in the list', async ({ page }) => {
  const name = unique('Create')
  await page.getByTestId('new-flow').click()
  await expect(page.getByTestId('create-submit')).toBeDisabled() // blank name
  await page.getByTestId('create-name').fill(name)
  await page.getByTestId('create-description').fill('first description')
  await page.getByTestId('create-submit').click()
  await expect(page.getByTestId('edit-name')).toHaveValue(name)
  await expect(page.getByTestId('edit-toggle')).toHaveAttribute('aria-checked', 'false')
  await page.getByTestId('back').click()
  const row = rowFor(page, name)
  await expect(row).toBeVisible()
  await expect(row).toContainText('first description')
  await expect(row).toContainText('0 cards')
})

test('create: cancel closes the dialog and creates nothing', async ({ page }) => {
  const name = unique('Cancelled')
  await page.getByTestId('new-flow').click()
  await page.getByTestId('create-name').fill(name)
  await page.getByTestId('create-cancel').click()
  await expect(page.getByTestId('create-dialog')).toBeHidden()
  await expect(rowFor(page, name)).toHaveCount(0)
})

test('edit: rename and re-describe persist; save is disabled until something changes', async ({ page }) => {
  const name = unique('Edit')
  await createViaUi(page, name)
  await expect(page.getByTestId('edit-save')).toBeDisabled()
  const renamed = `${name} renamed`
  await page.getByTestId('edit-name').fill(renamed)
  await page.getByTestId('edit-description').fill('now described')
  await page.getByTestId('edit-save').click()
  await expect(page.getByTestId('edit-saved')).toBeVisible()
  await page.reload()
  await expect(page.getByTestId('edit-name')).toHaveValue(renamed)
  await expect(page.getByTestId('edit-description')).toHaveValue('now described')
})

test('edit: a blank name cannot be saved', async ({ page }) => {
  await createViaUi(page, unique('Blank'))
  await page.getByTestId('edit-name').fill('   ')
  await expect(page.getByTestId('edit-save')).toBeDisabled()
})

test('enable / disable from the list and from the edit page', async ({ page }) => {
  const name = unique('Toggle')
  await createViaUi(page, name)
  await page.getByTestId('edit-toggle').click()
  await expect(page.getByTestId('edit-toggle')).toHaveAttribute('aria-checked', 'true')
  await page.getByTestId('back').click()
  const toggle = rowFor(page, name).locator('[role="switch"]')
  await expect(toggle).toHaveAttribute('aria-checked', 'true')
  await toggle.click()
  await expect(toggle).toHaveAttribute('aria-checked', 'false')
  await page.reload()
  await expect(rowFor(page, name).locator('[role="switch"]')).toHaveAttribute('aria-checked', 'false')
})

test('duplicate: adds a disabled "(copy)" next to the original', async ({ page }) => {
  const name = unique('Dup')
  await createViaUi(page, name)
  await page.getByTestId('edit-toggle').click()
  await page.getByTestId('back').click()
  await rowFor(page, name).getByTitle('Duplicate flow').click()
  const copy = rowFor(page, `${name} (copy)`)
  await expect(copy).toBeVisible()
  await expect(copy.locator('[role="switch"]')).toHaveAttribute('aria-checked', 'false')
})

test('delete: strict confirmation (exact name) from the list', async ({ page }) => {
  const name = unique('Delete')
  await createViaUi(page, name)
  await page.getByTestId('back').click()
  await rowFor(page, name).getByTitle('Delete flow').click()
  const confirm = page.getByTestId('delete-confirm')
  await expect(page.getByTestId('delete-dialog')).toBeVisible()
  await expect(confirm).toBeDisabled()
  for (const wrong of [name.toLowerCase(), `${name} `, name.slice(0, -1)]) {
    await page.getByTestId('delete-input').fill(wrong)
    await expect(confirm).toBeDisabled()
  }
  await page.getByTestId('delete-input').fill(name)
  await expect(confirm).toBeEnabled()
  await confirm.click()
  await expect(page.getByTestId('delete-dialog')).toBeHidden()
  await expect(rowFor(page, name)).toHaveCount(0)
  await page.reload()
  await expect(rowFor(page, name)).toHaveCount(0)
})

test('delete: keep closes the dialog and the flow stays', async ({ page }) => {
  const name = unique('Keep')
  await createViaUi(page, name)
  await page.getByTestId('back').click()
  await rowFor(page, name).getByTitle('Delete flow').click()
  await page.getByTestId('delete-cancel').click()
  await expect(page.getByTestId('delete-dialog')).toBeHidden()
  await expect(rowFor(page, name)).toBeVisible()
})

test('delete from the edit page returns to the list', async ({ page }) => {
  const name = unique('EditDelete')
  await createViaUi(page, name)
  await page.getByTestId('edit-delete').click()
  await page.getByTestId('delete-input').fill(name)
  await page.getByTestId('delete-confirm').click()
  await expect(page.getByTestId('flows')).toBeVisible()
  await expect(rowFor(page, name)).toHaveCount(0)
})

test('backend rejects a delete without the exact name (API contract)', async ({ request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: unique('Api'), description: '' } })).json()
  const bad = await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: 'nope' } })
  expect(bad.status()).toBe(400)
  const noBody = await request.delete(`/api/flows/${flow.id}`)
  expect(noBody.status()).toBe(400)
  expect((await request.get(`/api/flows/${flow.id}`)).status()).toBe(200)
  const ok = await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: flow.name } })
  expect(ok.status()).toBe(204)
  expect((await request.get(`/api/flows/${flow.id}`)).status()).toBe(404)
})

test('export then import round-trips through the API as a new disabled flow', async ({ request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: unique('Exp'), description: 'portable' } })).json()
  await request.post(`/api/flows/${flow.id}/enable`)
  const doc = await (await request.get(`/api/flows/${flow.id}/export`)).json()
  expect(doc).toMatchObject({ format: 'tammoa.flow', version: 1, name: flow.name })
  expect(doc.id).toBeUndefined()
  expect(doc.enabled).toBeUndefined()
  const imported = await (await request.post('/api/flows/import', { data: doc })).json()
  expect(imported.id).not.toBe(flow.id)
  expect(imported.enabled).toBe(false)
  expect(imported.name).toBe(flow.name)
  const bad = await request.post('/api/flows/import', { data: { ...doc, format: 'other' } })
  expect(bad.status()).toBe(400)
})

test('opening an unknown flow shows an error, not a blank page', async ({ page }) => {
  await page.goto('/#/flows/does-not-exist')
  await expect(page.getByTestId('edit-error')).toBeVisible()
})
