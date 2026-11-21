import { expect, test, type Page } from '@playwright/test'
import { card, codeOf, edge, makeFlow, node, open, openTab, outputOf, runAndWait, script, setCode, showJson, trigger } from './helpers'

const GREETING = `echo '{"greeting":"Hello Sok","items":["a","b"],"name":"Sok"}'`
const rowIn = (page: Page, scope: string, path: string) => page.getByTestId(scope).locator(`[data-testid="field-row"][data-path="${path}"]`)
const clipboard = (page: Page) => page.evaluate(() => navigator.clipboard.readText())

test.describe('Input tab: what this card received', () => {
  test('shows the real last input as usable fields; Copy puts the exact reference on the clipboard', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('b', 'script.shell', { command: 'echo got' }, 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await page.getByTestId('run-input').fill('{"city":"Phnom Penh"}')
    await page.getByTestId('run').click()
    await expect(node(page, 'b')).toHaveAttribute('data-status', 'succeeded')

    await node(page, 'b').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-source')).toContainText('From the selected run')
    const greeting = rowIn(page, 'input-data', 'input.json.greeting')
    await expect(greeting).toBeVisible()
    await expect(greeting.getByTestId('field-type')).toHaveText('text')
    await expect(greeting.getByTestId('field-preview')).toHaveText('Hello Sok')
    await expect(rowIn(page, 'input-data', 'input.exitCode').getByTestId('field-type')).toHaveText('number')
    await expect(rowIn(page, 'input-data', 'input.json.items')).toContainText('2 items')
    await expect(rowIn(page, 'input-data', 'input.json.items[0]')).toBeVisible()

    await greeting.getByTestId('field-copy').click()
    expect(await clipboard(page)).toBe('{{input.json.greeting}}')
    await rowIn(page, 'input-data', 'input.json.items[0]').getByTestId('field-copy').click()
    expect(await clipboard(page)).toBe('{{input.json.items[0]}}')

    // the Run input, available in every card, is listed too with its own reference
    await expect(rowIn(page, 'run-input-data', 'run.input.city').getByTestId('field-preview')).toHaveText('Phnom Penh')
    await rowIn(page, 'run-input-data', 'run.input.city').getByTestId('field-copy').click()
    expect(await clipboard(page)).toBe('{{run.input.city}}')
  })

  test('Fields / JSON: the same input as raw JSON', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('b', 'script.shell', { command: 'echo got' }, 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'b')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'b').click()
    await showJson(page, 'input')
    await expect(page.getByTestId('output-preview')).toContainText('"greeting": "Hello Sok"')
    await page.getByTestId('input-data').getByTestId('view-fields').click()
    await expect(rowIn(page, 'input-data', 'input.json.greeting')).toBeVisible()
  })

  test('before any run it shows what the connected card usually provides, then the real data replaces it', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('s', 'data.set', {}, 560)], [edge('t', 'a'), edge('a', 's')])
    await open(page, flow.id)
    await node(page, 's').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-expected-note')).toContainText('Not run yet')
    const expected = page.getByTestId('input-expected')
    await expect(expected).toContainText('From Script a')
    await expect(rowIn(page, 'input-expected', 'input.json.greeting')).toBeVisible() // from the Script card's documented output
    await expect(page.getByTestId('input-data')).toHaveCount(0)
    await expect(page.getByTestId('run-input-empty')).toBeVisible()

    await page.getByTestId('run').click()
    await expect(page.getByTestId('input-source')).toBeVisible() // real data arrived without reopening the tab
    await expect(page.getByTestId('input-expected')).toHaveCount(0)
    await expect(rowIn(page, 'input-data', 'input.json.greeting').getByTestId('field-preview')).toHaveText('Hello Sok')
  })

  test('a card with nothing connected says how to connect it', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('s', 'data.set', {}, 0)])
    await open(page, flow.id)
    await node(page, 's').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-unconnected')).toContainText('Nothing is connected')
  })

  test('a trigger has no previous card: it shows the Run input', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    await node(page, 't').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-previous')).toHaveCount(0)
    await expect(page.getByTestId('run-input-empty')).toBeVisible()
    await page.getByTestId('run-input').fill('hello')
    await page.getByTestId('run').click()
    await expect(rowIn(page, 'run-input-data', 'run.input').getByTestId('field-preview')).toHaveText('hello')
  })

  test('several connections: expected fields are numbered input[0], input[1]', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), script('a', GREETING, 280), { ...script('b', GREETING, 280), position: { x: 280, y: 110 } }, card('m', 'data.merge', {}, 560)],
      [edge('t', 'a'), edge('t', 'b'), edge('a', 'm'), edge('b', 'm')],
    )
    await open(page, flow.id)
    await node(page, 'm').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-expected')).toHaveCount(2)
    await expect(page.locator('[data-path="input[0].json.greeting"]')).toBeVisible()
    await expect(page.locator('[data-path="input[1].json.greeting"]')).toBeVisible()
    await expect(page.getByTestId('input-several')).toContainText('a list')
  })

  test('a failed card still shows the input it was given', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('s', 'data.set', { values: '{"a": 41}' }), script('bad', 'exit 3', 560)], [edge('t', 's'), edge('s', 'bad')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'bad')).toHaveAttribute('data-status', 'failed')
    await node(page, 'bad').click()
    await expect(page.getByTestId('inspector-error')).toContainText('code 3')
    await openTab(page, 'input')
    await expect(rowIn(page, 'input-data', 'input.a').getByTestId('field-preview')).toHaveText('41')
  })

  test('if the card did not run in the selected run, the newest run where it did is shown, and labelled', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), card('i', 'logic.if', { value1: '{{run.input}}', operator: 'equals', value2: 'yes' }, 260), script('y', 'echo yes-branch', 560)],
      [edge('t', 'i'), edge('i', 'y', 'true')],
    )
    await runAndWait(request, flow.id, 'yes') // y runs
    await runAndWait(request, flow.id, 'no') // y is skipped (newest run)
    await open(page, flow.id)
    await expect(node(page, 'y')).toHaveAttribute('data-status', 'skipped') // the newest run is selected
    await node(page, 'y').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('input-source')).toContainText('earlier run')
    await expect(page.getByTestId('input-data')).toBeVisible()
  })

  test('the chosen tab is remembered while you click from card to card; tabs with real data have a dot', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('b', 'script.shell', { command: 'echo got' }, 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await node(page, 'a').click()
    await expect(page.getByTestId('tab-has-data')).toHaveCount(0) // nothing has run
    await page.getByTestId('run').click()
    await expect(node(page, 'b')).toHaveAttribute('data-status', 'succeeded')
    await openTab(page, 'input')
    await node(page, 'b').click()
    await expect(page.getByTestId('inspector-tab-input')).toHaveAttribute('aria-selected', 'true')
    await expect(page.getByTestId('panel-input')).toBeVisible()
    await expect(page.getByTestId('tab-has-data')).toHaveCount(2) // Input and Output of a card that ran
  })

  test('long values keep their full text as a tooltip; many fields get a filter; names a template cannot use are marked', async ({ page, request }) => {
    const code = `node -e "console.log(JSON.stringify({ long: 'z'.repeat(300), 'a.b': 1, ok: true, f1:1,f2:2,f3:3,f4:4,f5:5,f6:6,f7:7 }))"`
    const flow = await makeFlow(request, [trigger(), script('a', code), card('b', 'script.shell', { command: 'echo got' }, 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'b')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'b').click()
    await openTab(page, 'input')
    const long = rowIn(page, 'input-data', 'input.json.long')
    expect(((await long.getByTestId('field-preview').textContent()) ?? '').length).toBeLessThan(100)
    await expect(long.getByTestId('field-preview')).toHaveAttribute('title', 'z'.repeat(300))
    const odd = rowIn(page, 'input-data', 'input.json.a.b') // a key with a dot cannot be written in a template…
    await expect(odd).toBeVisible()
    await expect(odd.getByTestId('field-copy')).toHaveCount(0) // …so no broken reference is offered
    await expect(odd.getByTestId('field-unaddressable')).toHaveAttribute('title', /cannot use/) // it explains instead
    await page.getByTestId('fields-filter').fill('f7')
    await expect(page.getByTestId('field-row')).toHaveCount(1)
    await page.getByTestId('fields-filter').fill('no-such-field')
    await expect(page.getByTestId('fields-none')).toBeVisible()
  })
})

test.describe('Output tab: what the next card can use', () => {
  test('before a run it shows the kind of output; after, the real fields; Copy gives the reference for the NEXT card', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('b', 'script.shell', { command: 'echo got' }, 560)], [edge('t', 'a'), edge('a', 'b')])
    await open(page, flow.id)
    await node(page, 'a').click()
    await openTab(page, 'output')
    await expect(page.getByTestId('output-example-note')).toContainText('has not run yet')
    await expect(page.locator('[data-path="input.json.greeting"]').first()).toBeVisible()

    await page.getByTestId('run').click()
    await expect(node(page, 'a')).toHaveAttribute('data-status', 'succeeded')
    await expect(page.getByTestId('output-source')).toContainText('card after this one receives this as its input')
    const row = rowIn(page, 'output-data', 'input.json.greeting')
    await expect(row.getByTestId('field-preview')).toHaveText('Hello Sok')
    await row.getByTestId('field-copy').click()
    expect(await clipboard(page)).toBe('{{input.json.greeting}}') // "input" because that is how the next card sees it
  })
})

test.describe('the backend keeps what each card received', () => {
  test('NodeRun.input: the parent output, a list when several connections are active, kept after a failure, none when skipped or for a trigger', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        card('a', 'data.set', { values: '{"v":1}' }, 200),
        { ...card('b', 'data.set', { values: '{"w":2}' }, 200), position: { x: 200, y: 110 } },
        card('m', 'data.merge', {}, 420),
        script('bad', 'exit 2', 640),
        card('i', 'logic.if', { value1: 'a', operator: 'equals', value2: 'b' }, 200, 230),
        card('never', 'data.set', { values: '{}' }, 420, 230),
      ],
      [edge('t', 'a'), edge('t', 'b'), edge('a', 'm'), edge('b', 'm'), edge('m', 'bad'), edge('t', 'i'), edge('i', 'never', 'true')],
    )
    const run = await runAndWait(request, flow.id, 'hi')
    const input = (id: string) => run.nodes.find((n) => n.nodeId === id)!.input
    expect(input('t')).toBeUndefined()
    expect(input('a')).toEqual({ input: 'hi' })
    expect(input('m')).toEqual([{ v: 1 }, { w: 2 }])
    expect(input('bad')).toEqual({ v: 1, w: 2 })
    expect(run.nodes.find((n) => n.nodeId === 'bad')!.status).toBe('failed')
    expect(run.nodes.find((n) => n.nodeId === 'never')!.status).toBe('skipped')
    expect(input('never')).toBeUndefined()
  })
})

test.describe('Guide tab: a tutorial for every card', () => {
  test('every card type has a guide with its overview, an output example and examples', async ({ page, request }) => {
    const types = (await (await request.get('/api/cards/types')).json()) as { type: string; name: string; guide: { overview: string; inputNote: string; examples: { title: string; params?: unknown }[]; tips?: string[] } }[]
    expect(types).toHaveLength(12)
    const nodes = types.map((t, i) => card(`c${i}`, t.type, {}, (i % 4) * 230, Math.floor(i / 4) * 90, t.name))
    const flow = await makeFlow(request, nodes)
    await open(page, flow.id)
    for (const [i, t] of types.entries()) {
      await node(page, `c${i}`).click()
      await openTab(page, 'guide')
      await expect(page.getByTestId('guide-overview'), t.type).toHaveText(t.guide.overview)
      await expect(page.getByTestId('guide-input'), t.type).toContainText(t.guide.inputNote)
      await expect(page.getByTestId('guide-output'), t.type).toBeVisible()
      await expect(page.getByTestId('guide-example'), t.type).toHaveCount(t.guide.examples.length)
      await expect(page.getByTestId('guide-example-body'), t.type).toHaveCount(1) // the first example is open
      if (t.guide.tips?.length) await expect(page.getByTestId('guide-tips').locator('li'), t.type).toHaveCount(t.guide.tips.length)
      if (t.guide.examples[0].params) await expect(page.getByTestId('guide-apply'), t.type).toHaveCount(1)
    }
  })

  test('"How data moves between cards" explains input, the Run input, templates and the script variables', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('s', 'data.set', {})])
    await open(page, flow.id)
    await node(page, 's').click()
    await openTab(page, 'guide')
    await expect(page.getByTestId('guide-basics')).toHaveCount(0)
    await page.getByTestId('guide-basics-toggle').click()
    const basics = page.getByTestId('guide-basics')
    await expect(basics.locator('li')).toHaveCount(6)
    for (const text of ['{{input.json.name}}', '{{run.input}}', 'input_data', 'runInput', '{{data.Style}}', 'loop']) await expect(basics).toContainText(text)
  })

  test('an example shows the input it assumes, as fields you can read, and what the card then outputs', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', {})])
    await open(page, flow.id)
    await node(page, 'c').click()
    await openTab(page, 'guide')
    const first = page.getByTestId('guide-example').first()
    await expect(first).toContainText('JavaScript: read a value and hand data on')
    await expect(first.getByTestId('guide-sample-input').locator('[data-path="input.json.name"]')).toBeVisible()
    await expect(first.getByTestId('guide-result')).toContainText('{{input.json.greeting}}')
    await expect(first.getByTestId('code-snippet')).toBeVisible()
    await expect(first.locator('.cm-content')).toContainText('const name = input.json.name')
    await first.getByTestId('guide-example-toggle').click()
    await expect(first.getByTestId('guide-example-body')).toHaveCount(0)
  })

  test('Copy on a snippet copies the code', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const flow = await makeFlow(request, [card('c', 'script.shell', {})])
    await open(page, flow.id)
    await node(page, 'c').click()
    await openTab(page, 'guide')
    await page.getByTestId('snippet-copy').first().click()
    expect(await clipboard(page)).toContain('const name = input.json.name')
  })

  test('"Use this example" fills in the settings, returns to the Settings tab and says so', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', {})])
    await open(page, flow.id)
    await node(page, 'c').click()
    await openTab(page, 'guide')
    await page.getByTestId('guide-apply').first().click()
    await expect(page.getByTestId('panel-settings')).toBeVisible()
    await expect(page.getByTestId('param-input-language')).toHaveValue('javascript')
    await expect(codeOf(page, 'param-input-command')).toContainText('const name = input.json.name')
    await expect(page.getByTestId('canvas-notice')).toContainText('Example applied')
    await expect(page.getByTestId('edit-save')).toBeEnabled() // it is a change to save
  })

  test('FOLLOW THE TUTORIAL: JavaScript, Python and shell examples read the previous card\'s output and produce what the guide says', async ({ page, request }) => {
    const cases = [
      { title: 'JavaScript: read a value and hand data on', check: (out: any) => expect(out.json.greeting).toBe('Hello Sok') },
      { title: 'Python: read a value and hand data on', check: (out: any) => expect(out.json.greeting).toBe('Hello Sok') },
      { title: 'Shell: put a value into a command', check: (out: any) => expect(out.stdout.trim()).toBe('Hello Sok') },
    ]
    for (const { title, check } of cases) {
      const flow = await makeFlow(request, [trigger(), script('a', `echo '{"name":"Sok"}'`), card('b', 'script.shell', {}, 560, 0, 'Tutorial card')], [edge('t', 'a'), edge('a', 'b')])
      await open(page, flow.id)
      await node(page, 'b').click()
      await openTab(page, 'guide')
      const example = page.getByTestId('guide-example').filter({ hasText: title })
      if ((await example.getByTestId('guide-example-body').count()) === 0) await example.getByTestId('guide-example-toggle').click() // the first one is already open
      await example.getByTestId('guide-apply').click()
      await page.getByTestId('edit-save').click()
      await expect(page.getByTestId('edit-saved')).toBeVisible()
      await page.getByTestId('run').click()
      await expect(node(page, 'b'), title).toHaveAttribute('data-status', 'succeeded')
      const res = await request.get(`/api/flows/${flow.id}/runs`)
      const run = await res.json()
      check(outputOf(run[0], 'b'))
    }
  })
})

test.describe('the script card explains how to read the input, in the language you picked', () => {
  test('hint under the editor and the starter code change with the language; Examples opens the guide', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('c', 'script.shell', {})])
    await open(page, flow.id)
    await node(page, 'c').click()
    const hint = page.getByTestId('code-hint')
    const language = page.getByTestId('param-input-language')

    await expect(hint).toContainText('TAMMOA_INPUT') // shell is the default
    await expect(hint).toContainText('{{input.stdout | sh}}')

    await language.selectOption('javascript')
    await expect(hint).toContainText('input.json.name')
    await expect(hint).toContainText('runInput')
    await expect(page.locator('.cm-placeholder')).toContainText('input = the output of the previous card')

    await language.selectOption('python')
    await expect(hint).toContainText('input_data["json"]["name"]')
    await expect(hint).toContainText('run_input')
    await expect(page.locator('.cm-placeholder')).toContainText('input_data = the output of the previous card')

    await page.getByTestId('code-open-guide').click()
    await expect(page.getByTestId('panel-guide')).toBeVisible()
    await expect(page.getByTestId('inspector-tab-guide')).toHaveAttribute('aria-selected', 'true')
  })

  test('the ready-made variables work: JavaScript input / runInput and Python input_data / run_input, written by hand', async ({ page, request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), script('a', GREETING), card('js', 'script.shell', { language: 'javascript', command: '' }, 560, 0, 'Node'), card('py', 'script.shell', { language: 'python', command: '' }, 560, 110, 'Py')],
      [edge('t', 'a'), edge('a', 'js'), edge('a', 'py')],
    )
    await open(page, flow.id)
    await node(page, 'js').click()
    await setCode(page, 'param-input-command', 'console.log(JSON.stringify({ who: input.json.name, items: input.json.items.length, asked: runInput }))')
    await node(page, 'py').click()
    await setCode(page, 'param-input-command', 'import json\nprint(json.dumps({"who": input_data["json"]["name"], "asked": run_input}))')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run-input').fill('from the run box')
    await page.getByTestId('run').click()
    await expect(node(page, 'js')).toHaveAttribute('data-status', 'succeeded')
    await expect(node(page, 'py')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'js').click()
    await openTab(page, 'output')
    await expect(rowIn(page, 'output-data', 'input.json.who').getByTestId('field-preview')).toHaveText('Sok')
    await expect(rowIn(page, 'output-data', 'input.json.items').getByTestId('field-preview')).toHaveText('2') // the script printed the count
    await expect(rowIn(page, 'output-data', 'input.json.asked').getByTestId('field-preview')).toHaveText('from the run box')
    await node(page, 'py').click()
    await expect(rowIn(page, 'output-data', 'input.json.who').getByTestId('field-preview')).toHaveText('Sok')
  })

  test('the grey starter is only a hint: "Insert starter" makes it real code, and running empty code explains what to do', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('a', GREETING), card('c', 'script.shell', { language: 'javascript', command: '' }, 560)], [edge('t', 'a'), edge('a', 'c')])
    await open(page, flow.id)
    await page.getByTestId('run').click() // empty code
    await expect(node(page, 'c')).toHaveAttribute('data-status', 'failed')
    await node(page, 'c').click()
    await expect(page.getByTestId('inspector-error')).toContainText('Use this example')

    await expect(page.locator('.cm-placeholder')).toContainText('input = the output of the previous card')
    await page.getByTestId('code-insert-starter').click()
    await expect(page.getByTestId('code-insert-starter')).toHaveCount(0) // it is code now, not a hint
    await expect(page.locator('.cm-placeholder')).toHaveCount(0)
    await expect(codeOf(page, 'param-input-command')).toContainText("const name = input.json?.name ?? 'world'")
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run').click()
    await expect(node(page, 'c')).toHaveAttribute('data-status', 'succeeded') // the starter really runs
    await openTab(page, 'output')
    await expect(rowIn(page, 'output-data', 'input.json.greeting').getByTestId('field-preview')).toHaveText('Hello Sok')
  })

  test('an error in your code shows YOUR line number', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('c', 'script.shell', { language: 'javascript', command: 'const a = 1\nconst b = 2\nthrow new Error("on line three")' }, 280)], [edge('t', 'c')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'c')).toHaveAttribute('data-status', 'failed')
    await node(page, 'c').click()
    await expect(page.getByTestId('inspector-error')).toContainText('script.js:3')
    await expect(page.getByTestId('inspector-error')).toContainText('on line three')
  })
})

test.describe('navigation', () => {
  test('going straight from one flow to another (no list in between) shows the other flow, with its own runs', async ({ page, request }) => {
    const a = await makeFlow(request, [trigger(), script('only-in-a', 'echo a', 280)], [edge('t', 'only-in-a')])
    const b = await makeFlow(request, [trigger(), script('only-in-b', 'echo b', 280)], [edge('t', 'only-in-b')])
    await runAndWait(request, a.id)
    await open(page, a.id)
    await expect(page.getByTestId('edit-name')).toHaveValue(a.name)
    await expect(node(page, 'only-in-a')).toBeVisible()
    await expect(page.locator('[data-testid^="run-row-"]')).toHaveCount(1)

    await page.evaluate((id) => (window.location.hash = `#/flows/${id}`), b.id) // as if a link or the history took you there
    await expect(page.getByTestId('edit-name')).toHaveValue(b.name)
    await expect(node(page, 'only-in-b')).toBeVisible()
    await expect(node(page, 'only-in-a')).toHaveCount(0)
    await expect(page.locator('[data-testid^="run-row-"]')).toHaveCount(0) // b has no runs: a's are not shown
    await page.getByTestId('run').click()
    await expect(node(page, 'only-in-b')).toHaveAttribute('data-status', 'succeeded')
    expect((await (await request.get(`/api/flows/${b.id}/runs`)).json()).length).toBe(1) // the run went to b, not to a
    expect((await (await request.get(`/api/flows/${a.id}/runs`)).json()).length).toBe(1)
  })
})
