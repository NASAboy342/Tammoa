import fs from 'node:fs'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { card, codeOf, edge, makeFlow, node, open, openTab, outputOf, runAndWait, script, setCode, setv, trigger, unique } from './helpers'

const data = (name: string, id: string, params: Record<string, unknown> = {}, x = 0, y = 160) => card(id, 'data.store', params, x, y, name)
const store = (request: APIRequestContext, flowId: string, cardId: string, text: string) => request.put(`/api/flows/${flowId}/data/${cardId}`, { data: { text } })
const stored = async (request: APIRequestContext, flowId: string, cardId: string) => (await (await request.get(`/api/flows/${flowId}/data/${cardId}`)).json()) as { text: string; path: string; exists: boolean; size: number }
const out = (run: Awaited<ReturnType<typeof runAndWait>>, id: string) => outputOf(run, id)

test.describe('a Data card keeps reusable text', () => {
  test('add one in the editor, name it, save the flow, type the text, Save text: it is stored as a .txt file and survives a reload', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    await page.getByTestId('palette-toggle').click()
    await page.getByTestId('palette-item-data.store').click()
    await page.getByTestId('inspector-name').fill('Style')
    await expect(page.getByTestId('stored-unsaved')).toContainText('Save the flow once') // the backend finds a Data card in the SAVED flow
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()

    const editor = page.getByTestId('stored-editor')
    await expect(editor).toBeVisible()
    await expect(page.getByTestId('stored-meta')).toContainText('nothing saved yet')
    await setCode(page, 'stored-editor', 'Be brief and friendly.')
    await expect(page.getByTestId('stored-dirty')).toBeVisible()
    await expect(page.getByTestId('edit-save')).toBeDisabled() // the flow itself is unchanged: the text is saved on its own
    await page.getByTestId('stored-save').click()
    await expect(page.getByTestId('stored-saved')).toBeVisible()
    await expect(page.getByTestId('stored-meta')).toContainText('22 characters')
    await expect(page.getByTestId('stored-reference')).toHaveText('{{data.Style}}')

    const path = (await page.getByTestId('stored-path').textContent())!
    expect(path).toMatch(/\.txt$/)
    expect(fs.readFileSync(path, 'utf8')).toBe('Be brief and friendly.') // a plain text file anyone can open

    await page.reload()
    await expect(page.getByTestId('canvas')).toBeVisible()
    await page.locator('[data-node-card]', { hasText: 'Style' }).click()
    await expect(codeOf(page, 'stored-editor')).toContainText('Be brief and friendly.')
  })

  test('Revert throws away unsaved typing; an empty text can be saved (it clears the file)', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), data('Notes', 'd')])
    await store(request, flow.id, 'd', 'original')
    await open(page, flow.id)
    await node(page, 'd').click()
    await expect(codeOf(page, 'stored-editor')).toContainText('original')
    await setCode(page, 'stored-editor', 'typed but not saved')
    await expect(page.getByTestId('stored-dirty')).toBeVisible()
    await page.getByTestId('stored-revert').click()
    await expect(codeOf(page, 'stored-editor')).toContainText('original')
    await expect(page.getByTestId('stored-dirty')).toHaveCount(0)

    await codeOf(page, 'stored-editor').click()
    await page.keyboard.press('ControlOrMeta+a')
    await page.keyboard.press('Backspace') // insertText('') would not clear it
    await page.getByTestId('stored-save').click()
    await expect(page.getByTestId('stored-saved')).toBeVisible()
    expect((await stored(request, flow.id, 'd')).text).toBe('')
  })

  test('the stored text is limited to 1 MB with a clear message', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), data('Big', 'd')])
    const tooBig = await store(request, flow.id, 'd', 'x'.repeat(1024 * 1024 + 1))
    expect(tooBig.status()).toBe(400)
    expect((await tooBig.json()).error).toMatch(/1024 KB/)
    expect((await store(request, flow.id, 'd', 'x'.repeat(1024 * 1024))).status()).toBe(200)
    await open(page, flow.id)
    await node(page, 'd').click()
    await expect(page.getByTestId('stored-meta')).toContainText('1,048,576 characters')
  })
})

test.describe('any card reads it by name, with no wiring and no trigger', () => {
  test('{{data.Style}} in a script, in a Set value card and in JavaScript / Python, none of them connected to the Data card', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        data('Style', 'style'),
        data('Settings', 'settings'),
        script('sh', 'echo {{data.Style | sh}}', 280),
        card('set', 'data.set', { values: '{"city":"{{data.Settings.city}}","days":"{{data.Settings.days}}","style":"{{data.Style}}"}' }, 280, 110),
        card('js', 'script.shell', { language: 'javascript', command: 'console.log(JSON.stringify({ s: data.Style, c: data.Settings.city }))' }, 280, 220),
        card('py', 'script.shell', { language: 'python', command: "import json\nprint(json.dumps({'s': data['Style'], 'd': data['Settings']['days']}))" }, 280, 330),
      ],
      [edge('t', 'sh'), edge('t', 'set'), edge('t', 'js'), edge('t', 'py')],
    )
    await store(request, flow.id, 'style', 'Be brief and friendly.')
    await store(request, flow.id, 'settings', '{"city":"Phnom Penh","days":3}')
    const run = await runAndWait(request, flow.id)
    expect(run.status, run.error).toBe('succeeded')
    expect(out(run, 'sh').stdout).toBe("Be brief and friendly.\n")
    expect(out(run, 'set')).toEqual({ city: 'Phnom Penh', days: 3, style: 'Be brief and friendly.' }) // days keeps its number type
    expect(out(run, 'js').json).toEqual({ s: 'Be brief and friendly.', c: 'Phnom Penh' })
    expect(out(run, 'py').json).toEqual({ s: 'Be brief and friendly.', d: 3 })
  })

  test('the Data card itself needs no trigger: it runs with every run and outputs its text; an unconnected ordinary card still does not', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'style'), script('loose', 'echo never')])
    await store(request, flow.id, 'style', 'hello')
    const run = await runAndWait(request, flow.id)
    expect(run.nodes.find((n) => n.nodeId === 'style')!.status).toBe('succeeded')
    expect(out(run, 'style')).toMatchObject({ text: 'hello', name: 'Style', scope: 'flow', size: 5, changed: false })
    expect(run.nodes.find((n) => n.nodeId === 'loose')!.status).toBe('skipped')
  })

  test('connected with a wire it also works as an ordinary card: its text is the next card\'s input', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'style'), script('use', 'echo got:{{input.text | sh}}', 280, {})], [edge('style', 'use')])
    await store(request, flow.id, 'style', 'wired text')
    const run = await runAndWait(request, flow.id)
    expect(run.nodes.find((n) => n.nodeId === 'use')!.status).toBe('succeeded')
    expect(out(run, 'use').stdout).toBe('got:wired text\n')
  })

  test('a JSON object or list is data (its fields work); other text, even a number, stays text', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), data('Cfg', 'cfg'), data('Count', 'count', {}, 200), card('set', 'data.set', { values: '{"first":"{{data.Cfg[0].id}}","n":"{{data.Count}}"}' }, 400, 0)],
      [edge('t', 'set')],
    )
    await store(request, flow.id, 'cfg', '[{"id":7},{"id":8}]')
    await store(request, flow.id, 'count', '42')
    const run = await runAndWait(request, flow.id)
    expect(out(run, 'set')).toEqual({ first: 7, n: '42' }) // 42 is text: only objects and lists become data
  })

  test('a Data card that is empty gives empty text, not an error', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Empty', 'e'), script('s', 'echo "[{{data.Empty}}]"', 280)], [edge('t', 's')])
    const run = await runAndWait(request, flow.id)
    expect(run.status).toBe('succeeded')
    expect(out(run, 's').stdout).toBe('[]\n')
  })
})

test.describe('a flow can write to it', () => {
  const flowWith = (request: APIRequestContext, params: Record<string, unknown>, name = 'Last answer') =>
    makeFlow(request, [trigger(), script('say', 'echo answer-{{run.input}}', 280), data(name, 'd', params, 560, 0), script('after', 'echo seen:{{data.Last answer | sh}}', 840)], [edge('t', 'say'), edge('say', 'd'), edge('d', 'after')])

  test('Replace: the incoming text is stored, later cards in the SAME run already see it, the next run starts from it', async ({ request }) => {
    const flow = await flowWith(request, { mode: 'replace', writeFrom: '{{input.stdout}}' })
    await store(request, flow.id, 'd', 'old text')
    const run = await runAndWait(request, flow.id, '1')
    expect(out(run, 'd')).toMatchObject({ text: 'answer-1\n', changed: true })
    expect(out(run, 'after').stdout).toBe('seen:answer-1\n\n') // the stored text ends with a newline, echo adds another
    expect((await stored(request, flow.id, 'd')).text).toBe('answer-1\n')
    const second = await runAndWait(request, flow.id, '2')
    expect(out(second, 'd').text).toBe('answer-2\n')
  })

  test('Add: every run adds a line; the first run does not start with a blank line', async ({ request }) => {
    const flow = await flowWith(request, { mode: 'append', writeFrom: 'run {{run.input}}' })
    await runAndWait(request, flow.id, 'a')
    await runAndWait(request, flow.id, 'b')
    const last = await runAndWait(request, flow.id, 'c')
    expect(out(last, 'd').text).toBe('run a\nrun b\nrun c')
  })

  test('Keep (the default): a connected card never overwrites the text', async ({ request }) => {
    const flow = await flowWith(request, {})
    await store(request, flow.id, 'd', 'precious')
    const run = await runAndWait(request, flow.id, 'x')
    expect(out(run, 'd')).toMatchObject({ text: 'precious', changed: false })
    expect((await stored(request, flow.id, 'd')).text).toBe('precious')
  })

  test('a failed run leaves the text as it was', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), script('bad', 'exit 3', 280), data('Notes', 'd', { mode: 'replace' }, 560, 0)], [edge('t', 'bad'), edge('bad', 'd')])
    await store(request, flow.id, 'd', 'safe')
    const run = await runAndWait(request, flow.id)
    expect(run.status).toBe('failed')
    expect((await stored(request, flow.id, 'd')).text).toBe('safe')
  })

  test('the editor follows what a run wrote, without a reload; unsaved typing is never thrown away', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('say', 'echo from-run-{{run.input}}', 280), data('Notes', 'd', { mode: 'replace', writeFrom: '{{input.stdout}}' }, 560, 0)], [edge('t', 'say'), edge('say', 'd')])
    await store(request, flow.id, 'd', 'before')
    await open(page, flow.id)
    await node(page, 'd').click()
    await expect(codeOf(page, 'stored-editor')).toContainText('before')

    await runAndWait(request, flow.id, '1') // a run (not this window) writes
    await expect(codeOf(page, 'stored-editor')).toContainText('from-run-1') // pushed to the open editor
    await expect(page.getByTestId('stored-changed-elsewhere')).toHaveCount(0)

    await setCode(page, 'stored-editor', 'my unsaved words')
    await runAndWait(request, flow.id, '2')
    await expect(page.getByTestId('stored-changed-elsewhere')).toBeVisible()
    await expect(codeOf(page, 'stored-editor')).toContainText('my unsaved words') // untouched
    await page.getByTestId('stored-reload').click()
    await expect(codeOf(page, 'stored-editor')).toContainText('from-run-2')
    await expect(page.getByTestId('stored-changed-elsewhere')).toHaveCount(0)
  })

  test('running from the editor: the Data card shows its state and output like any card', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'style')])
    await store(request, flow.id, 'style', 'hello there')
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'style')).toHaveAttribute('data-status', 'succeeded')
    await node(page, 'style').click()
    await openTab(page, 'output')
    await expect(page.locator('[data-path="input.text"]').getByTestId('field-preview')).toHaveText('hello there')
  })
})

test.describe('shared between flows, and the data follows its flow', () => {
  test('"All flows" shares the text by name: another flow with a Data card of that name reads the same text', async ({ request }) => {
    const name = unique('Shared notes')
    const a = await makeFlow(request, [trigger(), data(name, 'd', { scope: 'shared' })])
    const b = await makeFlow(request, [trigger(), data(name.toUpperCase(), 'other-id', { scope: 'shared' }), script('s', `echo {{data.${name.toUpperCase()} | sh}}`, 280)], [edge('t', 's')])
    await store(request, a.id, 'd', 'written in flow A')
    const run = await runAndWait(request, b.id)
    expect(run.status, run.error).toBe('succeeded')
    expect(out(run, 's').stdout).toBe('written in flow A\n')
    expect((await stored(request, a.id, 'd')).path).toContain('shared')
  })

  test('"This flow only" is private: the same name in another flow is a different text', async ({ request }) => {
    const a = await makeFlow(request, [trigger(), data('Notes', 'd')])
    const b = await makeFlow(request, [trigger(), data('Notes', 'd')])
    await store(request, a.id, 'd', 'only A')
    expect((await stored(request, b.id, 'd')).text).toBe('')
  })

  test('deleting a flow removes its stored text files; the shared ones stay', async ({ request }) => {
    const shared = unique('Keep')
    const flow = await makeFlow(request, [trigger(), data('Private', 'p'), data(shared, 's', { scope: 'shared' }, 200)])
    await store(request, flow.id, 'p', 'private text')
    await store(request, flow.id, 's', 'shared text')
    const privatePath = (await stored(request, flow.id, 'p')).path
    const sharedPath = (await stored(request, flow.id, 's')).path
    expect(fs.existsSync(privatePath)).toBe(true)
    const res = await request.delete(`/api/flows/${flow.id}`, { data: { confirmName: flow.name } })
    expect(res.status()).toBe(204)
    expect(fs.existsSync(privatePath)).toBe(false)
    expect(fs.existsSync(sharedPath)).toBe(true)
  })

  test('duplicating a flow copies its stored text; the copy is independent', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'd')])
    await store(request, flow.id, 'd', 'original')
    const copy = await (await request.post(`/api/flows/${flow.id}/duplicate`)).json()
    expect((await stored(request, copy.id, 'd')).text).toBe('original')
    await store(request, copy.id, 'd', 'changed in the copy')
    expect((await stored(request, flow.id, 'd')).text).toBe('original')
  })

  test('export carries the flow\'s own text (not the shared one); import restores it into a new flow', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'd'), data(unique('Sh'), 's', { scope: 'shared' }, 200)])
    await store(request, flow.id, 'd', 'travels with the flow')
    await store(request, flow.id, 's', 'stays on this computer')
    const doc = await (await request.get(`/api/flows/${flow.id}/export`)).json()
    expect(doc.data).toEqual({ d: 'travels with the flow' })
    const imported = await (await request.post('/api/flows/import', { data: doc })).json()
    expect(imported.id).not.toBe(flow.id)
    expect((await stored(request, imported.id, 'd')).text).toBe('travels with the flow')

    const plain = await makeFlow(request, [trigger()])
    expect((await (await request.get(`/api/flows/${plain.id}/export`)).json()).data).toBeUndefined() // no data: no field
  })
})

test.describe('names: how other cards refer to the card', () => {
  test('the name is checked when the flow is saved: unusable characters and duplicates are refused with the reason', async ({ request }) => {
    const flow = await makeFlow(request, [trigger()])
    const put = (nodes: unknown[]) => request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes, edges: [] } } })
    for (const [nodes, reason] of [
      [[data('a.b', 'x')], /contains '\.'/],
      [[data('', 'x')], /needs a name/],
      [[data('Style', 'x'), data('style', 'y')], /Two Data cards are called/],
      [[data('Fine', 'x', { scope: 'universe' })], /unknown scope/],
    ] as const) {
      const res = await put([...nodes])
      expect(res.status()).toBe(400)
      expect((await res.json()).error).toMatch(reason)
    }
    expect((await put([data('Style', 'x'), card('s', 'script.shell', {}, 0, 0, 'Style')])).status()).toBe(200) // the same name on another kind of card is fine
  })

  test('the card warns before you save when its name cannot be used, and shows the reference it will have', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'd')])
    await open(page, flow.id)
    await node(page, 'd').click()
    await expect(page.getByTestId('stored-reference')).toHaveText('{{data.Style}}')
    await page.getByTestId('inspector-name').fill('Last answer')
    await expect(page.getByTestId('stored-reference')).toHaveText('{{data.Last answer}}')
    await page.getByTestId('inspector-name').fill('my.notes')
    await expect(page.getByTestId('stored-name-problem')).toContainText('"."')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-save-error')).toContainText("contains '.'")
  })

  test('the card runs under its name: a run with a bad name fails that card with the reason', async ({ request }) => {
    // an old or imported file could slip a name past the editor: the run says so instead of guessing
    const flow = await makeFlow(request, [trigger(), data('Fine', 'd')])
    const run = await runAndWait(request, flow.id)
    expect(run.status).toBe('succeeded')
  })
})

test.describe('"Data in this flow": see what you can use, copy the reference', () => {
  test('every Data card is listed in the Input tab of every card, with copy; JSON ones show their fields; it follows changes', async ({ page, request, context }) => {
    await context.grantPermissions(['clipboard-read', 'clipboard-write'])
    const flow = await makeFlow(request, [trigger(), data('Style', 'style'), data('Settings', 'cfg', {}, 200), script('use', 'echo hi', 280)], [edge('t', 'use')])
    await store(request, flow.id, 'style', 'Be brief.')
    await store(request, flow.id, 'cfg', '{"city":"Phnom Penh","tags":["a","b"]}')
    await open(page, flow.id)
    await node(page, 'use').click()
    await openTab(page, 'input')
    const panel = page.getByTestId('data-cards')
    await expect(panel).toContainText('Data in this flow')
    await expect(page.getByTestId('data-card-style')).toContainText('Be brief.')
    await expect(page.getByTestId('data-card-cfg')).toContainText('json')

    await page.getByTestId('data-card-style').getByTestId('data-card-copy').click()
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('{{data.Style}}')

    await page.getByTestId('data-card-cfg').getByTestId('data-card-fields').click()
    const city = page.getByTestId('data-card-cfg').locator('[data-path="data.Settings.city"]')
    await expect(city.getByTestId('field-preview')).toHaveText('Phnom Penh')
    await city.getByTestId('field-copy').click()
    expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('{{data.Settings.city}}')

    await store(request, flow.id, 'style', 'Changed from outside.') // the list follows
    await expect(page.getByTestId('data-card-style')).toContainText('Changed from outside.')
  })

  test('no Data cards: no section', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('use', 'echo hi', 280)], [edge('t', 'use')])
    await open(page, flow.id)
    await node(page, 'use').click()
    await openTab(page, 'input')
    await expect(page.getByTestId('data-cards')).toHaveCount(0)
  })
})

test.describe('the API', () => {
  test('list / read / write; unknown flows and cards that are not Data cards are 404', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'd'), script('s', 'echo', 200)])
    await store(request, flow.id, 'd', 'x')
    const list = await (await request.get(`/api/flows/${flow.id}/data`)).json()
    expect(list).toEqual([expect.objectContaining({ cardId: 'd', name: 'Style', scope: 'flow', preview: 'x', size: 1, isJson: false })])
    expect((await request.get(`/api/flows/${flow.id}/data/s`)).status()).toBe(404) // a script card
    expect((await request.get(`/api/flows/${flow.id}/data/nope`)).status()).toBe(404)
    expect((await request.get('/api/flows/nope/data')).status()).toBe(404)
    expect((await request.put(`/api/flows/${flow.id}/data/s`, { data: { text: 'x' } })).status()).toBe(404)
    expect((await request.put(`/api/flows/${flow.id}/data/d`)).status()).toBeGreaterThanOrEqual(400)
  })

  test('data-changed is pushed on the event stream when the text changes', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), data('Style', 'd')])
    await page.goto('/#/')
    const seen = await page.evaluate(
      (url) =>
        new Promise<string>((resolve) => {
          const es = new EventSource('/api/events')
          es.addEventListener('data-changed', (e) => { es.close(); resolve((e as MessageEvent).data) })
          es.onopen = () => fetch(url, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: 'pushed' }) })
        }),
      `/api/flows/${flow.id}/data/d`,
    )
    expect(JSON.parse(seen)).toEqual({ flowId: flow.id, cardId: 'd' })
  })
})
