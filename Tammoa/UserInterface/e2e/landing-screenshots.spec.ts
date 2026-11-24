import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { test, type Page } from '@playwright/test'
import { card, edge, node, trigger } from './helpers'
import { startFakeOllama } from './fake-ollama'

/**
 * Makes the screenshots of the landing page (Landing/screenshots/*.png, light and dark).
 * NOT a regression test: it is skipped unless LANDING=1.
 *   LANDING=1 npx playwright test e2e/landing-screenshots.spec.ts --project=chromium
 * Needs the dev servers on an EMPTY, disposable data folder (TAMMOA_DATA_DIR): it seeds invented demo flows and a fake Ollama, never real data.
 * Output folder: env LANDING_OUT, else <repo root>/Landing/screenshots.
 */
test.skip(!process.env.LANDING, 'screenshots for the landing page only: run with LANDING=1')
test.describe.configure({ mode: 'serial' })

const OUT = process.env.LANDING_OUT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../../Landing/screenshots')
const VIEWPORT = { width: 1280, height: 800 }

test('landing page screenshots', async ({ browser, request }) => {
  test.setTimeout(180_000)
  fs.mkdirSync(OUT, { recursive: true })
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'tammoa-landing-'))
  fs.mkdirSync(path.join(work, 'notes'))
  const fake = await startFakeOllama({
    models: ['qwen3:14b', 'llama3.1:8b'],
    answer: (req) =>
      req.tools?.length
        ? 'Done. I saved the summary to notes/summary.md. The second write was outside the folder, so the rules refused it.'
        : 'You have three open items: call the printer company, plan the Friday demo and renew the domain. The demo is the most time-sensitive.',
    toolScript: [[
      { name: 'write_file', arguments: { path: 'notes/summary.md', content: '# Summary\n\n- Call the printer company\n- Plan the Friday demo\n- Renew the domain\n' } },
      { name: 'write_file', arguments: { path: '../outside.txt', content: 'x' } },
    ]],
  })

  try {
    const flows = (await (await request.get('/api/flows')).json()) as unknown[]
    if (flows.length) throw new Error('The data folder is not empty: start the backend with a fresh TAMMOA_DATA_DIR so only demo data is on the screenshots.')

    const conn = await (await request.post('/api/connections', { data: { name: 'Local Ollama', provider: 'ollama', baseUrl: fake.url } })).json()
    const model = 'qwen3:14b'

    async function seed(name: string, description: string, nodes: unknown[], edges: unknown[], enable = false) {
      const f = await (await request.post('/api/flows', { data: { name, description } })).json()
      const res = await request.put(`/api/flows/${f.id}`, { data: { name, description, definition: { schemaVersion: 1, nodes, edges } } })
      if (res.status() !== 200) throw new Error(await res.text())
      if (enable) await request.post(`/api/flows/${f.id}/enable`)
      return f.id as string
    }

    // 1) the hero flow: collect -> check -> summarize, with a "nothing to do" branch
    const briefing = await seed(
      'Morning briefing',
      'Collect my notes and summarize them with a local model.',
      [
        { ...trigger('start'), name: 'Start', position: { x: 20, y: 120 } },
        { ...card('collect', 'script.shell', { language: 'javascript', command: 'console.log(JSON.stringify({ count: 3, notes: ["Call the printer company", "Plan the Friday demo", "Renew the domain"] }))' }, 250, 120, 'Collect notes'), position: { x: 250, y: 120 } },
        { ...card('check', 'logic.if', { value1: '{{input.json.count}}', operator: 'greaterThan', value2: '0' }, 480, 120, 'Any notes?'), position: { x: 480, y: 120 } },
        { ...card('summary', 'ai.ollama', { connection: conn.id, model, prompt: 'Summarize these notes in two sentences:\n{{input.json.notes}}', think: 'off' }, 710, 40, 'Summarize'), position: { x: 710, y: 40 } },
        { ...card('none', 'data.set', { values: '{ "message": "Nothing to do today." }' }, 710, 230, 'Nothing to do'), position: { x: 710, y: 230 } },
      ],
      [edge('start', 'collect'), edge('collect', 'check'), edge('check', 'summary', 'true'), edge('check', 'none', 'false')],
      true,
    )

    // 2) an AI that acts inside one folder: one write allowed, one outside the folder refused by the rules
    const tidy = await seed(
      'Tidy the project notes',
      'An AI card that may write files, only inside one folder.',
      [
        { ...trigger('start'), name: 'Start', position: { x: 20, y: 100 } },
        { ...card('tidy', 'ai.ollama', { connection: conn.id, model, prompt: 'Write a short summary of the project notes to notes/summary.md.', think: 'off', tools: ['read_file', 'write_file'], toolRoot: work }, 300, 100, 'Tidy the notes'), position: { x: 300, y: 100 } },
      ],
      [edge('start', 'tidy')],
    )

    await seed(
      'Check an API',
      'Call a web API and branch on the answer.',
      [
        { ...trigger('start'), name: 'Start', position: { x: 20, y: 60 } },
        { ...card('call', 'net.http', { method: 'GET', url: 'https://api.example.com/status' }, 300, 60, 'Get status'), position: { x: 300, y: 60 } },
      ],
      [edge('start', 'call')],
      true,
    )
    await seed(
      'Copy notes to a backup file',
      'Read a text file and write it somewhere else.',
      [
        { ...trigger('start'), name: 'Start', position: { x: 20, y: 60 } },
        { ...card('read', 'file.read', { path: '~/notes/today.txt' }, 300, 60, 'Read notes'), position: { x: 300, y: 60 } },
        { ...card('write', 'file.write', { path: '~/backup/today.txt', content: '{{input.text}}' }, 580, 60, 'Write backup'), position: { x: 580, y: 60 } },
      ],
      [edge('start', 'read'), edge('read', 'write')],
    )

    // run the two flows once through the API so the screenshots show real results
    async function runAndWait(flowId: string, input?: unknown) {
      const started = await request.post(`/api/flows/${flowId}/runs`, { data: { input } })
      const { id } = await started.json()
      for (let i = 0; i < 60; i++) {
        const run = await (await request.get(`/api/runs/${id}`)).json()
        if (run.status !== 'running') return run
        await new Promise((r) => setTimeout(r, 500))
      }
      throw new Error('a demo run did not finish')
    }
    const first = await runAndWait(briefing)
    if (first.status !== 'succeeded') throw new Error(`the demo run failed: ${JSON.stringify(first.nodes)}`)
    await runAndWait(tidy)

    await request.put('/api/agent/access', { data: { enabled: true } })

    async function shoot(theme: 'light' | 'dark') {
      const context = await browser.newContext({ viewport: VIEWPORT, deviceScaleFactor: 1, colorScheme: theme })
      await context.addInitScript((t) => localStorage.setItem('tammoa-theme', t), theme)
      const page = await context.newPage()
      const save = (name: string) => page.screenshot({ path: path.join(OUT, `${name}-${theme}.png`) })

      // flows list
      await page.goto('/#/')
      await page.getByTestId('flow-list').waitFor()
      await save('flows')

      // the editor after a run: click the AI card to show what it produced
      await open(page, briefing)
      await page.getByTestId('zoom-reset').click()
      await page.getByTestId('zoom-out').click()
      await node(page, 'summary').click()
      await page.getByTestId('inspector-tab-output').click()
      await page.waitForTimeout(400)
      await save('editor')

      // the AI card that acted: its tool calls, ran and refused
      await open(page, tidy)
      await node(page, 'tidy').click()
      await page.getByTestId('inspector-tab-output').click()
      await page.waitForTimeout(400)
      await save('ai-tools')

      // the templates
      await page.goto('/#/')
      await page.getByTestId('from-template').click()
      await page.getByTestId('template-summarize-text').click()
      await page.getByTestId('template-model').selectOption('qwen3:14b')
      await page.waitForTimeout(300)
      await save('templates')

      // agent access
      await page.goto('/#/agent')
      await page.getByTestId('agent-token').waitFor()
      await page.waitForTimeout(300)
      await save('agent')
      await context.close()
    }

    async function open(page: Page, id: string) {
      await page.goto(`/#/flows/${id}`)
      await page.getByTestId('canvas').waitFor()
      await page.waitForTimeout(500)
    }

    await shoot('light')
    await shoot('dark')
    await request.put('/api/agent/access', { data: { enabled: false } })
  } finally {
    await fake.close()
    fs.rmSync(work, { recursive: true, force: true })
  }
})
