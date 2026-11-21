import { expect, test, type Locator, type Page } from '@playwright/test'
import { card, drag, edge, makeFlow, node, open, openTab, outputOf, runAndWait, script, trigger } from './helpers'

// card names are limited to 100 characters
const MEDIUM = 'Fetch the weekly finance report'
const LONG = 'Fetch the weekly report from the finance service and summarise it'
const VERY_LONG = 'Fetch the weekly report from the finance service, compare with the last four quarters, summarise'

const sizeOfCard = async (n: Locator) => {
  const b = (await n.boundingBox())!
  return { w: Math.round(b.width), h: Math.round(b.height) }
}
const named = (id: string, name: string, x = 0, y = 0) => card(id, 'script.shell', { command: 'echo' }, x, y, name)

async function dragBy(page: Page, from: Locator, dx: number, dy: number) {
  const b = (await from.boundingBox())!
  const x = b.x + b.width / 2
  const y = b.y + b.height / 2
  await page.mouse.move(x, y)
  await page.mouse.down()
  await page.mouse.move(x + dx, y + dy, { steps: 8 })
  await page.mouse.up()
}

test.describe('a card is as big as its title needs', () => {
  test('a short title keeps the default size; a longer one makes the card wider; a very long one wraps and makes it taller', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('s', 'Add'), named('m', MEDIUM, 0, 120), named('l', VERY_LONG, 0, 260)])
    await open(page, flow.id)
    const s = await sizeOfCard(node(page, 's'))
    const m = await sizeOfCard(node(page, 'm'))
    const l = await sizeOfCard(node(page, 'l'))
    expect(s).toEqual({ w: 208, h: 60 })
    expect(m.w).toBeGreaterThan(s.w + 40)
    expect(m.h).toBe(60)
    expect(l.w).toBe(420) // stops growing wider…
    expect(l.h).toBeGreaterThan(60) // …and grows taller instead
    expect(l.h).toBeGreaterThan(m.h)
  })

  test('the whole title is visible: nothing is cut off with an ellipsis', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('m', LONG), named('l', VERY_LONG, 0, 130)])
    await open(page, flow.id)
    for (const id of ['m', 'l']) {
      const title = node(page, id).getByTestId('node-name')
      const fits = await title.evaluate((e) => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1)
      expect(fits, id).toBe(true)
      await expect(title).toHaveText(id === 'm' ? LONG : VERY_LONG)
    }
  })

  test('renaming a card re-fits it at once', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('c', 'Short')])
    await open(page, flow.id)
    const before = await sizeOfCard(node(page, 'c'))
    await node(page, 'c').click()
    await page.getByTestId('inspector-name').fill(LONG)
    await expect.poll(async () => (await sizeOfCard(node(page, 'c'))).w).toBeGreaterThan(before.w + 40)
    await page.getByTestId('inspector-name').fill('Short')
    await expect.poll(async () => (await sizeOfCard(node(page, 'c'))).w).toBe(before.w)
  })

  test('a connection leaves the real right edge of a card that grew, and enters the real left edge', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), named('wide', LONG, 300), script('end', 'echo', 900)], [edge('t', 'wide'), edge('wide', 'end')])
    await open(page, flow.id)
    const box = (await node(page, 'wide').boundingBox())!
    const out = (await node(page, 'wide').getByTestId('out-wide-main').boundingBox())!
    expect(Math.abs(out.x + out.width / 2 - (box.x + box.width))).toBeLessThan(3)
    const d = (await page.locator('[data-testid="edge-e-wide-main-end"] path').first().getAttribute('d'))!
    const startX = Number(d.split(' ')[1])
    const layer = (await page.getByTestId('canvas-layer').boundingBox())!
    expect(Math.abs(layer.x + startX - (box.x + box.width))).toBeLessThan(3) // the line starts where the handle is
  })

  test('a card with two outputs (IF) keeps room for the labels', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('i', 'logic.if', {}, 260, 0, LONG)], [edge('t', 'i')])
    await open(page, flow.id)
    await expect(node(page, 'i').getByTestId('handle-label')).toHaveText(['true', 'false'])
    const labels = await node(page, 'i').getByTestId('handle-label').first().boundingBox()
    const title = await node(page, 'i').getByTestId('node-name').boundingBox()
    expect(labels!.x).toBeGreaterThanOrEqual(title!.x + title!.width - 2) // the title does not run under the TRUE / FALSE labels
  })
})

test.describe('resize a card by hand', () => {
  test('drag the bottom-right corner: the card follows, the size is saved with the flow and survives a reload', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('c', 'Resize me')])
    await open(page, flow.id)
    await node(page, 'c').click()
    await expect(node(page, 'c')).not.toHaveAttribute('data-manual-size', 'true')
    const before = await sizeOfCard(node(page, 'c'))
    await dragBy(page, page.getByTestId('resize-c'), 140, 50)
    await expect(node(page, 'c')).toHaveAttribute('data-manual-size', 'true')
    const after = await sizeOfCard(node(page, 'c'))
    expect(after.w - before.w).toBeGreaterThanOrEqual(130)
    expect(after.w - before.w).toBeLessThanOrEqual(150)
    expect(after.h - before.h).toBeGreaterThanOrEqual(40)
    await expect(page.getByTestId('edit-save')).toBeEnabled() // a resize is a change

    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    const saved = await (await request.get(`/api/flows/${flow.id}`)).json()
    expect(saved.definition.nodes[0].size).toEqual({ w: after.w, h: after.h })

    await page.reload()
    await expect(page.getByTestId('canvas')).toBeVisible()
    expect(await sizeOfCard(node(page, 'c'))).toEqual(after)
    await expect(node(page, 'c')).toHaveAttribute('data-manual-size', 'true')
  })

  test('the limits hold: not smaller than a readable card, not bigger than the maximum', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('c', 'Limits')])
    await open(page, flow.id)
    await node(page, 'c').click()
    await dragBy(page, page.getByTestId('resize-c'), -400, -300)
    expect(await sizeOfCard(node(page, 'c'))).toEqual({ w: 140, h: 56 })
    await dragBy(page, page.getByTestId('resize-c'), 1500, 1200)
    const big = await sizeOfCard(node(page, 'c'))
    expect(big.w).toBeLessThanOrEqual(900)
    expect(big.h).toBeLessThanOrEqual(700)
    expect(big.w).toBeGreaterThan(500)
  })

  test('zoomed in, the card still follows the pointer (the drag is in canvas units)', async ({ page, request }) => {
    const flow = await makeFlow(request, [named('c', 'Zoomed', 400, 150)]) // in the middle, so it stays on screen when zoomed
    await open(page, flow.id)
    await page.getByTestId('zoom-in').click()
    await page.getByTestId('zoom-in').click() // 1.44x
    await node(page, 'c').click()
    const before = (await node(page, 'c').boundingBox())!
    await dragBy(page, page.getByTestId('resize-c'), 144, 0)
    const after = (await node(page, 'c').boundingBox())!
    expect(Math.abs(after.width - before.width - 144)).toBeLessThan(6) // it grew by exactly what the pointer moved on screen
  })

  test('double-clicking the corner lets the card fit its title again, and the saved size is removed', async ({ page, request }) => {
    const flow = await makeFlow(request, [{ ...named('c', LONG), size: { w: 300, h: 120 } }])
    await open(page, flow.id)
    await expect(node(page, 'c')).toHaveAttribute('data-manual-size', 'true')
    expect(await sizeOfCard(node(page, 'c'))).toEqual({ w: 300, h: 120 })
    await node(page, 'c').click()
    await page.getByTestId('resize-c').dblclick()
    await expect(node(page, 'c')).not.toHaveAttribute('data-manual-size', 'true')
    expect((await sizeOfCard(node(page, 'c'))).w).toBeGreaterThan(300) // back to what the title needs
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    expect((await (await request.get(`/api/flows/${flow.id}`)).json()).definition.nodes[0].size).toBeUndefined()
  })

  test('shrunk below its title, the card cuts the title with an ellipsis but the tooltip keeps all of it', async ({ page, request }) => {
    const flow = await makeFlow(request, [{ ...named('c', VERY_LONG), size: { w: 160, h: 60 } }])
    await open(page, flow.id)
    const title = node(page, 'c').getByTestId('node-name')
    expect(await title.evaluate((e) => e.scrollHeight > e.clientHeight)).toBe(true) // really cut
    await expect(node(page, 'c')).toHaveAttribute('title', new RegExp(`^${VERY_LONG.slice(0, 30)}`))
    await expect(title).toHaveText(VERY_LONG) // the full text is in the page, only clipped by the card
  })

  test('a taller card spreads its output handles, and the connections follow them', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), { ...card('i', 'logic.if', {}, 260, 0, 'Decide'), size: { w: 220, h: 150 } }, script('yes', 'echo', 600)], [edge('t', 'i'), edge('i', 'yes', 'true')])
    await open(page, flow.id)
    const box = (await node(page, 'i').boundingBox())!
    const t = (await node(page, 'i').getByTestId('out-i-true').boundingBox())!
    const f = (await node(page, 'i').getByTestId('out-i-false').boundingBox())!
    expect(Math.abs(t.y + t.height / 2 - (box.y + box.height / 3))).toBeLessThan(3)
    expect(Math.abs(f.y + f.height / 2 - (box.y + (box.height * 2) / 3))).toBeLessThan(3)
    const d = (await page.locator('[data-testid="edge-e-i-true-yes"] path').first().getAttribute('d'))!
    const startY = Number(d.split(' ')[2])
    const layer = (await page.getByTestId('canvas-layer').boundingBox())!
    expect(Math.abs(layer.y + startY - (t.y + t.height / 2))).toBeLessThan(3) // the line starts at the TRUE handle
  })

  test('the size is checked by the backend when the flow is saved', async ({ request }) => {
    const flow = await makeFlow(request, [trigger()])
    const put = (size: unknown) => request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes: [{ ...trigger(), size }], edges: [] } } })
    for (const bad of [{ w: 5, h: 5 }, { w: 99999, h: 60 }, { w: 'wide', h: 60 }, { w: 200 }, 7]) {
      const res = await put(bad)
      expect(res.status(), JSON.stringify(bad)).toBe(400)
      expect((await res.json()).error).toMatch(/size/)
    }
    expect((await put({ w: 320, h: 90 })).status()).toBe(200)
    expect((await put(null)).status()).toBe(200)
  })
})

// ---------------------------------------------------------------------------------------------------------------------------
const COUNT = "const n = (input.json?.n ?? 0) + 1\nconsole.log(JSON.stringify({ n }))"
/** trigger -> "Add one" -> "Reached 5?" (IF: n < 5 → true goes BACK to "Add one", false → "Report") */
const counterLoop = (limit = 5) => ({
  nodes: [
    trigger(),
    card('inc', 'script.shell', { language: 'javascript', command: COUNT }, 260, 0, 'Add one'),
    card('check', 'logic.if', { value1: '{{input.json.n}}', operator: 'lessThan', value2: String(limit) }, 520, 0, 'Reached limit?'),
    card('done', 'script.shell', { command: 'echo finished with {{input.json.n}}' }, 800, 60, 'Report'),
  ],
  edges: [edge('t', 'inc'), edge('inc', 'check'), edge('check', 'inc', 'true'), edge('check', 'done', 'false')],
})

test.describe('a connection back to an earlier card makes a loop', () => {
  test('the loop repeats until the IF sends the flow elsewhere; cards after it run once; the canvas shows ×N', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(5)
    const flow = await makeFlow(request, nodes, edges)
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'inc').getByTestId('node-rounds')).toHaveText('×5')
    await expect(node(page, 'check').getByTestId('node-rounds')).toHaveText('×5')
    await expect(node(page, 'done').getByTestId('node-rounds')).toHaveCount(0) // ran once
    await expect(node(page, 'done')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'done').click()
    await openTab(page, 'output')
    await expect(page.locator('[data-path="input.stdout"]').getByTestId('field-preview')).toHaveText('finished with 5\\n')
  })

  test('look at each round: Round 5 of 5, step back to earlier rounds with their own input and output, and forward again', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(5)
    const flow = await makeFlow(request, nodes, edges)
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'inc').getByTestId('node-rounds')).toHaveText('×5')
    await node(page, 'inc').click()
    await openTab(page, 'output')
    await expect(page.getByTestId('round-label')).toContainText('Round 5 of 5')
    await expect(page.locator('[data-path="input.json.n"]').getByTestId('field-preview')).toHaveText('5')
    await page.getByTestId('round-prev').click()
    await expect(page.getByTestId('round-label')).toContainText('Round 4 of 5')
    await expect(page.locator('[data-path="input.json.n"]').getByTestId('field-preview')).toHaveText('4')
    await page.getByTestId('round-prev').click()
    await page.getByTestId('round-prev').click()
    await page.getByTestId('round-prev').click()
    await expect(page.getByTestId('round-label')).toContainText('Round 1 of 5')
    await expect(page.getByTestId('round-prev')).toBeDisabled()
    await expect(page.locator('[data-path="input.json.n"]').getByTestId('field-preview')).toHaveText('1')

    await openTab(page, 'input') // the Input tab shows the input OF THE SAME ROUND
    await expect(page.getByTestId('round-label')).toContainText('Round 1 of 5')
    await expect(page.locator('[data-path="input.input"]')).toBeVisible() // round 1 came from the trigger: { input: … }
    await page.getByTestId('round-next').click()
    await expect(page.locator('[data-path="input.json.n"]').getByTestId('field-preview')).toHaveText('1') // round 2 got round 1's output
    for (let i = 0; i < 3; i++) await page.getByTestId('round-next').click()
    await expect(page.getByTestId('round-label')).toContainText('Round 5 of 5')
    await expect(page.getByTestId('round-next')).toBeDisabled()
  })

  test('a card that ran once has no round picker', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(3)
    const flow = await makeFlow(request, nodes, edges)
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'done')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'done').click()
    await openTab(page, 'output')
    await expect(page.getByTestId('round-picker')).toHaveCount(0)
  })

  test('draw the loop-back yourself: drag from the IF back onto the earlier card; it is dashed, under the cards, labelled, and saved', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(3)
    const flow = await makeFlow(request, nodes, edges.filter((e) => e.id !== 'e-check-true-inc'))
    await open(page, flow.id)
    await expect(page.locator('[data-loop="true"]')).toHaveCount(0)
    await drag(page, node(page, 'check').getByTestId('out-check-true'), node(page, 'inc'))
    await expect(page.getByTestId('canvas-notice')).toContainText('Loop created')
    await expect(page.getByTestId('canvas-notice')).toContainText('until the IF sends the flow the other way')
    const loop = page.locator('[data-loop="true"]')
    await expect(loop).toHaveCount(1)
    await expect(loop.getByTestId('loop-label')).toHaveText('loop')
    expect(await loop.locator('path').first().getAttribute('stroke-dasharray')).toBeTruthy()
    await expect(page.locator('[data-testid^="edge-e-"]:not([data-loop])')).toHaveCount(3) // the forward lines are plain

    // the dashed line runs below the cards
    const d = (await loop.locator('path').first().getAttribute('d'))!
    const lowest = Math.max(...[...d.matchAll(/ (\d+(?:\.\d+)?)(?:,| C|$)/g)].map((m) => Number(m[1])))
    const incBox = (await node(page, 'inc').boundingBox())!
    const layer = (await page.getByTestId('canvas-layer').boundingBox())!
    expect(layer.y + lowest).toBeGreaterThan(incBox.y + incBox.height + 20)

    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    const saved = await (await request.get(`/api/flows/${flow.id}`)).json()
    expect(saved.definition.edges.map((e: any) => `${e.source}.${e.sourceHandle}->${e.target}`)).toContain('check.true->inc')
    await page.getByTestId('run').click()
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'inc').getByTestId('node-rounds')).toHaveText('×3')
  })

  test('a loop with nothing to end it says so, shows the loop limit, and the engine stops it there with a clear message', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', 'echo a', 260), script('b', 'echo b', 520)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await expect(page.getByTestId('loop-limit')).toHaveCount(0) // no loop: no limit field
    await drag(page, node(page, 'b').locator('[data-handle="out"]'), node(page, 'a'))
    await expect(page.getByTestId('canvas-notice')).toContainText('nothing in it decides when to stop')
    await expect(page.getByTestId('loop-hint')).toContainText('no IF')
    await expect(page.getByTestId('loop-limit')).toBeVisible()
    await page.getByTestId('loop-limit').fill('3')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    expect((await (await request.get(`/api/flows/${flow.id}`)).json()).definition.settings).toEqual({ loopLimit: 3 })

    await page.getByTestId('run').click()
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'failed')
    await expect(page.getByTestId('run-failure')).toContainText('Loop limit reached')
    await expect(page.getByTestId('run-failure')).toContainText('3 times')
    await expect(node(page, 'a').getByTestId('node-rounds')).toHaveText('×3')
  })

  test('the loop limit is remembered after a reload, clearing it removes the setting, and removing the loop-back hides the field', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(3)
    const flow = await makeFlow(request, nodes, edges)
    await open(page, flow.id)
    await expect(page.getByTestId('loop-limit')).toHaveValue('')
    await expect(page.getByTestId('loop-hint')).toHaveCount(0) // this loop has an IF
    await page.getByTestId('loop-limit').fill('12')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.reload()
    await expect(page.getByTestId('loop-limit')).toHaveValue('12')

    await page.getByTestId('loop-limit').fill('')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    expect((await (await request.get(`/api/flows/${flow.id}`)).json()).definition.settings).toBeUndefined()

    await page.locator('[data-loop="true"] path[stroke="transparent"]').dispatchEvent('pointerdown')
    await page.getByTestId('edge-delete').click()
    await expect(page.getByTestId('loop-limit')).toHaveCount(0)
  })

  test('the loop limit is kept inside 1 to 1000', async ({ page, request }) => {
    const { nodes, edges } = counterLoop(3)
    const flow = await makeFlow(request, nodes, edges)
    await open(page, flow.id)
    await page.getByTestId('loop-limit').fill('5000')
    await expect(page.getByTestId('loop-limit')).toHaveValue('1000')
    await page.getByTestId('loop-limit').fill('0')
    await expect(page.getByTestId('loop-limit')).toHaveValue('1')
  })

  test('cancelling in the middle of a loop ends the run at once', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), card('w', 'flow.wait', { seconds: 0.4 }, 260, 0, 'Pause'), card('c', 'logic.if', { value1: 'x', operator: 'equals', value2: 'x' }, 520, 0, 'Always again')],
      [edge('t', 'w'), edge('w', 'c'), edge('c', 'w', 'true')],
    )
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'w').getByTestId('node-rounds')).toBeVisible({ timeout: 10_000 }) // already repeating
    await page.locator('[data-testid^="run-cancel-"]').click()
    await expect(page.locator('[data-testid^="run-row-"]').first()).toHaveAttribute('data-status', 'cancelled')
  })

  test('a Data card works as the memory of a loop: each round re-reads what the previous round stored', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        card('step', 'script.shell', { language: 'javascript', command: 'console.log(JSON.stringify({ n: (data.Counter?.n ?? 0) + 1 }))' }, 260, 0, 'Step'),
        card('mem', 'data.store', { mode: 'replace', writeFrom: '{{input.stdout}}' }, 520, 0, 'Counter'),
        card('more', 'logic.if', { value1: '{{input.json.n}}', operator: 'lessThan', value2: '4' }, 780, 0, 'More?'),
        card('end', 'script.shell', { command: 'echo stored {{data.Counter.n}}' }, 1040, 60, 'End'),
      ],
      [edge('t', 'step'), edge('step', 'mem'), edge('mem', 'more'), edge('more', 'step', 'true'), edge('more', 'end', 'false')],
    )
    await request.put(`/api/flows/${flow.id}/data/mem`, { data: { text: '{"n":0}' } })
    const run = await runAndWait(request, flow.id)
    expect(run.status, run.error).toBe('succeeded')
    expect(run.nodes.find((n: any) => n.nodeId === 'step')).toMatchObject({ iteration: 4 })
    expect(outputOf(run, 'end').stdout).toBe('stored 4\n')
    expect((await (await request.get(`/api/flows/${flow.id}/data/mem`)).json()).text).toBe('{"n":4}\n')
  })

  test('the API: iteration and earlier rounds are in the run, loops are accepted, a self-connection is not', async ({ request }) => {
    const { nodes, edges } = counterLoop(4)
    const flow = await makeFlow(request, nodes, edges)
    const run = await runAndWait(request, flow.id)
    const inc = run.nodes.find((n: any) => n.nodeId === 'inc') as any
    expect(inc.iteration).toBe(4)
    expect(inc.previous.map((p: any) => p.iteration)).toEqual([1, 2, 3])
    expect(inc.previous[2]).toMatchObject({ status: 'succeeded', input: { json: { n: 2 } }, output: { json: { n: 3 } } })
    expect((run.nodes.find((n: any) => n.nodeId === 'done') as any).previous).toBeUndefined()

    const self = await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes: [trigger(), script('a', 'echo')], edges: [edge('t', 'a'), edge('a', 'a')] } } })
    expect(self.status()).toBe(400)
    const badLimit = await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes: [], edges: [], settings: { loopLimit: 0 } } } })
    expect(badLimit.status()).toBe(400)
    expect((await badLimit.json()).error).toMatch(/loop limit/)
  })

  test('the guide for IF explains loops, and the data lesson lists them', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('i', 'logic.if', {}, 260)], [edge('t', 'i')])
    await open(page, flow.id)
    await node(page, 'i').click()
    await openTab(page, 'guide')
    await expect(page.getByTestId('guide-tips')).toContainText('To make a LOOP')
    await expect(page.getByTestId('guide-tips')).toContainText('Loop limit')
  })
})
