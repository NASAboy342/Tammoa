import { expect, test, type Locator, type Page } from '@playwright/test'
import { edge, makeFlow, node, open, script, trigger } from './helpers'

/** Cards in a row (t, a, b) and one below (c): room to select, group and copy without overlaps. */
const layout = () => [trigger(), script('a', 'echo a', 260), script('b', 'echo b', 520), { ...script('c', 'echo c', 260), position: { x: 260, y: 160 } }]
const chain = () => [edge('t', 'a'), edge('a', 'b'), edge('b', 'c')]

const centre = async (l: Locator) => {
  const b = (await l.boundingBox())!
  return { x: b.x + b.width / 2, y: b.y + b.height / 2 }
}
async function dragBy(page: Page, from: Locator, dx: number, dy: number) {
  const c = await centre(from)
  await page.mouse.move(c.x, c.y)
  await page.mouse.down()
  await page.mouse.move(c.x + dx, c.y + dy, { steps: 8 })
  await page.mouse.up()
}
const shiftClick = (l: Locator) => l.click({ modifiers: ['Shift'] })
const pos = async (page: Page, id: string) => {
  const b = (await node(page, id).boundingBox())!
  return { x: Math.round(b.x), y: Math.round(b.y) }
}
const selectedCount = (page: Page) => page.locator('[data-node-card].border-2').count()
const saved = async (request: any, id: string) => (await (await request.get(`/api/flows/${id}`)).json()).definition
const save = async (page: Page) => {
  await page.getByTestId('edit-save').click()
  await expect(page.getByTestId('edit-saved')).toBeVisible()
}
const frames = (page: Page) => page.locator('[data-group-name]')

test.describe('selecting several cards', () => {
  test('Shift-click adds and removes cards; a plain click selects one; the background clears', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await expect(page.getByTestId('selection-bar')).toHaveCount(0)
    await node(page, 'a').click()
    await expect(page.getByTestId('selection-count')).toContainText('1 card')
    await shiftClick(node(page, 'b'))
    await expect(page.getByTestId('selection-count')).toContainText('2 cards')
    expect(await selectedCount(page)).toBe(2)
    await expect(page.getByTestId('inspector-hint')).toBeVisible() // no single card to edit
    await shiftClick(node(page, 'a'))
    await expect(page.getByTestId('selection-count')).toContainText('1 card')
    await expect(page.getByTestId('inspector-name')).toHaveValue('Script b') // one card again: its settings show
    await shiftClick(node(page, 'c'))
    await node(page, 'a').click()
    expect(await selectedCount(page)).toBe(1)
    await page.mouse.click(900, 600)
    await expect(page.getByTestId('selection-bar')).toHaveCount(0)
  })

  test('Shift + drag on the background draws a rectangle that selects what it touches; Ctrl/Cmd+A selects all', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    const a = (await node(page, 'a').boundingBox())!
    const b = (await node(page, 'b').boundingBox())!
    await page.keyboard.down('Shift')
    await page.mouse.move(a.x - 20, a.y - 20)
    await page.mouse.down()
    await page.mouse.move(b.x + 60, b.y + 30, { steps: 8 })
    await expect(page.getByTestId('marquee')).toBeVisible()
    await page.mouse.up()
    await page.keyboard.up('Shift')
    await expect(page.getByTestId('marquee')).toHaveCount(0)
    await expect(page.getByTestId('selection-count')).toContainText('2 cards')
    expect(await selectedCount(page)).toBe(2)
    await expect(node(page, 'c')).not.toHaveClass(/border-2/)
    await page.getByTestId('canvas').focus()
    await page.keyboard.press('ControlOrMeta+a')
    await expect(page.getByTestId('selection-count')).toContainText('4 cards')
  })

  test('selected cards move together; an unselected card moves alone; a click inside a selection selects just that card', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    const before = { t: await pos(page, 't'), a: await pos(page, 'a'), b: await pos(page, 'b'), c: await pos(page, 'c') }
    await node(page, 'a').click()
    await shiftClick(node(page, 'b'))
    await dragBy(page, node(page, 'a'), 100, 40)
    expect(await pos(page, 'a')).toEqual({ x: before.a.x + 100, y: before.a.y + 40 })
    expect(await pos(page, 'b')).toEqual({ x: before.b.x + 100, y: before.b.y + 40 })
    expect(await pos(page, 'c')).toEqual(before.c)
    expect(await pos(page, 't')).toEqual(before.t)
    await expect(page.getByTestId('selection-count')).toContainText('2 cards') // still both selected

    await dragBy(page, node(page, 'c'), 0, 60) // c was not selected: only it moves, and the selection becomes c
    expect(await pos(page, 'c')).toEqual({ x: before.c.x, y: before.c.y + 60 })
    expect(await pos(page, 'b')).toEqual({ x: before.b.x + 100, y: before.b.y + 40 })
    await expect(page.getByTestId('selection-count')).toContainText('1 card')

    await node(page, 'a').click()
    await shiftClick(node(page, 'b'))
    await node(page, 'a').click() // plain click inside the selection → only a
    await expect(page.getByTestId('selection-count')).toContainText('1 card')

    await save(page)
    const moved = (await saved(request, flow.id)).nodes.find((n: any) => n.id === 'b').position
    expect(moved).toEqual({ x: 520 + 100, y: 40 })
  })
})

test.describe('groups', () => {
  test('group two cards, name the group, and it is saved and still there after a reload', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await node(page, 'a').click()
    await expect(page.getByTestId('sel-group')).toBeDisabled() // one card is not a group
    await shiftClick(node(page, 'b'))
    await page.getByTestId('sel-group').click()
    await expect(frames(page)).toHaveCount(1)
    await expect(page.getByTestId('group-title')).toHaveText('Group')
    await expect(page.getByTestId('selection-count')).toContainText('Group · 2 cards')
    await page.getByTestId('group-name').fill('Prepare')
    await expect(page.getByTestId('group-title')).toHaveText('Prepare')
    await page.getByTestId('group-name').fill('')
    await page.getByTestId('group-name').blur()
    await expect(page.getByTestId('group-name')).toHaveValue('Prepare') // a group is never left without a name
    await save(page)
    const groups = (await saved(request, flow.id)).groups
    expect(groups).toEqual([{ id: expect.any(String), name: 'Prepare', nodeIds: ['a', 'b'] }])
    await page.reload()
    await expect(page.getByTestId('canvas')).toBeVisible()
    await expect(frames(page)).toHaveCount(1)
    await expect(page.getByTestId('group-title')).toHaveText('Prepare')
  })

  test('the frame wraps its cards and follows them; dragging the name bar moves the whole group', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    await open(page, flow.id)
    const frame = page.getByTestId('group-g1')
    const f = (await frame.boundingBox())!
    const a = (await node(page, 'a').boundingBox())!
    const b = (await node(page, 'b').boundingBox())!
    expect(f.x).toBeLessThan(a.x)
    expect(f.y).toBeLessThan(a.y - 20) // room for the name above the cards
    expect(f.x + f.width).toBeGreaterThan(b.x + b.width)
    const before = { a: await pos(page, 'a'), b: await pos(page, 'b'), c: await pos(page, 'c') }
    await dragBy(page, page.getByTestId('group-head-g1'), 80, 50)
    expect(await pos(page, 'a')).toEqual({ x: before.a.x + 80, y: before.a.y + 50 })
    expect(await pos(page, 'b')).toEqual({ x: before.b.x + 80, y: before.b.y + 50 })
    expect(await pos(page, 'c')).toEqual(before.c)
    await expect(page.getByTestId('selection-count')).toContainText('Group · 2 cards')
    await page.mouse.click(900, 600) // deselect: a card of a selected group would move the whole selection
    await dragBy(page, node(page, 'a'), 0, 90) // moving one card alone stretches the frame, the group stays
    expect((await frame.boundingBox())!.height).toBeGreaterThan(f.height + 50)
  })

  test('clicking inside a group on empty canvas still pans the canvas (the frame does not trap the mouse)', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Wide', nodeIds: ['a', 'c'] }] })
    await open(page, flow.id)
    const f = (await page.getByTestId('group-g1').boundingBox())!
    const before = await pos(page, 'b')
    await dragBy(page, page.getByTestId('canvas'), 0, 0) // no-op click on the background
    await page.mouse.move(f.x + f.width - 12, f.y + f.height / 2) // inside the frame, empty
    await page.mouse.down()
    await page.mouse.move(f.x + f.width - 12 + 60, f.y + f.height / 2 + 30, { steps: 5 })
    await page.mouse.up()
    const after = await pos(page, 'b')
    expect(after).toEqual({ x: before.x + 60, y: before.y + 30 })
  })

  test('ungroup keeps the cards; a card moved into a new group leaves its old one; saving writes no empty groups', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    await open(page, flow.id)
    await page.getByTestId('group-head-g1').click()
    await page.getByTestId('sel-ungroup').click()
    await expect(frames(page)).toHaveCount(0)
    await expect(page.locator('[data-node-card]')).toHaveCount(4)
    await expect(page.getByTestId('selection-count')).toContainText('2 cards') // the cards stay selected

    await page.getByTestId('sel-group').click()
    await page.getByTestId('group-name').fill('Again')
    await node(page, 'c').click()
    await shiftClick(node(page, 'b'))
    await page.getByTestId('sel-group').click() // b joins a new group with c, leaving "Again"
    await expect(frames(page)).toHaveCount(2)
    await save(page)
    const groups = (await saved(request, flow.id)).groups
    expect(groups.map((g: any) => g.nodeIds).sort()).toEqual([['a'], ['b', 'c']])

    await page.getByTestId('group-head-' + groups.find((g: any) => g.nodeIds.length === 2).id).click()
    await page.getByTestId('sel-ungroup').click()
    await node(page, 'a').click()
    await shiftClick(node(page, 't'))
    await page.getByTestId('sel-group').click() // a moves out of the one-card group, which disappears
    await save(page)
    expect((await saved(request, flow.id)).groups.map((g: any) => g.nodeIds)).toEqual([['t', 'a']])
  })
})

test.describe('copying', () => {
  test('a card is copied with its settings, a new name, and without any connection', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await node(page, 'a').click()
    await page.getByTestId('sel-copy').click()
    await expect(page.getByTestId('canvas-notice')).toContainText('without connections')
    await expect(page.locator('[data-node-card]')).toHaveCount(5)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(3) // no new connection
    await expect(page.getByTestId('inspector-name')).toHaveValue('Script a 2') // the copy is selected
    await expect(page.getByTestId('selection-count')).toContainText('1 card')
    await save(page)
    const def = await saved(request, flow.id)
    const copy = def.nodes.find((n: any) => n.name === 'Script a 2')
    expect(copy.params.command).toBe('echo a')
    expect(copy.id).not.toBe('a')
    expect(def.edges).toHaveLength(3)
    expect(def.edges.some((e: any) => e.source === copy.id || e.target === copy.id)).toBe(false)
  })

  test('several cards are copied at once with Ctrl/Cmd+D, and a copy of a grouped card is not in the group', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    await open(page, flow.id)
    await node(page, 'a').click()
    await shiftClick(node(page, 'c'))
    await page.getByTestId('canvas').focus()
    await page.keyboard.press('ControlOrMeta+d')
    await expect(page.locator('[data-node-card]')).toHaveCount(6)
    await expect(page.getByTestId('selection-count')).toContainText('2 cards')
    await save(page)
    const def = await saved(request, flow.id)
    expect(def.groups).toEqual([{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }])
    expect(def.nodes.map((n: any) => n.name)).toEqual(expect.arrayContaining(['Script a 2', 'Script c 2']))
  })

  test('a group is copied with the connections inside it, as a new group with new cards', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    await open(page, flow.id)
    await page.getByTestId('group-head-g1').click()
    await page.getByTestId('sel-copy').click()
    await expect(page.getByTestId('canvas-notice')).toContainText('1 connection')
    await expect(page.locator('[data-node-card]')).toHaveCount(6)
    await expect(frames(page)).toHaveCount(2)
    await expect(page.locator('[data-group-name="Pair copy"]')).toHaveCount(1)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(4) // the one inside the group came along
    await save(page)
    const def = await saved(request, flow.id)
    const copy = def.groups.find((g: any) => g.name === 'Pair copy')
    expect(copy.nodeIds).toHaveLength(2)
    expect(copy.nodeIds.some((id: string) => ['a', 'b'].includes(id))).toBe(false)
    const inside = def.edges.filter((e: any) => copy.nodeIds.includes(e.source) || copy.nodeIds.includes(e.target))
    expect(inside).toHaveLength(1) // only the one between the two copies; nothing links them to t or c
    expect(copy.nodeIds).toEqual(expect.arrayContaining([inside[0].source, inside[0].target]))
    expect(def.edges).toHaveLength(4)
    expect(def.groups.find((g: any) => g.id === 'g1').nodeIds).toEqual(['a', 'b']) // the original is untouched
  })
})

test.describe('deleting asks first', () => {
  test('a card: the dialog names it and counts its connections; Cancel keeps it, Delete removes it and its connections', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await node(page, 'a').click()
    await page.getByTestId('sel-delete').click()
    await expect(page.getByTestId('confirm-dialog')).toBeVisible()
    await expect(page.getByTestId('confirm-message')).toContainText('"Script a"')
    await expect(page.getByTestId('confirm-message')).toContainText('2 connections')
    await page.getByTestId('confirm-cancel').click()
    await expect(page.getByTestId('confirm-dialog')).toHaveCount(0)
    await expect(node(page, 'a')).toBeVisible()
    await page.keyboard.press('Escape') // nothing happens without a dialog
    await page.getByTestId('sel-delete').click()
    await page.keyboard.press('Escape')
    await expect(page.getByTestId('confirm-dialog')).toHaveCount(0)
    await expect(node(page, 'a')).toBeVisible()
    await page.getByTestId('sel-delete').click()
    await page.getByTestId('confirm-ok').click()
    await expect(node(page, 'a')).toHaveCount(0)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(1) // only b → c is left
    await expect(page.getByTestId('selection-bar')).toHaveCount(0)
  })

  test('the delete button in the card panel asks too', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await node(page, 'b').click()
    await page.getByTestId('inspector-delete').click()
    await expect(page.getByTestId('confirm-dialog')).toBeVisible()
    await expect(node(page, 'b')).toBeVisible()
    await page.getByTestId('confirm-ok').click()
    await expect(node(page, 'b')).toHaveCount(0)
  })

  test('several cards are deleted together after one question', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await node(page, 'a').click()
    await shiftClick(node(page, 'b'))
    await page.getByTestId('canvas').focus()
    await page.keyboard.press('Delete')
    await expect(page.getByTestId('confirm-message')).toContainText('these 2 cards')
    await page.getByTestId('confirm-ok').click()
    await expect(page.locator('[data-node-card]')).toHaveCount(2)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(0)
  })

  test('a group: the dialog says its cards go too and points at Ungroup; Delete removes group, cards and connections', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    await open(page, flow.id)
    await page.getByTestId('group-head-g1').click()
    await page.getByTestId('sel-delete').click()
    const msg = page.getByTestId('confirm-message')
    await expect(msg).toContainText('"Pair"')
    await expect(msg).toContainText('2 cards')
    await expect(msg).toContainText('3 connections')
    await expect(msg).toContainText('Ungroup')
    await page.getByTestId('confirm-cancel').click()
    await expect(frames(page)).toHaveCount(1)
    await page.getByTestId('sel-delete').click()
    await page.getByTestId('confirm-ok').click()
    await expect(frames(page)).toHaveCount(0)
    await expect(node(page, 'a')).toHaveCount(0)
    await expect(node(page, 'b')).toHaveCount(0)
    await expect(page.locator('[data-node-card]')).toHaveCount(2)
    await save(page)
    expect((await saved(request, flow.id)).groups).toBeUndefined()
  })

  test('deleting one card of a group leaves the group with the rest', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Trio', nodeIds: ['a', 'b', 'c'] }] })
    await open(page, flow.id)
    await node(page, 'b').click()
    await page.getByTestId('sel-delete').click()
    await page.getByTestId('confirm-ok').click()
    await save(page)
    expect((await saved(request, flow.id)).groups).toEqual([{ id: 'g1', name: 'Trio', nodeIds: ['a', 'c'] }])
  })

  test('removing a connection is not a card delete: no question', async ({ page, request }) => {
    const flow = await makeFlow(request, layout(), chain())
    await open(page, flow.id)
    await page.locator('[data-testid="edge-e-a-main-b"] path').last().dispatchEvent('pointerdown')
    await page.getByTestId('edge-delete').click()
    await expect(page.getByTestId('confirm-dialog')).toHaveCount(0)
    await expect(page.locator('[data-testid^="edge-e-"]')).toHaveCount(2)
  })
})

test.describe('the backend and the flow files', () => {
  test('groups are validated on save: unknown card, a card in two groups, no name, too long a name', async ({ request }) => {
    const flow = await makeFlow(request, layout(), chain())
    const put = (groups: unknown) => request.put(`/api/flows/${flow.id}`, { data: { name: flow.name, description: '', definition: { schemaVersion: 1, nodes: layout(), edges: chain(), groups } } })
    expect((await put([{ id: 'g', name: 'Ok', nodeIds: ['a'] }])).status()).toBe(200)
    for (const bad of [
      [{ id: 'g', name: 'Ghost', nodeIds: ['zzz'] }],
      [{ id: 'g1', name: 'One', nodeIds: ['a'] }, { id: 'g2', name: 'Two', nodeIds: ['a'] }],
      [{ id: 'g', name: ' ', nodeIds: ['a'] }],
      [{ id: 'g', name: 'x'.repeat(61), nodeIds: ['a'] }],
      'nope',
    ]) {
      const r = await put(bad)
      expect(r.status(), JSON.stringify(bad)).toBe(400)
    }
  })

  test('groups travel with the flow: duplicate and export / import keep them', async ({ request }) => {
    const flow = await makeFlow(request, layout(), chain(), { groups: [{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }] })
    const copy = await (await request.post(`/api/flows/${flow.id}/duplicate`)).json()
    expect((await saved(request, copy.id)).groups).toEqual([{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }])
    const file = await (await request.get(`/api/flows/${flow.id}/export`)).json()
    const imported = await request.post('/api/flows/import', { data: file })
    expect(imported.status()).toBe(201)
    expect((await saved(request, (await imported.json()).id)).groups).toEqual([{ id: 'g1', name: 'Pair', nodeIds: ['a', 'b'] }])
  })
})
