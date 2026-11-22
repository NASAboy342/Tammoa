import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type APIRequestContext } from '@playwright/test'
import { card, edge, makeFlow, node, open, openTab, runAndWait, trigger, unique } from './helpers'
import { startFakeOllama } from './fake-ollama'

async function aiFlow(request: APIRequestContext, fakeUrl: string, params: Record<string, unknown> = {}) {
  const conn = await (await request.post('/api/connections', { data: { name: unique('Fake'), provider: 'ollama', baseUrl: fakeUrl } })).json()
  return makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, model: 'tiny:1b', prompt: 'Write the note', think: 'off', ...params }, 280, 0, 'Ask')], [edge('t', 'ai')])
}

const option = (page: import('@playwright/test').Page, tool: string) => page.getByTestId(`param-option-tools-${tool}`)

test.describe('AI tools: settings', () => {
  test('tick tools: the matching settings appear, the warning shows for scripts, the choice is saved', async ({ page, request }) => {
    const fake = await startFakeOllama()
    try {
      const flow = await aiFlow(request, fake.url)
      await open(page, flow.id)
      await node(page, 'ai').click()

      // nothing ticked: the four tools are offered, no tool setting is shown
      for (const t of ['read_file', 'write_file', 'http_request', 'run_script']) await expect(option(page, t)).toHaveAttribute('aria-checked', 'false')
      for (const k of ['toolRoot', 'allowedHosts', 'maxSteps', 'approverModel']) await expect(page.getByTestId(`param-${k}`)).toHaveCount(0)

      await option(page, 'read_file').click()
      await expect(option(page, 'read_file')).toHaveAttribute('aria-checked', 'true')
      await expect(page.getByTestId('param-toolRoot')).toBeVisible()
      await expect(page.getByTestId('param-maxSteps')).toBeVisible()
      await expect(page.getByTestId('param-approverModel')).toBeVisible()
      await expect(page.getByTestId('param-allowedHosts')).toHaveCount(0)

      await option(page, 'http_request').click()
      await expect(page.getByTestId('param-allowedHosts')).toBeVisible()
      await option(page, 'read_file').click() // un-tick: the folder is not needed any more
      await expect(page.getByTestId('param-toolRoot')).toHaveCount(0)

      await option(page, 'run_script').click()
      await expect(page.getByTestId('param-warning-run_script')).toContainText('not a sandbox')
      await expect(page.getByTestId('param-toolRoot')).toBeVisible()

      await page.getByTestId('param-input-toolRoot').fill('/tmp/tammoa-e2e-root')
      await page.getByTestId('param-input-allowedHosts').fill('api.example.com')
      await page.getByTestId('edit-save').click()
      await expect(page.getByTestId('edit-save')).toBeDisabled()

      await page.reload()
      await node(page, 'ai').click()
      await expect(option(page, 'http_request')).toHaveAttribute('aria-checked', 'true')
      await expect(option(page, 'run_script')).toHaveAttribute('aria-checked', 'true')
      await expect(option(page, 'read_file')).toHaveAttribute('aria-checked', 'false')
      await expect(page.getByTestId('param-input-toolRoot')).toHaveValue('/tmp/tammoa-e2e-root')
      await expect(page.getByTestId('param-input-allowedHosts')).toHaveValue('api.example.com')

      // the saved definition holds a plain list of tool ids
      const saved = await (await request.get(`/api/flows/${flow.id}`)).json()
      expect(saved.definition.nodes.find((n: any) => n.id === 'ai').params.tools).toEqual(['http_request', 'run_script'])
    } finally {
      await fake.close()
    }
  })

  test('the options can be ticked with the keyboard', async ({ page, request }) => {
    const fake = await startFakeOllama()
    try {
      const flow = await aiFlow(request, fake.url)
      await open(page, flow.id)
      await node(page, 'ai').click()
      await option(page, 'write_file').focus()
      await page.keyboard.press('Space')
      await expect(option(page, 'write_file')).toHaveAttribute('aria-checked', 'true')
      await page.keyboard.press('Space')
      await expect(option(page, 'write_file')).toHaveAttribute('aria-checked', 'false')
    } finally {
      await fake.close()
    }
  })
})

test.describe('AI tools: a run', () => {
  test('a write inside the folder runs, one outside is refused: both are listed (live, then in the output); the file is only written inside', async ({ page, request }) => {
    const base = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-e2e-tools-'))
    const root = path.join(base, 'root')
    fs.mkdirSync(root)
    const fake = await startFakeOllama({
      hold: true,
      toolScript: [[
        { name: 'write_file', arguments: { path: 'note.txt', content: 'hello from the model' } },
        { name: 'write_file', arguments: { path: '../escape.txt', content: 'should never exist' } },
      ]],
    })
    try {
      const flow = await aiFlow(request, fake.url, { tools: ['write_file'], toolRoot: root })
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run').click()

      // the model is still writing its final answer (held): the calls made so far are already visible
      await expect(page.getByTestId('live-tool-calls')).toBeVisible()
      await expect(page.getByTestId('live-tool-calls').locator('[data-decision="ran"]')).toHaveCount(1)
      await expect(page.getByTestId('live-tool-calls').locator('[data-decision="rejected"]')).toHaveCount(1)
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'running')

      fake.release()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'succeeded')
      await openTab(page, 'output')
      const list = page.getByTestId('output-tool-calls')
      await expect(list).toBeVisible()
      await expect(page.getByTestId('tool-calls-summary')).toContainText('2 calls: 1 ran, 1 refused')
      await expect(list.getByTestId('tool-call-0')).toHaveAttribute('data-decision', 'ran')
      await expect(list.getByTestId('tool-call-1')).toHaveAttribute('data-decision', 'rejected')
      await expect(list.getByTestId('tool-call-decision-1')).toContainText('Refused')

      await list.getByTestId('tool-call-toggle-1').click()
      await expect(list.getByTestId('tool-call-reason-1')).toContainText('outside the allowed folder')
      await list.getByTestId('tool-call-toggle-0').click()
      await expect(list.getByTestId('tool-call-detail-0')).toContainText('Wrote')

      // the real effect: written inside, nothing outside
      expect(fs.readFileSync(path.join(root, 'note.txt'), 'utf8')).toBe('hello from the model')
      expect(fs.existsSync(path.join(base, 'escape.txt'))).toBe(false)
      // the fields view still has the full output (steps, text)
      await expect(page.locator('[data-path="input.steps"]')).toBeVisible()
    } finally {
      await fake.close()
      fs.rmSync(base, { recursive: true, force: true })
    }
  })

  test('a card with no tools has no tool list', async ({ page, request }) => {
    const fake = await startFakeOllama()
    try {
      const flow = await aiFlow(request, fake.url)
      const run = await runAndWait(request, flow.id)
      expect(run.status).toBe('succeeded')
      await open(page, flow.id)
      await node(page, 'ai').click()
      await openTab(page, 'output')
      await expect(page.locator('[data-path="input.text"]')).toBeVisible()
      await expect(page.getByTestId('output-tool-calls')).toHaveCount(0)
    } finally {
      await fake.close()
    }
  })
})
