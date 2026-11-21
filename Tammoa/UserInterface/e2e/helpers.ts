import { expect, type APIRequestContext, type Locator, type Page } from '@playwright/test'

export const unique = (label: string) => `${label} ${Date.now()}-${Math.floor(Math.random() * 1e4)}`

export type N = { id: string; type: string; name: string; position: { x: number; y: number }; params: Record<string, unknown> }
export type E = { id: string; source: string; sourceHandle?: string; target: string }

export const trigger = (id = 't', x = 0): N => ({ id, type: 'trigger.manual', name: 'Manual trigger', position: { x, y: 0 }, params: {} })
export const script = (id: string, command: string, x = 280, extra: Record<string, unknown> = {}): N => ({ id, type: 'script.shell', name: `Script ${id}`, position: { x, y: 0 }, params: { command, ...extra } })
export const setv = (id: string, values: string, x = 280): N => ({ id, type: 'data.set', name: `Set ${id}`, position: { x, y: 0 }, params: { values } })
export const card = (id: string, type: string, params: Record<string, unknown> = {}, x = 280, y = 0, name = id): N => ({ id, type, name, position: { x, y }, params })
export const edge = (source: string, target: string, sourceHandle = 'main'): E => ({ id: `e-${source}-${sourceHandle}-${target}`, source, sourceHandle, target })

/** Creates a flow through the API with the given cards (seeding only; the behaviour under test is driven through the UI). */
export async function makeFlow(request: APIRequestContext, nodes: N[], edges: E[] = []) {
  const flow = await (await request.post('/api/flows', { data: { name: unique('Canvas'), description: '' } })).json()
  const res = await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { schemaVersion: 1, nodes, edges } } })
  expect(res.status(), await res.text()).toBe(200)
  return flow as { id: string; name: string }
}

export async function open(page: Page, id: string) {
  await page.goto(`/#/flows/${id}`)
  await expect(page.getByTestId('canvas')).toBeVisible()
}

export async function drag(page: Page, from: Locator, to: Locator) {
  const a = (await from.boundingBox())!
  const b = (await to.boundingBox())!
  await page.mouse.move(a.x + a.width / 2, a.y + a.height / 2)
  await page.mouse.down()
  await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2, { steps: 8 })
  await page.mouse.up()
}

export const node = (page: Page, id: string) => page.getByTestId(`node-${id}`)
export const nodeCount = (page: Page) => page.locator('[data-node-card]').count()

export type InspectorTab = 'settings' | 'input' | 'output' | 'guide'
export const openTab = (page: Page, tab: InspectorTab) => page.getByTestId(`inspector-tab-${tab}`).click()

/** Opens the Input or Output tab and switches it to the raw JSON view (the field list is the default view). */
export async function showJson(page: Page, tab: 'input' | 'output') {
  await openTab(page, tab)
  await page.getByTestId(`${tab}-data`).getByTestId('view-json').click()
}

/** Replaces the whole content of a code field (CodeMirror) with `text`. */
export async function setCode(page: Page, testid: string, text: string) {
  const content = page.getByTestId(testid).locator('.cm-content').first()
  await content.click()
  await page.keyboard.press('ControlOrMeta+a')
  await page.keyboard.insertText(text)
}

export const codeOf = (page: Page, testid: string) => page.getByTestId(testid).locator('.cm-content').first()

/** Runs a flow through the API and waits (polling) until it is no longer running. */
export async function runAndWait(request: APIRequestContext, flowId: string, input?: unknown) {
  const started = await request.post(`/api/flows/${flowId}/runs`, { data: { input } })
  expect(started.status()).toBe(202)
  const { id } = await started.json()
  let run: { status: string; error?: string; nodes: { nodeId: string; status: string; output?: any; error?: string }[] } | undefined
  await expect
    .poll(async () => {
      run = await (await request.get(`/api/runs/${id}`)).json()
      return run!.status
    }, { timeout: 30_000 })
    .not.toBe('running')
  return run!
}

export const outputOf = (run: { nodes: { nodeId: string; output?: any }[] }, nodeId: string) => run.nodes.find((n) => n.nodeId === nodeId)?.output
