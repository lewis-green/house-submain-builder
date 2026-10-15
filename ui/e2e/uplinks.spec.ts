import { expect, test, type APIRequestContext } from '@playwright/test'
import { API, placeRack, seedRack } from './fixtures'

/** A second rack, "Core", in the seeded rack's house, holding a USW-Aggregation. */
async function coreRack(request: APIRequestContext, projectId: string) {
  const cabinets = await (await request.get(`${API}/catalogue/rack-types`)).json()
  const types = await (await request.get(`${API}/catalogue/rack-device-types`)).json()
  const twelve = cabinets.find((c: { heightU: number }) => c.heightU === 12)
  const aggregation = types.find((t: { model: string }) => t.model === 'USW-Aggregation')

  const rack = await (await request.post(`${API}/projects/${projectId}/racks`, { data: { name: 'Core', rackTypeId: twelve.id } })).json()
  const layout = await (await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: aggregation.id } })).json()
  return { rackId: rack.id as string, switchId: layout.items[0].id as string }
}

const overflow = (page: import('@playwright/test').Page) => page.evaluate(() => {
  const dialog = document.querySelector('[role=dialog]')!
  return {
    page: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    sheet: dialog.scrollWidth - dialog.clientWidth,
  }
})

test('a switch uplinks into the aggregation switch in another rack, and that rack shows the downlink', async ({ page, request }) => {
  const { projectId, rackId } = await seedRack(request)
  const core = await coreRack(request, projectId)

  await page.goto(`/racks/${rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()
  const sheet = page.getByRole('dialog')
  // The first SFP+ port, unless told otherwise.
  await expect(sheet.getByLabel('Uplink port')).toHaveValue('49')
  await sheet.getByLabel('Goes to').selectOption('switch')
  await sheet.getByLabel('Switch', { exact: true }).selectOption({ label: 'Switch 1 · Ubiquiti USW-Aggregation' })
  await sheet.getByLabel('Its port').selectOption('3')
  const saved = page.waitForResponse(r => r.request().method() === 'PUT' && r.url().endsWith('/uplink'))
  await sheet.getByRole('button', { name: 'Save uplink' }).tap()
  expect((await saved).ok()).toBe(true)

  await expect(sheet.getByText('Uplink to Switch 1 · Core · port 3', { exact: true })).toBeVisible()
  await expect(page.getByRole('status')).toContainText('Uplink saved')

  await page.goto(`/racks/${core.rackId}`)
  await page.getByRole('button', { name: /^Switch 1/ }).tap()
  await expect(page.getByLabel('Port 3 label')).toBeVisible()
  await expect(page.getByRole('dialog').getByText('Downlink from Switch 1 · Comms cupboard', { exact: true })).toBeVisible()
})

test('at 320px a switch\'s sheet with its uplink picker open does not scroll sideways, even with 200% text', async ({ page, request }) => {
  const { projectId, rackId, items } = await seedRack(request)
  await placeRack(request, rackId, 'Plant room under the back stairs')
  const core = await coreRack(request, projectId)
  await placeRack(request, core.rackId, 'Comms cupboard in the loft over the garage')
  const sw = items.find(i => i.label === 'Switch 1')!
  await request.put(`${API}/rack-items/${sw.id}/uplink`, { data: { port: 49, toItemId: core.switchId, toPort: 3 } })

  await page.setViewportSize({ width: 320, height: 740 })
  await page.goto(`/racks/${rackId}`, { waitUntil: 'networkidle' })
  await page.getByRole('button', { name: /^Switch 1/ }).tap()
  const sheet = page.getByRole('dialog')
  await expect(sheet.getByLabel('Its port')).toHaveValue('3')
  await sheet.getByLabel('Uplink port').scrollIntoViewIfNeeded()

  let measured = await overflow(page)
  expect(measured.page).toBeLessThanOrEqual(0)
  expect(measured.sheet).toBeLessThanOrEqual(0)

  await page.evaluate(() => { document.documentElement.style.fontSize = '32px' })
  await page.waitForTimeout(300)
  measured = await overflow(page)
  expect(measured.page, '200% text').toBeLessThanOrEqual(0)
  expect(measured.sheet, '200% text').toBeLessThanOrEqual(0)
})
