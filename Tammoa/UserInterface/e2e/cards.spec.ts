import os from 'node:os'
import path from 'node:path'
import fs from 'node:fs'
import { expect, test } from '@playwright/test'
import { card, codeOf, edge, makeFlow, node, open, outputOf, runAndWait, setCode, setv, showJson, trigger, unique } from './helpers'
import { startFakeOllama } from './fake-ollama'

test.describe('the new cards, end to end through the backend engine', () => {
  test('wait → HTTP request → set → write file → read file', async ({ request }) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-e2e-'))
    const file = path.join(dir, 'nested', 'version.txt')
    try {
      const flow = await makeFlow(
        request,
        [
          trigger(),
          card('w', 'flow.wait', { seconds: 0.1 }, 200),
          card('h', 'net.http', { method: 'GET', url: 'http://localhost:8000/api/app/version' }, 400),
          setv('s', '{"line":"v={{input.json.version}}"}', 600),
          card('wf', 'file.write', { path: file, content: '{{input.line}}\n' }, 800),
          card('rf', 'file.read', { path: '{{input.path}}' }, 1000),
        ],
        [edge('t', 'w'), edge('w', 'h'), edge('h', 's'), edge('s', 'wf'), edge('wf', 'rf')],
      )
      const run = await runAndWait(request, flow.id)
      expect(run.status, run.error).toBe('succeeded')
      expect(outputOf(run, 'h')).toMatchObject({ status: 200, ok: true, json: { name: 'Tammoa' } })
      expect(outputOf(run, 'wf')).toMatchObject({ path: file, mode: 'overwrite' })
      expect(outputOf(run, 'rf').text).toBe('v=0.1.0\n')
      expect(fs.readFileSync(file, 'utf8')).toBe('v=0.1.0\n')
    } finally {
      fs.rmSync(dir, { recursive: true, force: true })
    }
  })

  test('reading a missing file gives empty text; the switch creates the file and its folders instead; the switch works in the editor', async ({ page, request }) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-e2e-'))
    const file = path.join(dir, 'not', 'there.txt')
    try {
      const flow = await makeFlow(request, [trigger(), card('rf', 'file.read', { path: file }, 280, 0, 'Read')], [edge('t', 'rf')])
      const first = await runAndWait(request, flow.id, 'hello')
      expect(first.status, first.error).toBe('succeeded') // no "File not found" error any more
      expect(outputOf(first, 'rf')).toEqual({ path: file, text: '', size: 0, exists: false, input: { input: 'hello' } }) // the input it received is carried along
      expect(fs.existsSync(path.join(dir, 'not'))).toBe(false) // and nothing was created

      await open(page, flow.id)
      await node(page, 'rf').click()
      const toggle = page.getByTestId('param-input-createIfMissing')
      await expect(toggle).toHaveAttribute('aria-checked', 'false')
      await expect(toggle).toContainText('Off')
      await toggle.click()
      await expect(toggle).toHaveAttribute('aria-checked', 'true')
      await page.getByTestId('edit-save').click()
      await expect(page.getByTestId('edit-saved')).toBeVisible()

      const second = await runAndWait(request, flow.id)
      expect(second.status, second.error).toBe('succeeded')
      expect(outputOf(second, 'rf')).toMatchObject({ text: '', exists: false })
      expect(fs.readFileSync(file, 'utf8')).toBe('') // the empty file (and its folders) now exist

      fs.writeFileSync(file, 'kept')
      const third = await runAndWait(request, flow.id)
      expect(outputOf(third, 'rf')).toMatchObject({ text: 'kept', exists: true })
      expect(fs.readFileSync(file, 'utf8')).toBe('kept') // an existing file is never touched
    } finally {
      fs.rmSync(dir, { recursive: true, force: true })
    }
  })

  test('the read file output carries the input it received, so the next card can use both', async ({ request }) => {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-e2e-'))
    const file = path.join(dir, 'note.txt')
    fs.writeFileSync(file, 'file text')
    try {
      const flow = await makeFlow(
        request,
        [trigger(), setv('s', JSON.stringify({ path: file, who: 'Dara' }), 200), card('rf', 'file.read', { path: '{{input.path}}' }, 400), setv('u', '{"line":"{{input.input.who}} read {{input.text}}"}', 600)],
        [edge('t', 's'), edge('s', 'rf'), edge('rf', 'u')],
      )
      const run = await runAndWait(request, flow.id)
      expect(run.status, run.error).toBe('succeeded')
      expect(outputOf(run, 'rf')).toMatchObject({ text: 'file text', exists: true, input: { path: file, who: 'Dara' } })
      expect(outputOf(run, 'u')).toEqual({ line: 'Dara read file text' })
    } finally {
      fs.rmSync(dir, { recursive: true, force: true })
    }
  })

  test('two branches merged, then stringified as JSON', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), setv('a', '{"a":1}', 200), { ...setv('b', '{"b":2}', 200), position: { x: 200, y: 120 } }, card('m', 'data.merge', {}, 420), card('j', 'data.json', { mode: 'stringify', source: '{{input}}' }, 640)],
      [edge('t', 'a'), edge('t', 'b'), edge('a', 'm'), edge('b', 'm'), edge('m', 'j')],
    )
    const run = await runAndWait(request, flow.id)
    expect(run.status, run.error).toBe('succeeded')
    expect(outputOf(run, 'm')).toEqual({ a: 1, b: 2 })
    expect(outputOf(run, 'j').text).toBe('{"a":1,"b":2}')
  })

  test('JSON parse of a script result, then IF on a field', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [
        trigger(),
        card('sc', 'script.shell', { command: 'echo \'{"status":"ok","n":3}\'' }, 200),
        card('p', 'data.json', { mode: 'parse', source: '{{input.stdout}}' }, 400),
        card('i', 'logic.if', { value1: '{{input.status}}', operator: 'equals', value2: 'ok' }, 600),
        card('good', 'data.set', { values: '{"verdict":"good {{input.n}}"}' }, 800),
        card('bad', 'data.set', { values: '{"verdict":"bad"}' }, 800, 120),
      ],
      [edge('t', 'sc'), edge('sc', 'p'), edge('p', 'i'), edge('i', 'good', 'true'), edge('i', 'bad', 'false')],
    )
    const run = await runAndWait(request, flow.id)
    expect(outputOf(run, 'good')).toEqual({ verdict: 'good 3' })
    expect(run.nodes.find((n) => n.nodeId === 'bad')!.status).toBe('skipped')
  })

  test('an HTTP error status fails the card unless told not to', async ({ request }) => {
    const failing = await makeFlow(request, [trigger(), card('h', 'net.http', { url: 'http://localhost:8000/api/flows/does-not-exist' })], [edge('t', 'h')])
    const run = await runAndWait(request, failing.id)
    expect(run.status).toBe('failed')
    expect(run.nodes.find((n) => n.nodeId === 'h')!.error).toMatch(/HTTP 404/)

    const lenient = await makeFlow(request, [trigger(), card('h', 'net.http', { url: 'http://localhost:8000/api/flows/does-not-exist', failOnError: false })], [edge('t', 'h')])
    const ok = await runAndWait(request, lenient.id)
    expect(ok.status).toBe('succeeded')
    expect(outputOf(ok, 'h')).toMatchObject({ status: 404, ok: false })
  })

  test('bad configuration is a clear card error, not a crash', async ({ request }) => {
    const flow = await makeFlow(
      request,
      [trigger(), card('w', 'flow.wait', { seconds: 99999 }, 200), card('f', 'file.read', { path: 'relative.txt' }, 200, 100), card('u', 'net.http', { url: 'ftp://x' }, 200, 200)],
      [edge('t', 'w'), edge('t', 'f'), edge('t', 'u')],
    )
    const run = await runAndWait(request, flow.id)
    const errors = Object.fromEntries(run.nodes.map((n) => [n.nodeId, n.error]))
    expect(errors.w).toMatch(/between 0 and 3600/)
    expect(errors.f).toMatch(/absolute path/)
    expect(errors.u).toMatch(/http\(s\)/)
  })

  test('the Wait card keeps a run running and a cancel stops it at once', async ({ request }) => {
    const flow = await makeFlow(request, [trigger(), card('w', 'flow.wait', { seconds: 60 })], [edge('t', 'w')])
    const { id } = await (await request.post(`/api/flows/${flow.id}/runs`, { data: {} })).json()
    await expect.poll(async () => (await (await request.get(`/api/runs/${id}`)).json()).nodes.find((n: any) => n.nodeId === 'w').status).toBe('running')
    const started = Date.now()
    await request.post(`/api/runs/${id}/cancel`)
    await expect.poll(async () => (await (await request.get(`/api/runs/${id}`)).json()).status).toBe('cancelled')
    expect(Date.now() - started).toBeLessThan(5000)
  })
})

test.describe('connections', () => {
  test('create, test, edit (the key is kept unless changed) and remove a key; the key is never shown', async ({ page, request }) => {
    const fake = await startFakeOllama({ key: 'sekret' })
    try {
      const name = unique('Fake')
      await page.goto('/#/connections')
      await expect(page.getByTestId('connections')).toBeVisible()
      await page.getByTestId('new-connection').click()
      await expect(page.getByTestId('conn-url')).toHaveValue('http://localhost:11434') // the provider's default address
      await expect(page.getByTestId('conn-save')).toBeDisabled()
      await page.getByTestId('conn-name').fill(name)
      await page.getByTestId('conn-url').fill('not a url')
      await expect(page.getByTestId('conn-save')).toBeDisabled()
      await page.getByTestId('conn-url').fill(fake.url + '/')
      await page.getByTestId('conn-key').fill('sekret')
      await expect(page.getByTestId('conn-key')).toHaveAttribute('type', 'password')
      await page.getByTestId('conn-save').click()

      const row = page.locator('[data-testid^="connection-row-"]', { hasText: name })
      await expect(row).toBeVisible()
      await expect(row.getByTestId('connection-key-state')).toHaveText('Key saved')
      await expect(row.locator('[data-testid^="connection-result-"]')).toContainText('Connected: Ollama 0.0.1-fake · 2 models') // tested right after creating
      await expect(row.locator('[data-testid^="connection-result-"]')).toContainText('tiny:1b')
      await expect(page.locator('body')).not.toContainText('sekret')
      expect(JSON.stringify(await (await request.get('/api/connections')).json())).not.toContain('sekret')
      expect(fake.seen.every((s) => s.authorization === 'Bearer sekret')).toBe(true)

      // edit: rename, leave the key empty → the saved key is kept (the server still accepts us)
      await row.locator('[data-testid^="connection-edit-"]').click()
      await expect(page.getByTestId('conn-key')).toHaveAttribute('placeholder', /A key is saved/)
      await page.getByTestId('conn-name').fill(name + ' renamed')
      await page.getByTestId('conn-save').click()
      const renamed = page.locator('[data-testid^="connection-row-"]', { hasText: name + ' renamed' })
      await expect(renamed).toBeVisible()
      await renamed.locator('[data-testid^="connection-test-"]').click()
      await expect(renamed.locator('[data-testid^="connection-result-"]')).toContainText('Connected')

      // remove the saved key → the server now refuses, and the test says so
      await renamed.locator('[data-testid^="connection-edit-"]').click()
      await page.getByTestId('conn-clear-key').click()
      await expect(page.getByTestId('conn-key')).toBeDisabled()
      await page.getByTestId('conn-save').click()
      await expect(renamed.getByTestId('connection-key-state')).toHaveText('No key')
      await renamed.locator('[data-testid^="connection-test-"]').click()
      await expect(renamed.locator('[data-testid^="connection-result-"]')).toContainText('API key')
    } finally {
      await fake.close()
    }
  })

  test('an unreachable server is reported, not hidden', async ({ page }) => {
    await page.goto('/#/connections')
    await page.getByTestId('new-connection').click()
    const name = unique('Dead')
    await page.getByTestId('conn-name').fill(name)
    await page.getByTestId('conn-url').fill('http://127.0.0.1:1')
    await page.getByTestId('conn-save').click()
    const row = page.locator('[data-testid^="connection-row-"]', { hasText: name })
    await expect(row.locator('[data-testid^="connection-result-"]')).toContainText('Could not reach Ollama at http://127.0.0.1:1')
  })

  test('deleting asks first, and is refused while a card still uses the connection', async ({ page, request }) => {
    const created = await (await request.post('/api/connections', { data: { name: unique('Used'), provider: 'ollama', baseUrl: 'http://127.0.0.1:1' } })).json()
    const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: created.id, model: 'm', prompt: 'p' })], [edge('t', 'ai')])

    await page.goto('/#/connections')
    const row = page.getByTestId(`connection-row-${created.id}`)
    await row.getByTestId(`connection-delete-${created.id}`).click()
    await expect(page.getByTestId('confirm-dialog')).toBeVisible()
    await page.getByTestId('confirm-cancel').click()
    await expect(row).toBeVisible()

    await row.getByTestId(`connection-delete-${created.id}`).click()
    await page.getByTestId('confirm-ok').click()
    await expect(page.getByTestId('confirm-error')).toContainText(flow.name) // names the flow that uses it
    await expect(row).toBeVisible()
    await page.getByTestId('confirm-cancel').click()

    await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes: [], edges: [] } } })
    await row.getByTestId(`connection-delete-${created.id}`).click()
    await page.getByTestId('confirm-ok').click()
    await expect(row).toHaveCount(0)
    expect((await request.get(`/api/connections/${created.id}`)).status()).toBe(404)
  })

  test('the API: validation, 404, and a rename never loses the key', async ({ request }) => {
    expect((await request.post('/api/connections', { data: { name: '', provider: 'ollama', baseUrl: 'http://x' } })).status()).toBe(400)
    expect((await request.post('/api/connections', { data: { name: 'x', provider: 'ollama', baseUrl: 'ftp://x' } })).status()).toBe(400)
    expect((await request.post('/api/connections', { data: { name: 'x', provider: 'nope', baseUrl: 'http://x' } })).status()).toBeGreaterThanOrEqual(400)
    expect((await request.get('/api/connections/nope')).status()).toBe(404)
    expect((await request.post('/api/connections/nope/test')).status()).toBe(404)
    expect((await request.delete('/api/connections/nope')).status()).toBe(404)
    const providers = await (await request.get('/api/connections/providers')).json()
    expect(providers).toEqual([expect.objectContaining({ provider: 'ollama', defaultBaseUrl: 'http://localhost:11434', keyRequired: false })])

    const fake = await startFakeOllama({ key: 'k1' })
    try {
      const c = await (await request.post('/api/connections', { data: { name: unique('Keep'), provider: 'ollama', baseUrl: fake.url, apiKey: 'k1' } })).json()
      expect(c.hasKey).toBe(true)
      expect(c.apiKey).toBeUndefined()
      const renamed = await (await request.put(`/api/connections/${c.id}`, { data: { name: 'Renamed', provider: 'ollama', baseUrl: fake.url } })).json()
      expect(renamed.hasKey).toBe(true)
      expect((await (await request.post(`/api/connections/${c.id}/test`)).json()).ok).toBe(true)
      await request.put(`/api/connections/${c.id}`, { data: { name: 'Renamed', provider: 'ollama', baseUrl: fake.url, clearApiKey: true } })
      expect((await (await request.post(`/api/connections/${c.id}/test`)).json()).ok).toBe(false)
      await request.delete(`/api/connections/${c.id}`)
    } finally {
      await fake.close()
    }
  })
})

test.describe('the Ollama card', () => {
  test('pick a connection, load its models, choose one, write a prompt, run: the answer arrives and the server saw the right request', async ({ page, request }) => {
    const fake = await startFakeOllama({ key: 'sekret' })
    try {
      const conn = await (await request.post('/api/connections', { data: { name: unique('Fake'), provider: 'ollama', baseUrl: fake.url, apiKey: 'sekret' } })).json()
      const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', {}, 280, 0, 'Ask')], [edge('t', 'ai')])
      await open(page, flow.id)
      await node(page, 'ai').click()

      const model = page.getByTestId('param-input-model')
      await expect(model).toBeDisabled() // nothing to list until a connection is chosen
      await page.getByTestId('param-input-connection').selectOption({ label: conn.name })
      await expect(model.locator('option')).toHaveCount(3, { timeout: 10_000 }) // "Choose…" + the two models of the fake server
      await expect(model).toBeEnabled()
      await model.selectOption('tiny:1b')
      await setCode(page, 'param-input-prompt', 'Say: {{run.input}}')
      await page.getByTestId('param-input-think').selectOption('off')
      await page.getByTestId('param-input-temperature').fill('0.2')
      await page.getByTestId('edit-save').click()
      await expect(page.getByTestId('edit-saved')).toBeVisible()

      await page.getByTestId('run-input').fill('hi')
      await page.getByTestId('run').click()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'succeeded')
      await showJson(page, 'output')
      await expect(page.getByTestId('output-preview')).toContainText('"text": "fake answer to: Say: hi"')
      await expect(page.getByTestId('output-preview')).toContainText('"promptTokens": 4')

      const chat = fake.chats().at(-1)!
      expect(chat.authorization).toBe('Bearer sekret')
      expect(chat.body).toMatchObject({ model: 'tiny:1b', stream: true, think: false, options: { temperature: 0.2 } })
      expect(chat.body.messages.at(-1)).toEqual({ role: 'user', content: 'Say: hi' })
      expect(chat.body.format).toBeUndefined()
      await expect(page.locator('body')).not.toContainText('sekret')
    } finally {
      await fake.close()
    }
  })

  test('the model list can be reloaded, and a saved model that is gone stays visible but flagged', async ({ page, request }) => {
    const fake = await startFakeOllama({ models: ['a:1'] })
    try {
      const conn = await (await request.post('/api/connections', { data: { name: unique('Fake'), provider: 'ollama', baseUrl: fake.url } })).json()
      const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, model: 'removed:9', prompt: 'p' }, 280, 0, 'Ask')], [edge('t', 'ai')])
      await open(page, flow.id)
      await node(page, 'ai').click()
      const model = page.getByTestId('param-input-model')
      await expect(model.locator('option')).toHaveCount(3) // Choose… + a:1 + the saved one, flagged
      await expect(model.locator('option', { hasText: 'removed:9 (not available)' })).toHaveCount(1)
      await expect(model).toHaveValue('removed:9')
      const before = fake.seen.filter((s) => s.path === '/api/tags').length
      await page.getByTestId('param-reload-model').click()
      await expect.poll(() => fake.seen.filter((s) => s.path === '/api/tags').length).toBeGreaterThan(before)
    } finally {
      await fake.close()
    }
  })

  test('JSON mode asks for JSON and exposes it as { json }; an unknown model is explained', async ({ page, request }) => {
    const fake = await startFakeOllama()
    try {
      const conn = await (await request.post('/api/connections', { data: { name: unique('Fake'), provider: 'ollama', baseUrl: fake.url } })).json()
      const ask = (params: Record<string, unknown>) => makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, prompt: '{"x":1}', ...params })], [edge('t', 'ai')])

      const json = await runAndWait(request, (await ask({ model: 'tiny:1b', jsonMode: true })).id)
      expect(json.status).toBe('failed') // the fake answers with prose, so JSON mode must say so instead of passing it on
      expect(json.nodes.find((n) => n.nodeId === 'ai')!.error).toMatch(/valid JSON/)
      expect(fake.chats().at(-1)!.body.format).toBe('json')

      const missing = await runAndWait(request, (await ask({ model: 'missing' })).id)
      expect(missing.nodes.find((n) => n.nodeId === 'ai')!.error).toMatch(/model 'missing' not found/)

      const flow = await ask({ model: 'missing' })
      await open(page, flow.id)
      await page.getByTestId('run').click()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'failed')
      await node(page, 'ai').click()
      await expect(page.getByTestId('inspector-error')).toContainText("model 'missing' not found")
    } finally {
      await fake.close()
    }
  })

  test('an unreachable server fails the card with where it tried, and the model list says why it is empty', async ({ page, request }) => {
    const conn = await (await request.post('/api/connections', { data: { name: unique('Dead'), provider: 'ollama', baseUrl: 'http://127.0.0.1:1' } })).json()
    const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, model: 'm', prompt: 'p' }, 280, 0, 'Ask')], [edge('t', 'ai')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'ai')).toHaveAttribute('data-status', 'failed')
    await node(page, 'ai').click()
    await expect(page.getByTestId('inspector-error')).toContainText('Could not reach Ollama at http://127.0.0.1:1')
    await expect(page.getByTestId('param-error-model')).toContainText('Could not reach Ollama')
  })

  test('a deleted connection is flagged on the card and the run explains it', async ({ page, request }) => {
    const conn = await (await request.post('/api/connections', { data: { name: unique('Gone'), provider: 'ollama', baseUrl: 'http://127.0.0.1:1' } })).json()
    const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, model: 'm', prompt: 'p' }, 280, 0, 'Ask')], [edge('t', 'ai')])
    // sneak past the "in use" guard the way an import or an older file could: rewrite the flow, delete, restore
    const def = (await (await request.get(`/api/flows/${flow.id}`)).json()).definition
    await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { nodes: [], edges: [] } } })
    await request.delete(`/api/connections/${conn.id}`)
    await request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: def } })

    await open(page, flow.id)
    await node(page, 'ai').click()
    await expect(page.getByTestId('param-input-connection').locator('option', { hasText: '(deleted connection)' })).toHaveCount(1)
    await page.getByTestId('run').click()
    await expect(node(page, 'ai')).toHaveAttribute('data-status', 'failed')
    await expect(page.getByTestId('inspector-error')).toContainText('no longer exists')
  })

  test('a card that needs a connection links to the Connections page', async ({ page, request }) => {
    const flow = await makeFlow(request, [card('ai', 'ai.ollama', {}, 0, 0, 'Ask')])
    await open(page, flow.id)
    await node(page, 'ai').click()
    await page.getByTestId('manage-connections').click()
    await expect(page.getByTestId('connections')).toBeVisible()
    await expect(page).toHaveURL(/#\/connections$/)
    await expect(page.getByTestId('nav-connections')).toHaveClass(/border-ink/)
  })
})

test.describe('JSON code fields', () => {
  test('Set value has a JSON editor; invalid JSON is reported by the card, valid JSON works', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('s', 'data.set', { values: '{"a":1}' })], [edge('t', 's')])
    await open(page, flow.id)
    await node(page, 's').click()
    await expect(page.getByTestId('param-input-values').getByTestId('code-language')).toHaveText('JSON')
    await setCode(page, 'param-input-values', '{ not json')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run').click()
    await expect(node(page, 's')).toHaveAttribute('data-status', 'failed')
    await expect(page.getByTestId('inspector-error')).toContainText('not valid JSON')

    await setCode(page, 'param-input-values', '{ "fixed": true }')
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-saved')).toBeVisible()
    await page.getByTestId('run').click()
    await expect(page.locator('[data-testid^="run-row-"][data-status="succeeded"]')).toHaveCount(1)
    await expect(codeOf(page, 'param-input-values')).toContainText('"fixed": true')
  })
})
