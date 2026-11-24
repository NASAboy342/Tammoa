import { expect, test } from '@playwright/test'
import { card, edge, makeFlow, node, open, script, trigger } from './helpers'

/**
 * Every way things go wrong must be visible and readable, never a blank card or a frozen button.
 * Already covered elsewhere (cards.spec / live.spec / editor.spec): a dead Ollama address (connection test and card), an unknown model,
 * a deleted connection, an unreadable JSON answer, a silent model, a missing interpreter, a script syntax error. Here: the rest.
 */
test.describe('failures are visible', () => {
  test('a script that runs too long fails its card with the time limit in the message', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('slow', 'sleep 5', 280, { timeoutSeconds: 1 })], [edge('t', 'slow')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'slow')).toHaveAttribute('data-status', 'failed')
    await node(page, 'slow').click()
    await expect(page.getByTestId('inspector-error')).toContainText('Timed out after 1 s')
  })

  test('a script that exits with an error shows its message, and the cards after it say they were skipped', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), script('bad', 'echo "disk is full" 1>&2; exit 3'), script('after', 'echo never', 560)], [edge('t', 'bad'), edge('bad', 'after')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'bad')).toHaveAttribute('data-status', 'failed')
    await expect(node(page, 'after')).toHaveAttribute('data-status', 'skipped')
    await node(page, 'bad').click()
    await expect(page.getByTestId('inspector-error')).toContainText('exited with code 3')
    await expect(page.getByTestId('inspector-error')).toContainText('disk is full')
  })

  test('an AI card with nothing chosen says what to choose', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger(), card('ai', 'ai.ollama', { prompt: 'hi' })], [edge('t', 'ai')])
    await open(page, flow.id)
    await page.getByTestId('run').click()
    await expect(node(page, 'ai')).toHaveAttribute('data-status', 'failed')
    await node(page, 'ai').click()
    await expect(page.getByTestId('inspector-error')).toContainText('Choose a connection')
  })

  test('a run that cannot even start is a message next to the button, and the button works again', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    await page.route(/\/api\/flows\/[^/]+\/runs$/, (route) => (route.request().method() === 'POST' ? route.fulfill({ status: 500, json: { error: 'Unexpected server error.' } }) : route.fallback()))
    await page.getByTestId('run').click()
    await expect(page.getByTestId('run-error')).toContainText('Unexpected server error.')
    await expect(page.getByTestId('run')).toBeEnabled()
  })

  test('the flow list that cannot load shows the reason and a retry that works', async ({ page }) => {
    let fail = true
    await page.route(/\/api\/flows$/, (route) => (route.request().method() === 'GET' && fail ? route.fulfill({ status: 500, json: { error: 'Unexpected server error.' } }) : route.fallback()))
    await page.goto('/#/')
    await expect(page.getByTestId('flows-error')).toContainText('Unexpected server error.')
    fail = false
    await page.getByTestId('flows-retry').click()
    await expect(page.getByTestId('flows-error')).toHaveCount(0)
  })

  test('a flow that cannot be saved shows the reason and stays unsaved', async ({ page, request }) => {
    const flow = await makeFlow(request, [trigger()])
    await open(page, flow.id)
    await page.route(/\/api\/flows\/[^/]+$/, (route) => (route.request().method() === 'PUT' ? route.fulfill({ status: 400, json: { error: 'Card ids must be unique.' } }) : route.fallback()))
    await page.getByTestId('edit-name').fill('A new name') // a change makes the flow dirty
    await page.getByTestId('edit-save').click()
    await expect(page.getByTestId('edit-save-error')).toContainText('Card ids must be unique.')
    await expect(page.getByTestId('edit-save')).toBeEnabled()
  })

  test('when the app loses its live connection to the engine, the top bar says so, and clears when it is back', async ({ page }) => {
    await page.route('**/api/events', (route) => route.abort())
    await page.goto('/#/')
    await expect(page.getByTestId('reconnecting')).toBeVisible({ timeout: 15_000 })
    await expect(page.getByTestId('reconnecting')).toContainText('Reconnecting')
    await page.unroute('**/api/events')
    await expect(page.getByTestId('reconnecting')).toHaveCount(0, { timeout: 30_000 })
  })

  test('a backend that is gone shows Offline in the top bar', async ({ page }) => {
    await page.route('**/api/app/version', (route) => route.abort())
    await page.goto('/#/')
    await expect(page.getByTestId('backend-status')).toContainText('Offline')
  })
})
