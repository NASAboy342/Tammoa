import { expect, test } from '@playwright/test'
import { codeOf, drag, edge, makeFlow, node, nodeCount, open, script, setCode, setv, trigger } from './helpers'

test.describe('building a flow on the canvas', () => {
  test('the palette lists the card types the backend offers', async ({ page, request }) => {
    const flow = await makeFlow(request, [])
    await open(page, flow.id)
    await expect(page.getByTestId('canvas-empty')).toBeVisible()
    await page.getByTestId('palette-toggle').click()
    for (const t of ['trigger.manual', 'script.shell', 'data.set']) await expect(page.getByTestId(`palette-item-${t}`)).toBeVisible()
  })

  test('add cards, set a parameter, connect by dragging, save, and it all survives a reload', async ({ page, request }) => {
    const flow = await makeFlow(request, [])
    await open(page, flow.id)

    await page.getByTestId('palette-toggle').click()
    await page.getByTestId('palette-item-trigger.manual').click()
    await page.getByTestId('palette-toggle').click()
    await page.getByTestId('palette-item-script.shell').click()
    await expect.poll(() => nodeCount(page)).toBe(2)
    await expect(page.getByTestId('canvas-empty')).toBeHidden()

    // the new script card is selected: its inspector shows the backend-described form
    await expect(page.getByTestId('param-input-command')).toBeVisible()
    await setCode(page, 'param-input-command', 'echo built in the ui')
    await page.getByTestId('param-input-timeoutSeconds').fill('12')
    await page.getByTestId('inspector-name').fill('Greeter')

    const cards = page.locator('[data-node-card]')
    const triggerCard = cards.filter({ hasText: 'Manual trigger' })
    const scriptCard = cards.filter({ hasText: 'Greeter' })
    await drag(page, triggerCard.locator('[data-handle="out"]'), scriptCard)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(1)

    await expect(page.getByTestId('run')).toBeDisabled() // unsaved
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()

    await page.reload()
    await expect(page.getByTestId('canvas')).toBeVisible()
    await expect.poll(() => nodeCount(page)).toBe(2)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(1)
    await page.locator('[data-node-card]').filter({ hasText: 'Greeter' }).click()
    await expect(codeOf(page, 'param-input-command')).toContainText('echo built in the ui')
    await expect(page.getByTestId('param-input-timeoutSeconds')).toHaveValue('12')
  })

  test('connection rules: no self links, no loops (with an explanation)', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', 'echo a'), script('b', 'echo b', 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await drag(page, node(page, 'a').locator('[data-handle="out"]'), node(page, 'a'))
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(2)
    await drag(page, node(page, 'b').locator('[data-handle="out"]'), node(page, 'a'))
    await expect(page.getByTestId('canvas-notice')).toContainText('loop')
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(2)
    await drag(page, node(page, 'a').locator('[data-handle="out"]'), node(page, 't'))
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(2) // a trigger has no input
  })

  test('delete a card with the key and a connection with the button', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', 'echo a'), script('b', 'echo b', 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await page.locator('[data-testid="edge-e-a-main-b"] path').last().click({ force: true })
    await page.getByTestId('edge-delete').click()
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(1)
    await node(page, 'a').click()
    await page.getByTestId('canvas').focus()
    await page.keyboard.press('Delete')
    await expect(node(page, 'a')).toHaveCount(0)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(0) // its connection went with it
    await expect(page.getByTestId('edit-save')).toBeEnabled()
  })

  test('dragging a card moves it, zoom buttons and reset change the view', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    const before = (await node(page, 't').boundingBox())!
    const startX = before.x + before.width / 2
    const startY = before.y + before.height / 2
    await page.mouse.move(startX, startY)
    await page.mouse.down()
    await page.mouse.move(startX + 160, startY + 90, { steps: 6 })
    await page.mouse.up()
    const after = (await node(page, 't').boundingBox())!
    expect(after.x - before.x).toBeGreaterThan(100)
    expect(after.y - before.y).toBeGreaterThan(60)
    await expect(page.getByTestId('edit-save')).toBeEnabled() // position change is a change

    const layer = page.getByTestId('canvas-layer')
    await page.getByTestId('zoom-in').click()
    await expect(layer).toHaveAttribute('style', /scale\(1\.2\)/)
    await page.getByTestId('zoom-out').click()
    await page.getByTestId('zoom-out').click()
    await expect(layer).toHaveAttribute('style', /scale\(0\.83/)
    await page.getByTestId('zoom-reset').click()
    await expect(layer).toHaveAttribute('style', /translate\(40px, 40px\) scale\(1\)/)
  })
})

test.describe('running a flow (all execution happens in the backend)', () => {
  test('trigger → set value → script: data and templates flow through, states and output are shown', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), setv('s', '{"msg":"{{input.input}}"}'), script('c', "echo got {{input.msg | sh}}", 560)],
      [edge('t', 's'), edge('s', 'c')],
    )
    await open(page, flow.id)
    await page.getByTestId('run-input').fill("it's alive")
    await page.getByTestId('run').click()

    for (const id of ['t', 's', 'c']) await expect(node(page, id)).toHaveAttribute('data-status', 'succeeded')
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'succeeded')

    await node(page, 'c').click()
    await expect(page.getByTestId('inspector-result').getByTestId('output-preview')).toContainText("got it's alive") // quoted safely through the shell
    await node(page, 's').click()
    await expect(page.getByTestId('inspector-result').getByTestId('output-preview')).toContainText('"msg": "it\'s alive"')
  })

  test('state shows up immediately: finished cards turn green while a later card still runs (a stale start response must not overwrite pushed state)', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), setv('s', '{"a":1}'), script('slow', 'sleep 4', 560)], [edge('t', 's'), edge('s', 'slow')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'slow')).toHaveAttribute('data-status', 'running', { timeout: 1500 })
    await expect(node(page, 't')).toHaveAttribute('data-status', 'succeeded', { timeout: 500 })
    await expect(node(page, 's')).toHaveAttribute('data-status', 'succeeded', { timeout: 500 })
    await page.locator('[data-testid^="run-cancel-"]').click() // do not leave a 4 s process behind
  })

  test('a failing card is marked failed, downstream cards are skipped, the error is shown', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('bad', 'echo nope 1>&2; exit 3'), script('after', 'echo never', 560)], [edge('t', 'bad'), edge('bad', 'after')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'bad')).toHaveAttribute('data-status', 'failed')
    await expect(node(page, 'after')).toHaveAttribute('data-status', 'skipped')
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'failed')
    await expect(page.getByTestId('run-failure')).toContainText('code 3')
    await node(page, 'bad').click()
    await expect(page.getByTestId('inspector-error')).toContainText('nope')
  })

  test('a run can be cancelled while it is running', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('slow', 'sleep 20')], [edge('t', 'slow')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'slow')).toHaveAttribute('data-status', 'running')
    await page.locator('[data-testid^="run-cancel-"]').click()
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'cancelled')
    await expect(node(page, 'slow')).toHaveAttribute('data-status', 'cancelled')
  })

  test('runs are independent: two at once, started from different places, both finish', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('nap', 'sleep 2; echo done')], [edge('t', 'nap')])
    const other = await makeFlow(request, [trigger(), script('nap', 'sleep 2; echo done')], [edge('t', 'nap')])
    await open(page, flow.id)
    const started = Date.now()
    await page.getByTestId('run').click()
    await expect(page.locator('[data-testid^="run-row-"][data-status="running"]')).toHaveCount(1)
    await page.getByTestId('run').click() // a second run of the same flow while the first still runs
    await expect(page.locator('[data-testid^="run-row-"][data-status="running"]')).toHaveCount(2)
    await request.post(`/api/flows/${other.id}/runs`, { data: {} }) // and a third, of another flow, through the API
    await expect(page.locator('[data-testid^="run-row-"][data-status="succeeded"]')).toHaveCount(2, { timeout: 15_000 })
    expect(Date.now() - started).toBeLessThan(8_000) // 3 × 2 s run in parallel, not one after another
  })

  test('the page only displays pushed state: a run started elsewhere appears and finishes live, no reload', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('c', 'echo remote')], [edge('t', 'c')])
    await open(page, flow.id)
    await expect(page.getByTestId('runs-empty')).toBeVisible()
    const res = await request.post(`/api/flows/${flow.id}/runs`, { data: { input: 'x' } })
    expect(res.status()).toBe(202)
    await expect(page.locator('[data-testid^="run-row-"]')).toHaveCount(1)
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'c')).toHaveAttribute('data-status', 'succeeded')
  })

  test('run is disabled with no trigger, or with unsaved changes', async ({ page, request }) => {
    const flow = await makeFlow(request, [script('a', 'echo a', 0)])
    await open(page, flow.id)
    await expect(page.getByTestId('run')).toBeDisabled()
    await expect(page.getByTestId('run')).toHaveAttribute('title', /trigger/i)
    await page.getByTestId('palette-toggle').click()
    await page.getByTestId('palette-item-trigger.manual').click()
    await expect(page.getByTestId('run')).toBeDisabled()
    await expect(page.getByTestId('run')).toHaveAttribute('title', /Save/)
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('run')).toBeEnabled()
  })
})

test.describe('backend contract', () => {
  test('GET /api/cards/types describes the cards and their params', async ({ request }) => {
    const types = await (await request.get('/api/cards/types')).json()
    expect(types.map((t: { type: string }) => t.type).sort()).toEqual([
      'ai.ollama', 'data.json', 'data.merge', 'data.set', 'file.read', 'file.write', 'flow.wait', 'logic.if', 'net.http', 'script.shell', 'trigger.manual',
    ])
    const shell = types.find((t: { type: string }) => t.type === 'script.shell')
    expect(shell.params.map((p: { key: string }) => p.key)).toEqual(['language', 'command', 'interpreter', 'workingDirectory', 'stdin', 'timeoutSeconds', 'failOnError'])
    expect(shell.params[0]).toMatchObject({ kind: 'select', defaultValue: 'shell' })
    expect(shell.params[0].options.map((o: { value: string }) => o.value)).toEqual(['shell', 'javascript', 'python'])
    expect(shell.params[1]).toMatchObject({ kind: 'code', codeLanguageParam: 'language' })
    expect(shell.params[5]).toMatchObject({ kind: 'number', defaultValue: 30 })
    const iff = types.find((t: { type: string }) => t.type === 'logic.if')
    expect(iff.outputs.map((o: { key: string }) => o.key)).toEqual(['true', 'false'])
    expect(types.find((t: { type: string }) => t.type === 'trigger.manual').outputs).toEqual([{ key: 'main', label: '' }])
    const ollama = types.find((t: { type: string }) => t.type === 'ai.ollama')
    expect(ollama.params.find((p: { key: string }) => p.key === 'connection')).toMatchObject({ kind: 'connection', provider: 'ollama' })
    expect(ollama.params.find((p: { key: string }) => p.key === 'model')).toMatchObject({ kind: 'select', optionsSource: 'ollama.models', optionsDependsOn: 'connection' })
    expect(types.find((t: { type: string }) => t.type === 'trigger.manual').isTrigger).toBe(true)
  })

  test('saving a bad graph is a 400 with a reason, and nothing is stored', async ({ request }) => {
    const flow = await makeFlow(request, [trigger()])
    const put = (definition: unknown) => request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition } })
    for (const [definition, reason] of [
      [{ nodes: [{ id: 'x', type: 'ghost' }], edges: [] }, /Unknown card type/],
      [{ nodes: [script('a', 'x'), script('b', 'x')], edges: [edge('a', 'b'), edge('b', 'a')] }, /Loops/],
      [{ nodes: [script('a', 'x')], edges: [edge('a', 'nope')] }, /does not exist/],
      [{ nodes: [trigger(), trigger()], edges: [] }, /Duplicate/],
    ] as const) {
      const res = await put(definition)
      expect(res.status()).toBe(400)
      expect((await res.json()).error).toMatch(reason)
    }
    const stored = await (await request.get(`/api/flows/${flow.id}`)).json()
    expect(stored.definition.nodes).toHaveLength(1)
  })

  test('running a flow without a trigger is a 400; unknown flow/run are 404', async ({ request }) => {
    const flow = await makeFlow(request, [script('a', 'echo a', 0)])
    const noTrigger = await request.post(`/api/flows/${flow.id}/runs`, { data: {} })
    expect(noTrigger.status()).toBe(400)
    expect((await noTrigger.json()).error).toMatch(/trigger/i)
    expect((await request.post('/api/flows/nope/runs', { data: {} })).status()).toBe(404)
    expect((await request.get('/api/runs/nope')).status()).toBe(404)
    expect((await request.post('/api/runs/nope/cancel')).status()).toBe(404)
  })

  test('an exported flow with cards imports back whole; an import with a bad graph is refused', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', 'echo a')], [edge('t', 'a')])
    const doc = await (await request.get(`/api/flows/${flow.id}/export`)).json()
    const imported = await (await request.post('/api/flows/import', { data: doc })).json()
    expect(imported.definition.nodes).toHaveLength(2)
    expect(imported.definition.edges).toHaveLength(1)
    const bad = await request.post('/api/flows/import', { data: { ...doc, definition: { nodes: [{ id: 'q', type: 'ghost' }], edges: [] } } })
    expect(bad.status()).toBe(400)
  })
})
