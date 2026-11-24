import { expect, test, type Page } from '@playwright/test'
import { node, runAndWait, showJson, unique } from './helpers'
import { startFakeOllama } from './fake-ollama'

/** What a brand-new user sees: the welcome, the templates, the help when there is no Ollama connection yet. */

/** The real backend has flows from other tests: pretend this is a fresh install for the LIST only (creating a template still goes to the real backend). */
async function pretendEmpty(page: Page) {
  await page.route(/\/api\/flows$/, (route) => (route.request().method() === 'GET' ? route.fulfill({ json: [] }) : route.fallback()))
}

test.describe('first run: welcome and templates', () => {
  test('an empty list is a welcome with three steps and two ways to start', async ({ page }) => {
    await pretendEmpty(page)
    await page.goto('/#/')
    await expect(page.getByTestId('welcome-title')).toHaveText('Welcome to Tammoa')
    await expect(page.getByTestId('welcome-steps').locator('li')).toHaveCount(3)
    await expect(page.getByTestId('welcome-template')).toBeVisible()
    await expect(page.getByTestId('welcome-new')).toBeVisible()
    await page.getByTestId('welcome-connections').click()
    await expect(page).toHaveURL(/#\/connections/)
  })

  test('"Empty flow" in the welcome opens the usual create dialog', async ({ page }) => {
    await pretendEmpty(page)
    await page.goto('/#/')
    await page.getByTestId('welcome-new').click()
    await expect(page.getByTestId('create-dialog')).toBeVisible()
  })

  test('the template list shows what each one needs, and the no-AI ones say "Nothing to set up"', async ({ page }) => {
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await expect(page.getByTestId('template-dialog')).toBeVisible()
    await expect(page.getByTestId('template-list').getByRole('radio')).toHaveCount(5)
    await expect(page.getByTestId('template-hello')).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByTestId('template-details')).toContainText('Nothing to set up')
    await expect(page.getByTestId('template-ai')).toHaveCount(0)

    await page.getByTestId('template-web-api').click()
    await expect(page.getByTestId('template-need')).toContainText('Needs Internet')
    await page.getByTestId('template-summarize-text').click()
    await expect(page.getByTestId('template-need')).toContainText('Needs Ollama')
    await expect(page.getByTestId('template-description')).toContainText('summary')
    await page.getByTestId('template-cancel').click()
    await expect(page.getByTestId('template-dialog')).toHaveCount(0)
  })

  test('"Hello, flow" from the welcome: created, opened, and it really runs', async ({ page }) => {
    await pretendEmpty(page)
    await page.goto('/#/')
    await page.getByTestId('welcome-template').click()
    await page.getByTestId('template-hello').click()
    await page.getByTestId('template-create').click()

    await expect(page.getByTestId('canvas')).toBeVisible()
    await expect(page).toHaveURL(/#\/flows\//)
    await expect(node(page, 'start')).toBeVisible()
    await expect(node(page, 'greet')).toBeVisible()
    await page.getByTestId('run-input').fill('Sam')
    await page.getByTestId('run').click()
    await expect(node(page, 'greet')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'greet').click()
    await showJson(page, 'output')
    await expect(page.getByTestId('output-preview')).toContainText('Hello, Sam!')
  })

  test('the choose-a-path template takes the right branch', async ({ page }) => {
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await page.getByTestId('template-branch').click()
    await page.getByTestId('template-create').click()
    await expect(node(page, 'check')).toBeVisible()
    await page.getByTestId('run-input').fill('yes please')
    await page.getByTestId('run').click()
    await expect(node(page, 'yes')).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'no')).toHaveAttribute('data-status', 'skipped')
  })

  test('a template flow is a normal flow: disabled, in the list, and a new one each time', async ({ page, request }) => {
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await page.getByTestId('template-hello').click()
    await page.getByTestId('template-create').click()
    await expect(page.getByTestId('canvas')).toBeVisible()
    const id = page.url().split('/flows/')[1]
    const flow = await (await request.get(`/api/flows/${id}`)).json()
    expect(flow.name).toBe('Hello, flow')
    expect(flow.enabled).toBe(false)
    expect(flow.definition.nodes).toHaveLength(2)
    await page.goto('/#/')
    await expect(page.getByTestId(`flow-row-${id}`)).toBeVisible()
  })
})

test.describe('first run: AI templates', () => {
  test('without a connection the dialog says so and offers the way to add one', async ({ page }) => {
    await page.route(/\/api\/connections$/, (route) => (route.request().method() === 'GET' ? route.fulfill({ json: [] }) : route.fallback()))
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await page.getByTestId('template-summarize-file').click()
    await expect(page.getByTestId('template-needs-connection')).toContainText('No Ollama connection yet')
    await expect(page.getByTestId('template-connection')).toHaveCount(0)
    await page.getByTestId('template-go-connections').click()
    await expect(page).toHaveURL(/#\/connections/)
    await expect(page.getByTestId('template-dialog')).toHaveCount(0)
  })

  test('with a connection: choose it and a model, and every AI card of the flow is filled in and runs', async ({ page, request }) => {
    const fake = await startFakeOllama()
    try {
      const conn = await (await request.post('/api/connections', { data: { name: unique('Tpl'), provider: 'ollama', baseUrl: fake.url } })).json()
      await page.goto('/#/')
      await page.getByTestId('from-template').click()
      await page.getByTestId('template-summarize-text').click()
      await page.getByTestId('template-connection').selectOption('') // "choose later": no model to pick
      await expect(page.getByTestId('template-model')).toHaveCount(0)
      await page.getByTestId('template-connection').selectOption(conn.id)
      const model = page.getByTestId('template-model')
      await expect(model).toBeEnabled()
      await model.selectOption('tiny:1b')
      await page.getByTestId('template-create').click()

      await expect(page.getByTestId('canvas')).toBeVisible()
      const id = page.url().split('/flows/')[1]
      const ai = (await (await request.get(`/api/flows/${id}`)).json()).definition.nodes.find((n: any) => n.type === 'ai.ollama')
      expect(ai.params).toMatchObject({ connection: conn.id, model: 'tiny:1b' })

      await page.getByTestId('run-input').fill('A long text about ducks')
      await page.getByTestId('run').click()
      await expect(node(page, 'summary')).toHaveAttribute('data-status', 'succeeded')
      expect(fake.chats().at(-1)!.body.messages.at(-1).content).toContain('A long text about ducks')
    } finally {
      await fake.close()
    }
  })

  test('a connection that cannot list models says why, instead of an empty list', async ({ page, request }) => {
    const conn = await (await request.post('/api/connections', { data: { name: unique('Dead'), provider: 'ollama', baseUrl: 'http://127.0.0.1:1' } })).json()
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await page.getByTestId('template-summarize-text').click()
    await page.getByTestId('template-connection').selectOption(conn.id)
    await expect(page.getByTestId('template-models-error')).toContainText('Could not reach Ollama at http://127.0.0.1:1')
    await expect(page.getByTestId('template-model')).toBeDisabled()
    // the flow can still be created: the card asks for a model later
    await page.getByTestId('template-create').click()
    await expect(page.getByTestId('canvas')).toBeVisible()
  })

  test('the template without a connection leaves the AI card to be filled in, and running it says what is missing', async ({ page, request }) => {
    await page.route(/\/api\/connections$/, (route) => (route.request().method() === 'GET' ? route.fulfill({ json: [] }) : route.fallback()))
    await page.goto('/#/')
    await page.getByTestId('from-template').click()
    await page.getByTestId('template-summarize-text').click()
    await page.getByTestId('template-create').click()
    await expect(page.getByTestId('canvas')).toBeVisible()
    const id = page.url().split('/flows/')[1]
    const run = await runAndWait(request, id, 'x')
    expect(run.status).toBe('failed')
    expect(run.nodes.find((n) => n.nodeId === 'summary')!.error).toContain('Choose a connection')
  })
})

test('no connections yet: the Connections page explains how to get Ollama going', async ({ page }) => {
  await page.route(/\/api\/connections$/, (route) => (route.request().method() === 'GET' ? route.fulfill({ json: [] }) : route.fallback()))
  await page.goto('/#/connections')
  await expect(page.getByTestId('connections-empty')).toBeVisible()
  await expect(page.getByTestId('connections-help')).toContainText('ollama pull')
  await expect(page.getByTestId('connections-help')).toContainText('http://localhost:11434')
})
