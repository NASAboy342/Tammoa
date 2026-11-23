import { expect, test, type APIRequestContext } from '@playwright/test'
import { apiBase, node, unique } from './helpers'

/**
 * Agent access: the page, and what an agent (a request with `Authorization: Bearer <token>`) can really do.
 * The settings are global to the app, so these tests run one after the other and start from "off".
 */
test.describe.configure({ mode: 'serial' })

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` })

async function reset(request: APIRequestContext) {
  await request.put('/api/agent/access', { data: { enabled: false } })
}
const info = async (request: APIRequestContext) => (await (await request.get('/api/agent/access')).json()) as { enabled: boolean; allowRun: boolean; url: string; token: string }

test.beforeEach(async ({ request }) => reset(request))
test.afterAll(async ({ request }) => reset(request))

test.describe('Agent access', () => {
  test('the page: off by default, the address and a hidden token, switches work and are kept', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await page.goto('/#/agent')
    await expect(page.getByTestId('agent-enabled')).toHaveAttribute('aria-checked', 'false')
    await expect(page.getByTestId('agent-allow-run')).toBeDisabled()
    await expect(page.getByTestId('agent-url')).toHaveText(apiBase())

    const { token } = await info(request)
    await expect(page.getByTestId('agent-token')).not.toContainText(token)
    await page.getByTestId('agent-toggle-token').click()
    await expect(page.getByTestId('agent-token')).toHaveText(token)

    await page.getByTestId('agent-enabled').click()
    await expect(page.getByTestId('agent-enabled')).toHaveAttribute('aria-checked', 'true')
    await expect(page.getByTestId('agent-allow-run')).toBeEnabled()
    await page.getByTestId('agent-allow-run').click()
    await expect(page.getByTestId('agent-allow-run')).toHaveAttribute('aria-checked', 'true')
    await page.reload()
    await expect(page.getByTestId('agent-allow-run')).toHaveAttribute('aria-checked', 'true') // saved by the backend

    await page.getByTestId('agent-copy-all').click()
    const text = await page.evaluate(() => navigator.clipboard.readText())
    expect(text).toContain(apiBase())
    expect(text).toContain(token)
    expect(text).toContain('/api/agent/skill')
    expect(text).toContain('only when the user asks')

    await page.getByTestId('agent-enabled').click() // off again: running goes off with it
    await expect(page.getByTestId('agent-allow-run')).toHaveAttribute('aria-checked', 'false')
    await expect(page.getByTestId('agent-allow-run')).toBeDisabled()
  })

  test('an agent: refused while off, then may build flows but not delete, change its own permissions or run', async ({ request }) => {
    const { token } = await info(request)
    const h = bearer(token)
    const off = await request.get('/api/flows', { headers: h })
    expect(off.status()).toBe(403)
    expect((await off.json()).error).toContain('switched off')
    expect((await request.get('/api/flows', { headers: bearer('wrong') })).status()).toBe(401)
    expect((await request.get('/api/flows')).status(), 'the UI (no header) is never refused').toBe(200)

    await request.put('/api/agent/access', { data: { enabled: true } })
    expect((await request.get('/api/flows', { headers: h })).status()).toBe(200)
    expect((await request.get('/api/cards/types', { headers: h })).status()).toBe(200)
    expect((await request.get('/api/connections', { headers: h })).status()).toBe(200)

    const flow = await (await request.post('/api/flows', { headers: h, data: { name: unique('Agent flow'), description: '' } })).json()
    expect((await request.delete(`/api/flows/${flow.id}`, { headers: h, data: { confirmName: flow.name } })).status(), 'delete is not for agents').toBe(403)
    expect((await request.put('/api/agent/access', { headers: h, data: { allowRun: true } })).status(), 'an agent cannot give itself permissions').toBe(403)
    expect((await request.get('/api/agent/access', { headers: h })).status(), 'nor read the token settings').toBe(403)
    expect((await request.post('/api/connections', { headers: h, data: { name: 'x', provider: 'ollama', baseUrl: 'http://x' } })).status()).toBe(403)

    const run = await request.post(`/api/flows/${flow.id}/runs`, { headers: h })
    expect(run.status()).toBe(403)
    expect((await run.json()).error).toContain('Let the agent run flows')

    await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: flow.name } }) // cleanup as the UI would
  })

  test('an agent builds a flow end to end: validate, create, fill, see it in the app, and run it only once allowed', async ({ page, request }) => {
    await request.put('/api/agent/access', { data: { enabled: true } })
    const { token } = await info(request)
    const h = bearer(token)
    const definition = {
      nodes: [
        { id: 'start', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} },
        { id: 'greet', type: 'data.set', name: 'Greet', position: { x: 280, y: 0 }, params: { values: '{"hello":"world"}' } },
      ],
      edges: [{ id: 'e1', source: 'start', target: 'greet' }],
    }

    const bad = await (await request.post('/api/flows/validate', { headers: h, data: { definition: { nodes: [{ id: 'a', type: 'no.such.card' }], edges: [] } } })).json()
    expect(bad).toEqual({ valid: false, error: "Unknown card type 'no.such.card'." })
    expect(await (await request.post('/api/flows/validate', { headers: h, data: { definition } })).json()).toEqual({ valid: true })

    const name = unique('Built by an agent')
    const created = await (await request.post('/api/flows', { headers: h, data: { name, description: 'made through the agent API' } })).json()
    const saved = await request.put(`/api/flows/${created.id}`, { headers: h, data: { name, description: 'made through the agent API', definition } })
    expect(saved.status()).toBe(200)

    await page.goto('/#/')
    await expect(page.getByText(name)).toBeVisible() // it shows up in the user's list
    await page.goto(`/#/flows/${created.id}`)
    await expect(node(page, 'greet')).toBeVisible() // and opens with its cards

    expect((await request.post(`/api/flows/${created.id}/runs`, { headers: h })).status(), 'not allowed to run yet').toBe(403)
    await request.put('/api/agent/access', { data: { allowRun: true } })
    const started = await request.post(`/api/flows/${created.id}/runs`, { headers: h, data: { input: 'hi' } })
    expect(started.status()).toBe(202)
    const runId = (await started.json()).id
    await expect.poll(async () => (await (await request.get(`/api/runs/${runId}`, { headers: h })).json()).status).not.toBe('running')
    expect((await (await request.get(`/api/runs/${runId}`, { headers: h })).json()).status).toBe('succeeded')

    await request.delete(`/api/flows/${created.id}`, { data: { confirmName: name } })
  })

  test('the skill: an agent reads it with the token; it lists the routes and the live cards, and never the token; the page can copy and show it', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    await request.put('/api/agent/access', { data: { enabled: true } })
    const { token, url } = await info(request)
    const res = await request.get('/api/agent/skill', { headers: bearer(token) })
    expect(res.status()).toBe(200)
    expect(res.headers()['content-type']).toContain('text/markdown')
    const skill = await res.text()
    expect(skill).toContain('name: tammoa-flows')
    expect(skill).toContain(url)
    expect(skill).toContain('| POST | `/api/flows/validate` |')
    expect(skill).toContain('`trigger.manual`')
    expect(skill).toContain('`ai.ollama`')
    expect(skill).toContain('`check_page`') // a parameter option of the AI card: generated from the live catalog
    expect(skill).not.toContain(token)

    await page.goto('/#/agent')
    await page.getByTestId('agent-view-skill').click()
    await expect(page.getByTestId('output-preview')).toContainText('Tammoa flows')
    await page.getByTestId('agent-copy-skill').click()
    expect(await page.evaluate(() => navigator.clipboard.readText())).toContain('name: tammoa-flows')
  })

  test('a new token: asks first, then the old one stops working', async ({ page, request }) => {
    await request.put('/api/agent/access', { data: { enabled: true } })
    const old = (await info(request)).token
    await page.goto('/#/agent')
    await page.getByTestId('agent-new-token').click()
    await page.getByTestId('confirm-cancel').click()
    expect((await info(request)).token).toBe(old) // cancelled: nothing changed

    await page.getByTestId('agent-new-token').click()
    await page.getByTestId('confirm-ok').click()
    await expect.poll(async () => (await info(request)).token).not.toBe(old)
    expect((await request.get('/api/flows', { headers: bearer(old) })).status()).toBe(401)
    expect((await request.get('/api/flows', { headers: bearer((await info(request)).token) })).status()).toBe(200)
  })
})
