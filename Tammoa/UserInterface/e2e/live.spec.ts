import { expect, test } from '@playwright/test'
import { card, edge, makeFlow, node, open, openTab, trigger, unique } from './helpers'
import { startFakeOllama } from './fake-ollama'

async function aiFlow(request: import('@playwright/test').APIRequestContext, fakeUrl: string, params: Record<string, unknown> = {}) {
  const conn = await (await request.post('/api/connections', { data: { name: unique('Fake'), provider: 'ollama', baseUrl: fakeUrl } })).json()
  return makeFlow(request, [trigger(), card('ai', 'ai.ollama', { connection: conn.id, model: 'tiny:1b', prompt: 'Say hi', think: 'on', ...params }, 280, 0, 'Ask')], [edge('t', 'ai')])
}

test.describe('the AI card shows what the model is writing, live', () => {
  test('thinking and answer appear while the model is still writing, in every tab; the final output replaces them', async ({ page, request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url)
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run').click()

      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'running')
      const live = page.getByTestId('inspector-live')
      await expect(live).toBeVisible()
      await expect(page.getByTestId('live-thinking')).toContainText('Let me think.')
      await expect(page.getByTestId('live-text')).toContainText('fake answer')
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'running') // still going: this is NOT the final output
      await expect(page.getByTestId('live-text-count')).toContainText('chars')

      // visible whichever tab is open
      await openTab(page, 'input')
      await expect(live).toBeVisible()
      await openTab(page, 'guide')
      await expect(live).toBeVisible()
      await openTab(page, 'settings')

      // the thinking can be folded away
      await page.getByTestId('live-thinking-toggle').click()
      await expect(page.getByTestId('live-thinking')).toHaveCount(0)
      await page.getByTestId('live-thinking-toggle').click()
      await expect(page.getByTestId('live-thinking')).toBeVisible()

      fake.release() // the model finishes
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'succeeded')
      await expect(live).toHaveCount(0) // no live view for a finished card
      await openTab(page, 'output')
      await expect(page.locator('[data-path="input.text"]').getByTestId('field-preview')).toHaveText('fake answer to: Say hi')
      await expect(page.locator('[data-path="input.thinking"]')).toBeVisible() // the full thinking is part of the output
    } finally {
      await fake.close()
    }
  })

  test('the answer grows between snapshots (it really streams, it is not one jump)', async ({ page, request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url, { think: 'off' })
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run').click()
      await expect(page.getByTestId('live-text')).toContainText('fake answer')
      const before = (await page.getByTestId('live-text').textContent())!.length
      expect(before).toBeLessThan('fake answer to: Say hi'.length) // only the first pieces so far
      await expect(page.getByTestId('live-thinking-section')).toHaveCount(0) // thinking off: no thinking section
      fake.release()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'succeeded')
    } finally {
      await fake.close()
    }
  })

  test('cancelling the run stops the stream: the card is cancelled, the live view is gone, the server sees the client leave', async ({ page, request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url)
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run').click()
      await expect(page.getByTestId('inspector-live')).toBeVisible()
      await page.locator('[data-testid^="run-cancel-"]').click()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'cancelled')
      await expect(page.getByTestId('inspector-live')).toHaveCount(0)
      await expect.poll(() => fake.aborted(), { timeout: 5000 }).toBeGreaterThanOrEqual(1)
      expect(fake.finished()).toBe(0)
    } finally {
      await fake.close()
    }
  })

  test('a model that goes silent stops the card after the chosen number of seconds, with a clear message', async ({ page, request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url, { timeoutSeconds: 1 })
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run').click()
      await expect(node(page, 'ai')).toHaveAttribute('data-status', 'failed', { timeout: 10_000 })
      await expect(page.getByTestId('inspector-error')).toContainText('was silent for 1 s')
      await expect(page.getByTestId('inspector-live')).toHaveCount(0)
    } finally {
      await fake.close()
    }
  })

  test('the API shows the live text while the card runs, and not after', async ({ request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url)
      const { id } = await (await request.post(`/api/flows/${flow.id}/runs`, { data: {} })).json()
      const ai = async () => (await (await request.get(`/api/runs/${id}`)).json()).nodes.find((n: { nodeId: string }) => n.nodeId === 'ai')
      await expect.poll(async () => (await ai()).live?.text ?? '', { timeout: 10_000 }).toContain('fake answer')
      expect((await ai()).status).toBe('running')
      expect((await ai()).live.thinking).toContain('Let me think.')
      fake.release()
      await expect.poll(async () => (await ai()).status).toBe('succeeded')
      const done = await ai()
      expect(done.live).toBeUndefined()
      expect(done.output.text).toBe('fake answer to: Say hi')
    } finally {
      await fake.close()
    }
  })

  test('two runs of the same flow stream independently', async ({ page, request }) => {
    const fake = await startFakeOllama({ hold: true })
    try {
      const flow = await aiFlow(request, fake.url, { prompt: 'Say {{run.input}}' })
      await open(page, flow.id)
      await node(page, 'ai').click()
      await page.getByTestId('run-input').fill('one')
      await page.getByTestId('run').click()
      await expect(page.getByTestId('live-text')).toContainText('fake answer')
      await page.getByTestId('run-input').fill('two')
      await page.getByTestId('run').click()
      await expect(page.locator('[data-testid^="run-row-"][data-status="running"]')).toHaveCount(2)
      fake.release()
      await expect(page.locator('[data-testid^="run-row-"][data-status="succeeded"]')).toHaveCount(2)
      const texts = await Promise.all((await (await request.get(`/api/flows/${flow.id}/runs`)).json()).map((r: any) => r.nodes.find((n: any) => n.nodeId === 'ai').output.text))
      expect(texts.sort()).toEqual(['fake answer to: Say one', 'fake answer to: Say two'])
    } finally {
      await fake.close()
    }
  })
})
