import { expect, test } from '@playwright/test'
import { card, codeOf, drag, edge, makeFlow, node, open, runAndWait, script, setCode, setv, showJson, trigger } from './helpers'

const sideWidth = async (page: import('@playwright/test').Page) => Math.round((await page.getByTestId('side-panel').boundingBox())!.width)

test.describe('resizable side panel', () => {
  test('drag to resize, remembered after reload, double-click resets, limits hold', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    expect(await sideWidth(page)).toBe(340)

    const handle = (await page.getByTestId('side-resizer').boundingBox())!
    const x = handle.x + handle.width / 2
    const y = handle.y + handle.height / 2
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x - 150, y, { steps: 8 })
    await page.mouse.up()
    expect(await sideWidth(page)).toBeGreaterThanOrEqual(488)
    expect(await sideWidth(page)).toBeLessThanOrEqual(492)

    await page.reload()
    await expect(page.getByTestId('canvas')).toBeVisible()
    expect(await sideWidth(page)).toBeGreaterThanOrEqual(488)

    await page.getByTestId('side-resizer').dblclick()
    expect(await sideWidth(page)).toBe(340)

    // far right: never narrower than the minimum; far left: never wider than the maximum
    for (const [dx, expected] of [[+900, 280], [-2000, 900]] as const) {
      const h = (await page.getByTestId('side-resizer').boundingBox())!
      await page.mouse.move(h.x + h.width / 2, h.y + 40)
      await page.mouse.down()
      await page.mouse.move(Math.max(2, Math.min(1270, h.x + h.width / 2 + dx)), h.y + 40, { steps: 6 })
      await page.mouse.up()
      expect(await sideWidth(page)).toBe(expected)
    }
  })

  test('keyboard: arrows resize, Home resets; the handle is a labelled separator', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    const handle = page.getByTestId('side-resizer')
    await expect(handle).toHaveAttribute('role', 'separator')
    await expect(handle).toHaveAttribute('aria-valuenow', '340')
    await handle.focus()
    await page.keyboard.press('ArrowLeft')
    await page.keyboard.press('ArrowLeft')
    await expect(handle).toHaveAttribute('aria-valuenow', '388')
    await page.keyboard.press('ArrowRight')
    await expect(handle).toHaveAttribute('aria-valuenow', '364')
    await page.keyboard.press('Home')
    await expect(handle).toHaveAttribute('aria-valuenow', '340')
  })

  test('a wider panel really gives long fields more room', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', { command: 'echo hi' })])
    await open(page, flow.id)
    await node(page, 'c').click()
    const before = (await page.getByTestId('param-input-workingDirectory').boundingBox())!.width
    await page.getByTestId('side-resizer').focus()
    for (let i = 0; i < 5; i++) await page.keyboard.press('ArrowLeft')
    const after = (await page.getByTestId('param-input-workingDirectory').boundingBox())!.width
    expect(after - before).toBeGreaterThan(100)
  })
})

test.describe('long text is never silently cut off', () => {
  // names are limited to 100 characters, so this is as long as the app allows
  const LONG = 'Weekly report for the whole engineering organisation, all contractors and partner teams '.repeat(2).slice(0, 99).trim()

  test('a long card name shows in full on the card (it grows), on hover and in the inspector', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('t', 'trigger.manual', {}, 0, 0, LONG)])
    await open(page, flow.id)
    await expect(node(page, 't')).toHaveAttribute('title', `${LONG} (Manual trigger)`)
    const nameEl = node(page, 't').getByTestId('node-name')
    expect(await nameEl.evaluate((e) => e.scrollWidth <= e.clientWidth + 1)).toBe(true) // the card grows to hold the whole title…
    await node(page, 't').click()
    await expect(page.getByTestId('inspector-name')).toHaveValue(LONG) // …and complete where it is edited
  })

  test('long flow names and descriptions carry the full text as a tooltip in the list', async ({ page, request }) => {
    const flow = await (await request.post('/api/flows', { data: { name: LONG, description: LONG + ' (description)' } })).json()
    await page.goto('/#/')
    const row = page.getByTestId(`flow-row-${flow.id}`)
    await expect(row.getByTestId('flow-name')).toHaveAttribute('title', LONG)
    await expect(row.locator('p[title]').first()).toHaveAttribute('title', LONG + ' (description)')
  })

  test('a long output: preview with a count, copy, and a full-size view', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const flow = await makeFlow(request, [trigger(), script('big', "head -c 6000 /dev/zero | tr '\\0' 'x'")], [edge('t', 'big')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'big')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'big').click()
    await showJson(page, 'output')

    const viewer = page.getByTestId('output-data').getByTestId('output-viewer')
    await expect(viewer.getByTestId('output-cut')).toContainText('more characters')
    expect(await viewer.getByTestId('output-preview').evaluate((e) => (e as HTMLElement).innerText.length)).toBeLessThan(2000)
    await expect(viewer.getByTestId('output-size')).toContainText(/6,\d{3}|7,\d{3}/)

    await viewer.getByTestId('output-expand').click()
    const full = page.getByTestId('output-full')
    await expect(full).toBeVisible()
    expect(await full.evaluate((e) => (e as HTMLElement).innerText.length)).toBeGreaterThan(6000)
    await page.getByTestId('output-dialog-copy').click()
    await expect(page.getByTestId('output-dialog-copy')).toContainText('Copied')
    const clip = await page.evaluate(() => navigator.clipboard.readText())
    expect(clip.length).toBeGreaterThan(6000)
    expect(clip).toContain('x'.repeat(500))
    expect(JSON.parse(clip).exitCode).toBe(0) // it is the whole, valid JSON
    await page.getByTestId('output-dialog-close').click()
    await expect(page.getByTestId('output-dialog')).toBeHidden()
  })

  test('a short output has no cut note; Escape closes the full view', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('s', 'echo short')], [edge('t', 's')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 's')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 's').click()
    await showJson(page, 'output')
    await expect(page.getByTestId('output-cut')).toHaveCount(0)
    await page.getByTestId('output-expand').click()
    await expect(page.getByTestId('output-dialog')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.getByTestId('output-dialog')).toBeHidden()
  })

  test('long card descriptions are clamped with Show more / Show less', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', { command: 'echo' })])
    await open(page, flow.id)
    await node(page, 'c').click()
    const text = page.getByTestId('inspector-description')
    const toggle = page.getByTestId('inspector').getByTestId('expand-text')
    await expect(toggle).toContainText('Show more')
    const clamped = (await text.boundingBox())!.height
    await expect(text).toHaveAttribute('title', /Runs Shell, JavaScript/) // hover shows everything too
    await toggle.click()
    await expect(toggle).toContainText('Show less')
    expect((await text.boundingBox())!.height).toBeGreaterThan(clamped + 10)
    await toggle.click()
    expect((await text.boundingBox())!.height).toBeLessThanOrEqual(clamped + 1)
  })

  test('a long error is clamped but complete after Show more', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('bad', 'echo ' + 'boom '.repeat(120) + ' 1>&2; exit 4')], [edge('t', 'bad')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'bad')).toHaveAttribute('data-status', 'failed')
    await node(page, 'bad').click()
    const err = page.getByTestId('inspector-error')
    await err.getByTestId('expand-text').click()
    await expect(err).toContainText('code 4')
    await expect(err).toContainText('boom boom boom')
  })
})

test.describe('run log: clear, delete, live removal', () => {
  test('Clear removes finished runs and keeps a running one; each run can be deleted; the page follows pushed removals', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('nap', 'sleep {{run.input | sh}}')], [edge('t', 'nap')])
    const a = await runAndWait(request, flow.id, 0)
    const b = await runAndWait(request, flow.id, 0)
    const c = await runAndWait(request, flow.id, 0)
    const slow = await (await request.post(`/api/flows/${flow.id}/runs`, { data: { input: 20 } })).json()
    expect([a, b, c].every((r) => r.status === 'succeeded')).toBe(true)

    await open(page, flow.id)
    await expect(page.getByTestId('runs-count')).toHaveText('4')
    await expect(page.locator('[data-testid^="run-delete-"]')).toHaveCount(3) // the running one offers Cancel, not Delete
    await expect(page.getByTestId(`run-delete-${slow.id}`)).toHaveCount(0)

    // deleting one: the button, then the page and the backend agree
    const first = page.locator('[data-testid^="run-delete-"]').first()
    const deletedId = (await first.getAttribute('data-testid'))!.replace('run-delete-', '')
    await first.click()
    await expect(page.getByTestId(`run-row-${deletedId}`)).toHaveCount(0)
    expect((await request.get(`/api/runs/${deletedId}`)).status()).toBe(404)

    // removal pushed from somewhere else shows up without a reload
    const other = page.locator('[data-testid^="run-delete-"]').first()
    const otherId = (await other.getAttribute('data-testid'))!.replace('run-delete-', '')
    expect((await request.delete(`/api/runs/${otherId}`)).status()).toBe(204)
    await expect(page.getByTestId(`run-row-${otherId}`)).toHaveCount(0)

    // a running run cannot be deleted (409), and Clear leaves it alone
    const refused = await request.delete(`/api/runs/${slow.id}`)
    expect(refused.status()).toBe(409)
    expect((await refused.json()).error).toMatch(/Cancel/)
    await page.getByTestId('runs-clear').click()
    await expect(page.locator('[data-testid^="run-row-"]')).toHaveCount(1)
    await expect(page.getByTestId(`run-row-${slow.id}`)).toHaveAttribute('data-status', 'running')
    const left = await (await request.get(`/api/flows/${flow.id}/runs`)).json()
    expect(left.map((r: { id: string }) => r.id)).toEqual([slow.id])

    // clean up the slow process and clear again: nothing is left
    await page.getByTestId(`run-cancel-${slow.id}`).click()
    await expect(page.getByTestId(`run-row-${slow.id}`)).toHaveAttribute('data-status', 'cancelled')
    await page.getByTestId('runs-clear').click()
    await expect(page.getByTestId('runs-empty')).toBeVisible()
    await expect(page.getByTestId('runs-clear')).toBeDisabled()
  })

  test('Clear only touches this flow', async ({ page, request }) => {
    const mine = await makeFlow(request, [trigger(), script('s', 'echo mine')], [edge('t', 's')])
    const theirs = await makeFlow(request, [trigger(), script('s', 'echo theirs')], [edge('t', 's')])
    await runAndWait(request, mine.id)
    await runAndWait(request, theirs.id)
    await open(page, mine.id)
    await page.getByTestId('runs-clear').click()
    await expect(page.getByTestId('runs-empty')).toBeVisible()
    expect(await (await request.get(`/api/flows/${theirs.id}/runs`)).json()).toHaveLength(1)
  })

  test('the log cannot grow without bound: only the newest runs per flow are kept', async ({ request }) => {
    const flow = await makeFlow(request, [trigger()])
    for (let i = 0; i < 56; i++) await request.post(`/api/flows/${flow.id}/runs`, { data: {} })
    await expect.poll(async () => (await (await request.get(`/api/flows/${flow.id}/runs`)).json()).length, { timeout: 20_000 }).toBeLessThanOrEqual(50)
    const runs = await (await request.get(`/api/flows/${flow.id}/runs`)).json()
    expect(runs.every((r: { status: string }) => r.status !== 'running')).toBe(true)
  })
})

test.describe('code editor', () => {
  test('language follows the Language select; line numbers; brackets close themselves', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', { command: '' })])
    await open(page, flow.id)
    await node(page, 'c').click()
    const field = page.getByTestId('param-input-command')
    await expect(field.getByTestId('code-language')).toHaveText('Shell')
    await expect(field.locator('.cm-lineNumbers')).toBeVisible()

    await page.getByTestId('param-input-language').selectOption('python')
    await expect(field.getByTestId('code-language')).toHaveText('Python')
    await page.getByTestId('param-input-language').selectOption('javascript')
    await expect(field.getByTestId('code-language')).toHaveText('JavaScript')

    await codeOf(page, 'param-input-command').click()
    await page.keyboard.type('console.log(')
    await expect(codeOf(page, 'param-input-command')).toContainText('console.log()') // the closing bracket was added
    await page.keyboard.type('"hi"')
    await expect(codeOf(page, 'param-input-command')).toContainText('console.log("hi")') // typing the closer steps over it

    await page.keyboard.press('ControlOrMeta+a')
    await page.keyboard.press('Backspace')
    await page.keyboard.type('if (x) {')
    await expect(codeOf(page, 'param-input-command')).toContainText('if (x) {}')
    await page.keyboard.press('Enter')
    await expect(field.locator('.cm-line')).toHaveCount(3) // opening line, an indented empty line, the closing brace
    await page.keyboard.type('go()')
    await expect(codeOf(page, 'param-input-command')).toContainText('go()')
  })

  test('open it in a large editor, edit there, and the change is in the field', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', { command: 'echo one', language: 'shell' })])
    await open(page, flow.id)
    await node(page, 'c').click()
    await page.getByTestId('code-expand').click()
    await expect(page.getByTestId('code-dialog')).toBeVisible()
    const dialogEditor = page.getByTestId('code-dialog-editor').locator('.cm-content')
    await expect(dialogEditor).toContainText('echo one')
    expect((await page.getByTestId('code-dialog-editor').boundingBox())!.height).toBeGreaterThan(400)
    await dialogEditor.click()
    await page.keyboard.press('ControlOrMeta+End')
    await page.keyboard.insertText('\necho two')
    await page.getByTestId('code-collapse').click()
    await expect(page.getByTestId('code-dialog')).toBeHidden()
    await expect(page.getByTestId('param-input-command').locator('.cm-content').first()).toContainText('echo two')
    await expect(page.getByTestId('edit-save')).toBeEnabled() // it counts as a change
  })

  test('the editor can be dragged taller (bottom edge resizes it)', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', { command: 'echo' })])
    await open(page, flow.id)
    await node(page, 'c').click()
    const box = page.getByTestId('param-input-command').getByTestId('code-resize')
    expect(await box.evaluate((e) => getComputedStyle(e).resize)).toBe('vertical')
    const before = (await box.boundingBox())!.height
    await box.evaluate((e) => ((e as HTMLElement).style.height = '320px')) // what dragging the corner does
    expect((await box.boundingBox())!.height).toBeGreaterThan(before + 100)
  })

  test('JavaScript and Python scripts run, read their input and their JSON output is usable', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        card('js', 'script.shell', { language: 'javascript', command: 'const i = JSON.parse(process.env.TAMMOA_INPUT); console.log(JSON.stringify({ shout: String(i.input).toUpperCase() }))' }, 280, 0, 'Node'),
        card('py', 'script.shell', { language: 'python', command: "import json, os\nprint(json.dumps({'len': len(json.loads(os.environ['TAMMOA_RUN_INPUT']))}))" }, 280, 100, 'Python'),
        setv('both', '{"a":"{{input.json.shout}}"}', 560),
      ],
      [edge('t', 'js'), edge('t', 'py'), edge('js', 'both')],
    )
    await open(page, flow.id)
    await page.getByTestId('run-input').fill('hello')
    await page.getByTestId('run').click()
    for (const id of ['js', 'py', 'both']) await expect(node(page, id)).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'js').click()
    await showJson(page, 'output')
    await expect(page.getByTestId('output-preview')).toContainText('"shout": "HELLO"')
    await node(page, 'py').click()
    await showJson(page, 'output')
    await expect(page.getByTestId('output-preview')).toContainText('"len": 5')
    await node(page, 'both').click()
    await showJson(page, 'output')
    await expect(page.getByTestId('output-preview')).toContainText('"a": "HELLO"')
  })

  test('a missing interpreter is explained, and a script error shows the interpreter\'s message', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('a', 'script.shell', { language: 'javascript', command: '1', interpreter: 'no-such-runtime' }, 280, 0, 'A'), card('b', 'script.shell', { language: 'python', command: 'def (' }, 280, 100, 'B')], [edge('t', 'a'), edge('t', 'b')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'a')).toHaveAttribute('data-status', 'failed')
    await expect(node(page, 'b')).toHaveAttribute('data-status', 'failed')
    await node(page, 'a').click()
    await expect(page.getByTestId('inspector-error')).toContainText("'no-such-runtime' was not found")
    await node(page, 'b').click()
    await expect(page.getByTestId('inspector-error')).toContainText('SyntaxError')
  })

  test('editing code in the editor and running uses the edited code', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('c', 'script.shell', { command: 'echo old' })], [edge('t', 'c')])
    await open(page, flow.id)
    await node(page, 'c').click()
    await setCode(page, 'param-input-command', 'echo edited in the editor')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run').click()
    await expect(node(page, 'c')).toHaveAttribute('data-status', 'succeeded')
    await showJson(page, 'output')
    await expect(page.getByTestId('output-preview')).toContainText('edited in the editor')
  })
})

test.describe('palette: groups and search', () => {
  test('cards are grouped, searchable, and pickable from the search', async ({ page, request }) => {
    const flow = await makeFlow(request, [])
    await open(page, flow.id)
    await page.getByTestId('palette-toggle').click()
    for (const g of ['trigger', 'logic', 'data', 'action', 'ai']) await expect(page.getByTestId(`palette-group-${g}`)).toBeVisible()
    await expect(page.locator('[data-testid^="palette-item-"]')).toHaveCount(12)
    await expect(page.getByTestId('palette-search')).toBeFocused()

    await page.getByTestId('palette-search').fill('olla')
    await expect(page.locator('[data-testid^="palette-item-"]')).toHaveCount(1)
    await expect(page.getByTestId('palette-group-logic')).toHaveCount(0)

    await page.getByTestId('palette-search').fill('web api')
    await expect(page.getByTestId('palette-item-net.http')).toBeVisible()

    await page.getByTestId('palette-search').fill('zzz-nothing')
    await expect(page.getByTestId('palette-empty')).toBeVisible()

    await page.getByTestId('palette-search').fill('wait')
    await page.getByTestId('palette-item-flow.wait').click()
    await expect(page.getByTestId('palette')).toBeHidden()
    await expect(page.locator('[data-node-card]')).toHaveCount(1)
    await expect(page.getByTestId('param-input-seconds')).toHaveValue('1') // backend default shown
  })
})

test.describe('select params and branching (IF)', () => {
  test('IF shows true/false outputs; drag each to a different card; only the matching branch runs', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        card('i', 'logic.if', { value1: '{{input.input}}', operator: 'equals', value2: 'yes' }, 260, 40, 'Decide'),
        script('yes', 'echo YES', 560),
        { ...script('no', 'echo NO', 560), position: { x: 560, y: 150 } },
      ],
      [edge('t', 'i')],
    )
    await open(page, flow.id)
    await expect(node(page, 'i').getByTestId('handle-label')).toHaveText(['true', 'false'])
    await drag(page, node(page, 'i').getByTestId('out-i-true'), node(page, 'yes'))
    await drag(page, node(page, 'i').getByTestId('out-i-false'), node(page, 'no'))
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(3)
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()

    const saved = await (await request.get(`/api/flows/${flow.id}`)).json()
    expect(saved.definition.edges.map((e: { source: string; sourceHandle: string; target: string }) => `${e.source}.${e.sourceHandle}->${e.target}`).sort()).toEqual(['i.false->no', 'i.true->yes', 't.main->i'])

    await page.getByTestId('run-input').fill('yes')
    await page.getByTestId('run').click()
    await expect(node(page, 'yes')).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'no')).toHaveAttribute('data-status', 'skipped')
    await page.getByTestId('run-input').fill('nope')
    await page.getByTestId('run').click()
    await expect(node(page, 'no')).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'yes')).toHaveAttribute('data-status', 'skipped')
  })

  test('the operator select works, and changing it changes the branch', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), card('i', 'logic.if', { value1: '{{input.input}}', value2: '5' }, 260, 40, 'Compare'), script('big', 'echo big', 560)],
      [edge('t', 'i'), edge('i', 'big', 'true')],
    )
    await open(page, flow.id)
    await node(page, 'i').click()
    const op = page.getByTestId('param-input-operator')
    await expect(op.locator('option')).toHaveCount(11)
    await expect(op).toHaveValue('equals') // the default is shown, not a blank
    await op.selectOption('greaterThan')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run-input').fill('9')
    await page.getByTestId('run').click()
    await expect(node(page, 'big')).toHaveAttribute('data-status', 'succeeded')
    await page.getByTestId('run-input').fill('1')
    await page.getByTestId('run').click()
    await expect(node(page, 'big')).toHaveAttribute('data-status', 'skipped')
  })

  test('a connection from an IF output survives save and reload on the right handle', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('i', 'logic.if', {}, 260, 40), script('a', 'echo a', 560)], [edge('t', 'i'), edge('i', 'a', 'false')])
    await open(page, flow.id)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(2)
    const path = await page.locator('[data-testid="edge-e-i-false-a"] path').first().getAttribute('d')
    expect(path).toBeTruthy()
    // the false handle sits lower than the true handle: the line starts below the card's middle
    const startY = Number(path!.split(' ')[2])
    expect(startY).toBeGreaterThan(40 + 60 / 2)
  })
})
