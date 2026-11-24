import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '@playwright/test'
import { unique } from './helpers'

// These tests change app-wide settings and back up the whole (disposable) data folder: one at a time.
test.describe.configure({ mode: 'serial' })

const available = { currentVersion: '0.1.0', latestVersion: '9.9.9', updateAvailable: true, url: 'https://github.com/someone/Tammoa/releases/tag/v9.9.9' }
const latest = { currentVersion: '0.1.0', latestVersion: '0.1.0', updateAvailable: false }

test.describe('Settings: updates', () => {
  test.afterEach(async ({ request }) => {
    await request.put('/api/settings', { data: { checkUpdatesAtStart: false } })
  })

  test('the page shows the version and the three possible answers of a check', async ({ page }) => {
    await page.goto('/#/settings')
    await expect(page.getByTestId('settings-version')).toHaveText(/^\d+\.\d+\.\d+/)
    await expect(page.getByTestId('update-result')).toHaveCount(0) // nothing is asked until the button is pressed

    let answer: unknown = available
    await page.route('**/api/app/update', (route) => route.fulfill({ json: answer }))
    await page.getByTestId('check-updates').click()
    await expect(page.getByTestId('update-result')).toContainText('Version 9.9.9 is available (you have 0.1.0)')
    await expect(page.getByTestId('update-url')).toHaveText(available.url)

    answer = latest
    await page.getByTestId('check-updates').click()
    await expect(page.getByTestId('update-result')).toContainText('You have the latest version (0.1.0)')
    await expect(page.getByTestId('update-url-row')).toHaveCount(0)

    answer = { currentVersion: '0.1.0', updateAvailable: false, error: 'Could not reach GitHub. Check your internet connection.' }
    await page.getByTestId('check-updates').click()
    await expect(page.getByTestId('update-result')).toContainText('Could not reach GitHub. Check your internet connection.')
  })

  test('a check that fails outright is a message, not a broken page', async ({ page }) => {
    await page.route('**/api/app/update', (route) => route.fulfill({ status: 500, json: { error: 'Unexpected server error.' } }))
    await page.goto('/#/settings')
    await page.getByTestId('check-updates').click()
    await expect(page.getByTestId('update-result')).toContainText('Unexpected server error.')
    await expect(page.getByTestId('check-updates')).toBeEnabled()
  })

  test('the real check against GitHub answers with something readable (a version, or why not)', async ({ page, request }) => {
    const info = await (await request.get('/api/app/update')).json()
    expect(info.currentVersion).toMatch(/^\d+\.\d+\.\d+/)
    expect(info.updateAvailable === true || info.latestVersion !== undefined || typeof info.error === 'string').toBe(true)
    await page.goto('/#/settings')
    await page.getByTestId('check-updates').click()
    await expect(page.getByTestId('update-result')).toBeVisible()
  })

  test('by default the app never contacts GitHub on its own', async ({ page }) => {
    let calls = 0
    await page.route('**/api/app/update', (route) => { calls++; return route.fulfill({ json: available }) })
    await page.goto('/#/')
    await expect(page.getByTestId('version')).toBeVisible()
    await page.waitForTimeout(500)
    expect(calls).toBe(0)
    await expect(page.getByTestId('update-available')).toHaveCount(0)
  })

  test('switch on "check when the app opens": saved, kept after a reload, and a newer version shows in the top bar', async ({ page }) => {
    let calls = 0
    await page.route('**/api/app/update', (route) => { calls++; return route.fulfill({ json: available }) })
    await page.goto('/#/settings')
    const sw = page.getByTestId('auto-update')
    await expect(sw).toHaveAttribute('aria-checked', 'false')
    await sw.click()
    await expect(sw).toHaveAttribute('aria-checked', 'true')
    await expect(sw).toContainText('on')

    await page.reload()
    await expect(page.getByTestId('auto-update')).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByTestId('update-available')).toContainText('Update 9.9.9')
    expect(calls).toBeGreaterThanOrEqual(1)

    await page.goto('/#/')
    await page.getByTestId('update-available').click()
    await expect(page).toHaveURL(/#\/settings/)
  })

  test('on, but already up to date: no line in the top bar', async ({ page, request }) => {
    await request.put('/api/settings', { data: { checkUpdatesAtStart: true } })
    await page.route('**/api/app/update', (route) => route.fulfill({ json: latest }))
    await page.goto('/#/')
    await expect(page.getByTestId('version')).toBeVisible()
    await page.waitForTimeout(500)
    await expect(page.getByTestId('update-available')).toHaveCount(0)
  })

  test('a settings problem is shown and the switch keeps its old position', async ({ page }) => {
    await page.goto('/#/settings')
    await expect(page.getByTestId('auto-update')).toHaveAttribute('aria-checked', 'false')
    await page.route('**/api/settings', (route) => (route.request().method() === 'PUT' ? route.fulfill({ status: 500, json: { error: 'Unexpected server error.' } }) : route.fallback()))
    await page.getByTestId('auto-update').click()
    await expect(page.getByTestId('settings-error')).toContainText('Unexpected server error.')
    await expect(page.getByTestId('auto-update')).toHaveAttribute('aria-checked', 'false')
  })
})

test.describe('Settings: backup', () => {
  const tmp: string[] = []
  test.afterAll(() => tmp.forEach((f) => fs.rmSync(f, { force: true })))

  function writeTemp(name: string, content: string) {
    const file = path.join(os.tmpdir(), `tammoa-e2e-${Date.now()}-${name}`)
    fs.writeFileSync(file, content)
    tmp.push(file)
    return file
  }

  async function makeFlow(request: import('@playwright/test').APIRequestContext, name: string) {
    const flow = await (await request.post('/api/flows', { data: { name, description: 'for the backup test' } })).json()
    const definition = { schemaVersion: 1, nodes: [{ id: 't', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} }], edges: [] }
    await request.put(`/api/flows/${flow.id}`, { data: { name, description: 'for the backup test', definition } })
    return flow as { id: string; name: string }
  }

  test('back up, lose a flow, restore it from the list: it comes back with the same id, never twice', async ({ page, request }) => {
    const name = unique('Precious')
    const flow = await makeFlow(request, name)
    await page.goto('/#/settings')
    await expect(page.getByTestId('backup-folder')).toContainText('backups')
    await page.getByTestId('backup-now').click()
    await expect(page.getByTestId('backup-made')).toContainText('Saved tammoa-backup-')
    const fileName = /Saved (tammoa-backup-[\w-]+\.json)/.exec((await page.getByTestId('backup-made').textContent()) ?? '')![1]
    const restore = page.getByTestId(`backup-restore-${fileName}`) // the file just made, not "the first row": the list reloads a moment later
    await expect(restore).toBeVisible()
    await expect(page.getByTestId(`backup-row-${fileName}`)).toContainText('flow')

    expect((await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: name } })).status()).toBe(204)
    expect((await request.get(`/api/flows/${flow.id}`)).status()).toBe(404)

    await restore.click()
    await expect(page.getByTestId('restore-result')).toContainText('restored (disabled until you switch them on)')
    const back = await (await request.get(`/api/flows/${flow.id}`)).json()
    expect(back.name).toBe(name)
    expect(back.enabled).toBe(false)

    await restore.click()
    await expect(page.getByTestId('restore-result')).toContainText('already here, left as they are')
    await expect(page.getByTestId('restore-result')).not.toContainText('restored (disabled')
    const all = await (await request.get('/api/flows')).json()
    expect(all.filter((f: any) => f.name === name)).toHaveLength(1)

    await page.goto('/#/')
    await expect(page.getByTestId(`flow-row-${flow.id}`)).toBeVisible() // the Flows page shows what came back
  })

  test('the file never holds an API key, and a restored connection asks for it again', async ({ page, request }) => {
    const connName = unique('Keyed')
    const conn = await (await request.post('/api/connections', { data: { name: connName, provider: 'ollama', baseUrl: 'http://127.0.0.1:1', apiKey: 'sekret-e2e-key-777' } })).json()
    const made = await (await request.post('/api/backup')).json()
    const text = fs.readFileSync(made.path, 'utf8')
    expect(text).not.toContain('sekret-e2e-key-777')
    expect(text).toContain(connName)

    expect((await request.delete(`/api/connections/${conn.id}`)).status()).toBe(204)
    await page.goto('/#/settings')
    await page.getByTestId(`backup-restore-${made.fileName}`).click()
    await expect(page.getByTestId('restore-result')).toContainText('connection restored')
    await expect(page.getByTestId('restore-result')).toContainText(`Type the key again for: ${connName}`)
    const restored = (await (await request.get('/api/connections')).json()).find((c: any) => c.id === conn.id)
    expect(restored).toMatchObject({ name: connName, hasKey: false })
    await request.delete(`/api/connections/${conn.id}`)
  })

  test('restore from a file you choose', async ({ page, request }) => {
    const name = unique('FromFile')
    const flow = await makeFlow(request, name)
    const made = await (await request.post('/api/backup')).json()
    expect((await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: name } })).status()).toBe(204)

    await page.goto('/#/settings')
    await page.getByTestId('backup-file-input').setInputFiles(made.path)
    await expect(page.getByTestId('restore-result')).toContainText('restored')
    expect((await request.get(`/api/flows/${flow.id}`)).status()).toBe(200)
  })

  test('files that are not backups are refused with a reason, and nothing changes', async ({ page, request }) => {
    const before = (await (await request.get('/api/flows')).json()).length
    await page.goto('/#/settings')
    const input = page.getByTestId('backup-file-input')

    await input.setInputFiles(writeTemp('notes.json', 'hello, not json'))
    await expect(page.getByTestId('restore-error')).toContainText('not a Tammoa backup')

    await input.setInputFiles(writeTemp('flow.json', JSON.stringify({ format: 'tammoa.flow', version: 1, name: 'x' })))
    await expect(page.getByTestId('restore-error')).toContainText('not a Tammoa backup')

    await input.setInputFiles(writeTemp('future.json', JSON.stringify({ format: 'tammoa.backup', version: 99, createdAt: new Date().toISOString(), flows: [] })))
    await expect(page.getByTestId('restore-error')).toContainText('newer')
    await expect(page.getByTestId('restore-error')).toContainText('Update')
    await expect(page.getByTestId('restore-result')).toHaveCount(0)

    expect((await (await request.get('/api/flows')).json()).length).toBe(before)
  })

  test('one broken flow in a backup is reported by name and does not stop the others', async ({ page, request }) => {
    const goodName = unique('GoodOne')
    const doc = {
      format: 'tammoa.backup', version: 1, createdAt: new Date().toISOString(), flows: [
        { id: `bad-${Date.now()}`, name: 'Broken one', description: '', definition: { nodes: [{ id: 'x', type: 'no.such.card', name: 'X', position: { x: 0, y: 0 }, params: {} }], edges: [] } },
        { id: `good-${Date.now()}`, name: goodName, description: '', definition: { nodes: [], edges: [] } },
      ],
    }
    await page.goto('/#/settings')
    await page.getByTestId('backup-file-input').setInputFiles(writeTemp('mixed.json', JSON.stringify(doc)))
    await expect(page.getByTestId('restore-result')).toContainText('1 flow restored')
    await expect(page.getByTestId('restore-problem')).toContainText('Broken one')
    const all = await (await request.get('/api/flows')).json()
    expect(all.some((f: any) => f.name === goodName)).toBe(true)
    expect(all.some((f: any) => f.name === 'Broken one')).toBe(false)
  })

  test('a backup problem from the server is shown', async ({ page }) => {
    await page.route('**/api/backup', (route) => (route.request().method() === 'POST' ? route.fulfill({ status: 500, json: { error: 'Unexpected server error.' } }) : route.fallback()))
    await page.goto('/#/settings')
    await page.getByTestId('backup-now').click()
    await expect(page.getByTestId('backup-error')).toContainText('Unexpected server error.')
    await expect(page.getByTestId('backup-now')).toBeEnabled()
  })

  test('the folder can be shown in the file manager (a refusal is a message)', async ({ page }) => {
    await page.route('**/api/backup/open-folder', (route) => route.fulfill({ status: 409, json: { error: 'Could not open the folder. It is here: /somewhere' } }))
    await page.goto('/#/settings')
    await page.getByTestId('backup-open-folder').click()
    await expect(page.getByTestId('backup-error')).toContainText('It is here: /somewhere')
  })
})
