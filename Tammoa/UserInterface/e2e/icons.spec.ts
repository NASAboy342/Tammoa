import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { drag, edge, makeFlow, node, openTab, runAndWait, script, setCode, showJson, trigger } from './helpers'
import { startFakeOllama } from './fake-ollama'

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

test('code editor, output viewer, run log, IF and AI cards: every clickable has an icon', async ({ page, request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: `Audit ${Date.now()}`, description: '' } })).json()
  const long = 'Long description of what this flow does. '.repeat(10)
  const nodes = [
    { id: 't', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} },
    { id: 'i', type: 'logic.if', name: 'Decide', position: { x: 260, y: 0 }, params: { value1: 'a', operator: 'equals', value2: 'a' } },
    { id: 'c', type: 'script.shell', name: 'Script', position: { x: 560, y: 0 }, params: { language: 'javascript', command: 'console.log(1)' } },
    { id: 'a', type: 'ai.ollama', name: 'Ask', position: { x: 560, y: 120 }, params: {} },
  ]
  const edges = [{ id: 'e1', source: 't', target: 'i' }, { id: 'e2', source: 'i', sourceHandle: 'true', target: 'c' }]
  await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: long.slice(0, 200), definition: { nodes, edges } } })
  await page.goto(`/#/flows/${flow.id}`)
  await expect(page.getByTestId('node-c')).toBeVisible()

  await page.getByTestId('palette-toggle').click()
  await expect(page.getByTestId('palette-group-ai')).toBeVisible()
  expect(await withoutIcon(page), 'palette (groups, search)').toEqual([])
  await page.keyboard.press('Escape')

  await page.getByTestId('node-i').click()
  await expect(page.getByTestId('param-input-operator')).toBeVisible()
  expect(await withoutIcon(page), 'IF card selected (select)').toEqual([])

  await page.getByTestId('node-c').click()
  await expect(page.getByTestId('code-expand')).toBeVisible()
  await expect(page.getByTestId('expand-text')).toBeVisible()
  expect(await withoutIcon(page), 'script card selected (code editor, Show more)').toEqual([])
  await page.getByTestId('code-expand').click()
  await expect(page.getByTestId('code-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'large code editor dialog').toEqual([])
  await page.getByTestId('code-collapse').click()

  await page.getByTestId('node-a').click()
  await expect(page.getByTestId('manage-connections')).toBeVisible()
  expect(await withoutIcon(page), 'AI card selected (connection + model selects, reload, link)').toEqual([])

  await page.getByTestId('run').click() // the flow is already saved, so Run is available
  await expect(page.locator('[data-testid^="run-row-"][data-status="succeeded"]')).toHaveCount(1) // IF (a = a) → true → the JavaScript card
  await page.getByTestId('node-c').click()
  await showJson(page, 'output')
  await expect(page.getByTestId('output-copy')).toBeVisible()
  expect(await withoutIcon(page), 'finished run: delete, clear, output copy / full').toEqual([])
  await page.getByTestId('output-expand').click()
  await expect(page.getByTestId('output-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'output in full dialog').toEqual([])
})

test('input / output tabs, the guide with examples, the code hint: every clickable has an icon', async ({ page, request }) => {
  const flow = await (await request.post('/api/flows', { data: { name: `Tabs ${Date.now()}`, description: '' } })).json()
  const many = Object.fromEntries(Array.from({ length: 12 }, (_, i) => [`f${i}`, i]))
  const nodes = [
    { id: 't', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} },
    { id: 'a', type: 'script.shell', name: 'Make data', position: { x: 260, y: 0 }, params: { command: `echo '${JSON.stringify({ name: 'Sok', ...many })}'` } },
    { id: 'b', type: 'script.shell', name: 'Use data', position: { x: 520, y: 0 }, params: { language: 'javascript', command: '' } },
  ]
  await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes, edges: [{ id: 'e1', source: 't', target: 'a' }, { id: 'e2', source: 'a', target: 'b' }] } } })
  await page.goto(`/#/flows/${flow.id}`)
  await expect(page.getByTestId('node-b')).toBeVisible()
  await page.getByTestId('run-input').fill('{"city":"Phnom Penh"}')
  await page.getByTestId('run').click()
  await expect(page.getByTestId('node-a')).toHaveAttribute('data-status', 'succeeded')

  await page.getByTestId('node-b').click()
  await expect(page.getByTestId('code-hint')).toBeVisible()
  expect(await withoutIcon(page), 'Settings tab with the language hint and its Examples button').toEqual([])

  await openTab(page, 'input')
  await expect(page.getByTestId('fields-filter').first()).toBeVisible() // many fields: the filter shows
  await expect(page.getByTestId('field-copy').first()).toBeVisible()
  expect(await withoutIcon(page), 'Input tab: fields with copy buttons, Fields / JSON, Run input').toEqual([])
  await page.getByTestId('input-data').getByTestId('view-json').click()
  expect(await withoutIcon(page), 'Input tab in JSON view (output viewer buttons)').toEqual([])

  await openTab(page, 'output')
  expect(await withoutIcon(page), 'Output tab').toEqual([])

  await openTab(page, 'guide')
  await expect(page.getByTestId('guide-apply').first()).toBeVisible()
  await page.getByTestId('guide-basics-toggle').click()
  await expect(page.getByTestId('guide-basics')).toBeVisible()
  expect(await withoutIcon(page), 'Guide tab: example (Use this example, Copy), data basics').toEqual([])
  await page.getByTestId('guide-example-toggle').nth(1).click()
  expect(await withoutIcon(page), 'Guide tab with two examples open').toEqual([])
  for (const id of ['inspector-tab-settings', 'inspector-tab-input', 'inspector-tab-output', 'inspector-tab-guide']) {
    expect(await page.getByTestId(id).locator('svg[data-icon]').count(), id).toBeGreaterThanOrEqual(1) // tabs carry icons too
  }
})

test('live AI output and the Data card (stored text, data list, change notice): every clickable has an icon', async ({ page, request }) => {
  const fake = await startFakeOllama({ hold: true })
  try {
    const conn = await (await request.post('/api/connections', { data: { name: `Live ${Date.now()}`, provider: 'ollama', baseUrl: fake.url } })).json()
    const flow = await (await request.post('/api/flows', { data: { name: `LiveData ${Date.now()}`, description: '' } })).json()
    const nodes = [
      { id: 't', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} },
      { id: 'ai', type: 'ai.ollama', name: 'Ask', position: { x: 260, y: 0 }, params: { connection: conn.id, model: 'tiny:1b', prompt: 'hi', think: 'on' } },
      { id: 'style', type: 'data.store', name: 'Style', position: { x: 0, y: 140 }, params: {} },
      { id: 'cfg', type: 'data.store', name: 'Settings', position: { x: 260, y: 140 }, params: {} },
    ]
    await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes, edges: [{ id: 'e1', source: 't', target: 'ai' }] } } })
    await request.put(`/api/flows/${flow.id}/data/cfg`, { data: { text: '{"city":"Phnom Penh"}' } })
    await page.goto(`/#/flows/${flow.id}`)
    await expect(page.getByTestId('node-ai')).toBeVisible()

    await page.getByTestId('node-ai').click()
    await page.getByTestId('run').click()
    await expect(page.getByTestId('live-text')).toContainText('fake answer')
    expect(await withoutIcon(page), 'the live view while the model writes (thinking toggle)').toEqual([])
    fake.release()
    await expect(page.getByTestId('node-ai')).toHaveAttribute('data-status', 'succeeded')

    await page.getByTestId('node-style').click()
    await expect(page.getByTestId('stored-editor')).toBeVisible()
    expect(await withoutIcon(page), 'Data card: stored text editor, Save text, Revert, copy reference, copy path').toEqual([])
    await setCode(page, 'stored-editor', 'unsaved words')
    await request.put(`/api/flows/${flow.id}/data/style`, { data: { text: 'changed elsewhere' } })
    await expect(page.getByTestId('stored-changed-elsewhere')).toBeVisible()
    expect(await withoutIcon(page), 'Data card: "changed elsewhere" notice with its Load button').toEqual([])

    await openTab(page, 'input')
    await expect(page.getByTestId('data-cards')).toBeVisible()
    await page.getByTestId('data-card-cfg').getByTestId('data-card-fields').click()
    await expect(page.getByTestId('data-card-explorer')).toBeVisible()
    expect(await withoutIcon(page), 'Data in this flow, with a JSON card opened').toEqual([])
  } finally {
    await fake.close()
  }
})

test('a loop: loop-back line, the loop limit field, the round picker and the resize handle all pass the icon audit', async ({ page, request }) => {
  const flow = await makeFlow(request, [trigger(), script('a', 'echo a', 260), script('b', 'echo b', 520)], [edge('t', 'a'), edge('a', 'b')])
  await page.goto(`/#/flows/${flow.id}`)
  await expect(node(page, 'b')).toBeVisible()
  await drag(page, node(page, 'b').locator('[data-handle="out"]'), node(page, 'a'))
  await expect(page.getByTestId('loop-limit')).toBeVisible()
  expect(await withoutIcon(page), 'canvas with a loop-back, the loop limit field and hint, resize handles').toEqual([])
  expect(await page.getByTestId('loop-limit').locator('xpath=..').locator('svg[data-icon]').count(), 'loop-limit leading icon').toBeGreaterThanOrEqual(1)
  await page.getByTestId('loop-limit').fill('3')
  await page.getByTestId('edit-save').click()
  await expect(page.getByTestId('edit-saved')).toBeVisible()
  await page.getByTestId('run').click()
  await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'failed')
  await node(page, 'a').click()
  await openTab(page, 'output')
  await expect(page.getByTestId('round-picker')).toBeVisible()
  expect(await withoutIcon(page), 'round picker (earlier / later round)').toEqual([])
  void runAndWait
})

test('several cards selected, a group selected (name field, ungroup) and the delete warning: every clickable has an icon', async ({ page, request }) => {
  const flow = await makeFlow(request, [trigger(), script('a', 'echo a', 260), script('b', 'echo b', 520)], [edge('t', 'a'), edge('a', 'b')], { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
  await page.goto(`/#/flows/${flow.id}`)
  await expect(node(page, 'a')).toBeVisible()
  await node(page, 't').click()
  await node(page, 'a').click({ modifiers: ['Shift'] })
  await expect(page.getByTestId('selection-bar')).toBeVisible()
  expect(await withoutIcon(page), 'selection bar for several cards (group, copy, delete) and the group frame').toEqual([])
  await page.getByTestId('group-head-g1').click()
  await expect(page.getByTestId('group-name')).toBeVisible()
  expect(await withoutIcon(page), 'selection bar for a group (ungroup, copy, delete)').toEqual([])
  expect(await page.getByTestId('group-name').locator('xpath=..').locator('svg[data-icon]').count(), 'group name leading icon').toBeGreaterThanOrEqual(1)
  await page.getByTestId('sel-delete').click()
  await expect(page.getByTestId('confirm-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'delete warning dialog').toEqual([])
})

test('connections page and its dialogs: every clickable has an icon, every field a leading icon', async ({ page, request }) => {
  const c = await (await request.post('/api/connections', { data: { name: `Audit ${Date.now()}`, provider: 'ollama', baseUrl: 'http://127.0.0.1:1', apiKey: 'k' } })).json()
  await page.goto('/#/connections')
  await expect(page.getByTestId(`connection-row-${c.id}`)).toBeVisible()
  await page.getByTestId(`connection-test-${c.id}`).click()
  await expect(page.getByTestId(`connection-result-${c.id}`)).toBeVisible()
  expect(await withoutIcon(page), 'connections list with a failed test').toEqual([])

  await page.getByTestId('new-connection').click()
  await expect(page.getByTestId('connection-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'new connection dialog').toEqual([])
  for (const id of ['conn-name', 'conn-provider', 'conn-url', 'conn-key']) {
    expect(await page.getByTestId(id).locator('xpath=..').locator('svg[data-icon]').count(), id).toBeGreaterThanOrEqual(1)
  }
  await page.getByTestId('conn-cancel').click()

  await page.getByTestId(`connection-edit-${c.id}`).click()
  await expect(page.getByTestId('conn-clear-key')).toBeVisible()
  expect(await withoutIcon(page), 'edit connection dialog (remove key switch)').toEqual([])
  await page.getByTestId('conn-cancel').click()

  await page.getByTestId(`connection-delete-${c.id}`).click()
  await expect(page.getByTestId('confirm-dialog')).toBeVisible()
  expect(await withoutIcon(page), 'confirm dialog').toEqual([])
  await page.getByTestId('confirm-cancel').click()
  await request.delete(`/api/connections/${c.id}`)
})

test('the audit really looks at the controls (not an empty selection)', async ({ page }) => {
  await page.goto(ROUTES[0])
  await page.getByTestId('flows').waitFor()
  const visible = await page.locator(CLICKABLE).evaluateAll((els) => els.filter((e) => e.getBoundingClientRect().width > 0).length)
  expect(visible).toBeGreaterThanOrEqual(2) // brand, theme toggle, new flow (+ rows)
})

test('AI tool settings (ticked tools, warning) and the tool call list (live and in the output): every clickable has an icon', async ({ page, request }) => {
  const fake = await startFakeOllama({ hold: true, toolScript: [[{ name: 'read_file', arguments: { path: 'a.txt' } }, { name: 'write_file', arguments: { path: '../x.txt', content: 'x' } }]] })
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-e2e-icons-'))
  try {
    const conn = await (await request.post('/api/connections', { data: { name: `Fake ${Date.now()}`, provider: 'ollama', baseUrl: fake.url } })).json()
    const flow = await (await request.post('/api/flows', { data: { name: `Tools ${Date.now()}`, description: '' } })).json()
    const nodes = [
      { id: 't', type: 'trigger.manual', name: 'Start', position: { x: 0, y: 0 }, params: {} },
      { id: 'a', type: 'ai.ollama', name: 'Ask', position: { x: 280, y: 0 }, params: { connection: conn.id, model: 'tiny:1b', prompt: 'go', think: 'off', tools: ['read_file', 'write_file', 'http_request', 'run_script'], toolRoot: root, allowedHosts: 'a.test' } },
    ]
    await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes, edges: [{ id: 'e', source: 't', target: 'a' }] } } })
    await page.goto(`/#/flows/${flow.id}`)
    await page.getByTestId('node-a').click()
    await expect(page.getByTestId('param-warning-run_script')).toBeVisible()
    expect(await withoutIcon(page), 'tool settings with every tool ticked').toEqual([])

    await page.getByTestId('run').click()
    await expect(page.getByTestId('live-phase')).toBeVisible()
    await expect(page.getByTestId('live-tool-calls')).toBeVisible()
    expect(await withoutIcon(page), 'live phase and tool calls').toEqual([])
    fake.release()
    await expect(page.getByTestId('node-a')).toHaveAttribute('data-status', 'succeeded')
    await page.getByTestId('inspector-tab-output').click()
    await page.getByTestId('tool-call-toggle-0').click()
    await page.getByTestId('tool-call-toggle-1').click()
    await expect(page.getByTestId('tool-call-detail-1')).toBeVisible()
    expect(await withoutIcon(page), 'tool calls in the output, opened').toEqual([])
  } finally {
    await fake.close()
    fs.rmSync(root, { recursive: true, force: true })
  }
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
