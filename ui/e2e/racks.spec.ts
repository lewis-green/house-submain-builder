import { expect, test, type APIRequestContext } from '@playwright/test'
import { API, seedRack } from './fixtures'
import { fingerDrag } from './touch'

const layoutOf = async (request: APIRequestContext, rackId: string) =>
  (await (await request.get(`${API}/racks/${rackId}/layout`)).json()) as {
    items: { id: string; label: string; topU: number; ports: { number: number; label: string | null; poe: boolean }[] }[]
  }

test('a rack is added from the house page, like an automation panel, and its first switch from the rack screen', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E house ${Date.now()}` } })).json()

  await page.goto(`/projects/${project.id}`)
  await page.getByRole('link', { name: 'Add rack' }).tap()
  await page.getByLabel('Rack name').fill('Comms cupboard')
  await page.getByRole('button', { name: 'Add rack' }).tap()

  await expect(page.getByRole('heading', { name: 'Comms cupboard' })).toBeVisible()
  await expect(page.getByText('Nothing in this rack yet')).toBeVisible()

  // The first item goes in the top U of the 12U cabinet the new-rack page offers first.
  await page.getByRole('button', { name: 'Add to rack' }).first().tap()
  await page.getByRole('dialog', { name: 'Add to rack' }).getByRole('button', { name: /USW-Pro-Max-48-PoE/ }).tap()

  await expect(page.getByRole('button', { name: /^Switch 1, / })).toBeVisible()
  await expect(page.getByRole('status')).toContainText('Switch 1 added in U12')
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
