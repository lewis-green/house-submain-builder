import { expect, test, type APIRequestContext } from '@playwright/test'
import { API } from './fixtures'
import { fingerDrag } from './touch'

interface Seeded { projectId: string; rackId: string; items: { id: string; label: string; topU: number }[] }

/** A house with a 12U rack holding a 48-port switch and a patch panel, built straight through the API. */
async function seedRack(request: APIRequestContext): Promise<Seeded> {
  const cabinets = await (await request.get(`${API}/catalogue/rack-types`)).json()
  const types = await (await request.get(`${API}/catalogue/rack-device-types`)).json()
  const twelve = cabinets.find((c: { heightU: number }) => c.heightU === 12)
  const sw = types.find((t: { model: string }) => t.model === 'USW-Pro-Max-48-PoE')
  const panel = types.find((t: { kind: string }) => t.kind === 'PatchPanel')

  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E rack ${Date.now()}-${Math.random()}` } })).json()
  const rack = await (await request.post(`${API}/projects/${project.id}/racks`, { data: { name: 'Comms cupboard', rackTypeId: twelve.id } })).json()
  await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: sw.id } })
  const layout = await (await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: panel.id } })).json()
  return { projectId: project.id, rackId: rack.id, items: layout.items }
}

const layoutOf = async (request: APIRequestContext, rackId: string) =>
  (await (await request.get(`${API}/racks/${rackId}/layout`)).json()) as {
    items: { id: string; label: string; topU: number; ports: { number: number; label: string | null; poe: boolean }[] }[]
  }

test('a rack is added from the house page, like a submain', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E house ${Date.now()}` } })).json()

  await page.goto(`/projects/${project.id}`)
  await page.getByRole('link', { name: 'Add rack' }).tap()
  await page.getByLabel('Rack name').fill('Comms cupboard')
  await page.getByRole('button', { name: 'Add rack' }).tap()

  await expect(page.getByRole('heading', { name: 'Comms cupboard' })).toBeVisible()
  await expect(page.getByText('Nothing in this rack yet')).toBeVisible()
})

test('ports are labelled in one run: Return saves and moves to the next port', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()
  const saved = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith('/ports/2'))
  await page.getByLabel('Port 1 label').fill('Kitchen AP')
  await page.keyboard.press('Enter')
  await expect(page.getByLabel('Port 2 label')).toBeFocused()
  await page.keyboard.type('Lounge TV')
  await page.keyboard.press('Enter')
  await saved
  const poeSaved = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith('/ports/1'))
  await page.getByRole('checkbox', { name: 'Port 1 PoE' }).tap()
  await poeSaved

  const sw = (await layoutOf(request, rackId)).items.find(i => i.label === 'Switch 1')!
  expect(sw.ports).toEqual([
    { number: 1, label: 'Kitchen AP', poe: true },
    { number: 2, label: 'Lounge TV', poe: false },
  ])
})

test('an SFP+ port offers no PoE tick', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()

  await expect(page.getByLabel('Port 49 label')).toBeVisible()
  await expect(page.getByRole('checkbox', { name: 'Port 49 PoE' })).toHaveCount(0)
})

test('an item can be dragged with a finger to another U and stays there after a reload', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  const panel = page.getByRole('button', { name: /^Patch panel 1/ })
  const box = (await panel.boundingBox())!
  // One U on screen is the item's drawn height plus its two insets; drop it three U lower.
  const unit = box.height * 56 / 48
  const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/position'))

  const gesture = await fingerDrag(panel, { dx: 0, dy: unit * 3 })
  expect(gesture.movePrevented, 'the drag must stop Safari scrolling the page').toBe(true)
  expect((await saved).ok()).toBe(true)

  await page.reload()
  expect((await layoutOf(request, rackId)).items.find(i => i.label === 'Patch panel 1')!.topU).toBe(8)
})

test('issuing a rack PDF offers a plain link to a real PDF', async ({ page, request }) => {
  const { rackId } = await seedRack(request)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /issue pdf/i }).tap()
  const link = page.getByRole('link', { name: /open pdf \(revision 1\)/i })
  await expect(link).toBeVisible()

  const href = await link.getAttribute('href')
  const pdf = await request.get(`${API}${href!.replace(/^\/api/, '')}`)
  expect(pdf.headers()['content-type']).toContain('application/pdf')
  expect((await pdf.body()).subarray(0, 4).toString()).toBe('%PDF')
})

test.describe('with a mouse or trackpad', () => {
  test.use({ viewport: { width: 1280, height: 900 }, hasTouch: false, isMobile: false, deviceScaleFactor: 1 })

  test('a rack item can be dragged straight away, without holding it first', async ({ page, request }) => {
    const { rackId } = await seedRack(request)

    await page.goto(`/racks/${rackId}`)
    const panel = page.getByRole('button', { name: /^Patch panel 1/ })
    const box = (await panel.boundingBox())!
    const unit = box.height * 56 / 48
    const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/position'))

    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.down()
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2 + unit * 2, { steps: 10 })
    await page.mouse.up()

    expect((await saved).ok()).toBe(true)
    await expect(page.getByRole('dialog'), 'a drop is not a click').toBeHidden()
    expect((await layoutOf(request, rackId)).items.find(i => i.label === 'Patch panel 1')!.topU).toBe(9)
  })
})
