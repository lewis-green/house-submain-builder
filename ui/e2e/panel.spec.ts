import { expect, test } from '@playwright/test'
import { seedSubmain, threeDimmed } from './fixtures'

const SLOT_PX = 8
const MODULE_PX = SLOT_PX * 3

/**
 * Long-press, move, release — the gesture a thumb makes.
 *
 * Waits for the save to land before returning: the move is applied optimistically
 * and saved in the background, so a test that reloads the moment the button comes
 * up will abort its own request and see the drag undone.
 */
async function dragDevice(page: import('@playwright/test').Page, label: string, byModules: number) {
  const device = page.getByRole('button', { name: new RegExp(`^${label}`) })
  const box = await device.boundingBox()
  if (!box) throw new Error(`${label} has no bounding box`)

  const x = box.x + box.width / 2
  const y = box.y + box.height / 2

  const saved = page.waitForResponse(r =>
    r.request().method() === 'PATCH' && r.url().includes('/position'))

  await page.mouse.move(x, y)
  await page.mouse.down()
  await page.waitForTimeout(450)            // past the long-press threshold
  await page.mouse.move(x + byModules * MODULE_PX, y, { steps: 8 })
  await page.mouse.up()

  const response = await saved
  if (!response.ok()) {
    throw new Error(`the move was refused: ${response.status()} ${await response.text()}`)
  }
}

/** For drags that are meant to be refused, where no request should be sent. */
async function dragDeviceExpectingNoSave(
  page: import('@playwright/test').Page,
  label: string,
  byModules: number,
) {
  const device = page.getByRole('button', { name: new RegExp(`^${label}`) })
  const box = await device.boundingBox()
  if (!box) throw new Error(`${label} has no bounding box`)

  const x = box.x + box.width / 2
  const y = box.y + box.height / 2

  await page.mouse.move(x, y)
  await page.mouse.down()
  await page.waitForTimeout(450)
  await page.mouse.move(x + byModules * MODULE_PX, y, { steps: 8 })
  await page.mouse.up()
  await page.waitForTimeout(300)
}

test('a panel generated from the wizard is drawn to scale', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)

  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Relay 1/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /C1/ })).toBeVisible()
})

test('tapping a device opens its channels', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /^Dimmer 1/ }).click()

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Circuit name' }).first()).toHaveValue('Kitchen ceiling')
})

test('a device can be dragged to a free slot and stays there after a reload', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  const before = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  await dragDevice(page, 'Dimmer 1', 10)

  await page.reload()
  const after = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  expect(after!.x).toBeGreaterThan(before!.x)
})

test('a drag onto an occupied slot leaves the device where it was', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  const before = await page.getByRole('button', { name: /^Dimmer 2/ }).boundingBox()

  // One module left puts Dimmer 2 on top of Dimmer 1, so nothing should be sent.
  await dragDeviceExpectingNoSave(page, 'Dimmer 2', -1)

  await page.reload()
  const after = await page.getByRole('button', { name: /^Dimmer 2/ }).boundingBox()

  expect(after!.x).toBeCloseTo(before!.x, 0)
})

test('a dragged position survives re-generation', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await dragDevice(page, 'Dimmer 1', 10)
  const moved = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  // Re-generate with the circuits it already has, plus one more.
  const layout = await (await request.get(`http://localhost:5020/submains/${submainId}/layout`)).json()
  const existing = layout.layout.devices
    .flatMap((d: { channels: { circuitId: string | null; circuitName: string | null }[] }) => d.channels)
    .filter((c: { circuitId: string | null }) => c.circuitId)
    .map((c: { circuitId: string; circuitName: string }, i: number) => ({
      id: c.circuitId, type: 'DimmedLighting', name: c.circuitName, sequence: i + 1,
    }))

  await request.post(`http://localhost:5020/submains/${submainId}/design/generate`, {
    data: { circuits: [...existing, { type: 'Switched', name: 'Extractor', sequence: existing.length + 1 }] },
  })

  await page.reload()
  const after = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  expect(after!.x).toBeCloseTo(moved!.x, 0)
})

test('the drawing does not make the page scroll sideways at phone width', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()

  const overflows = await page.evaluate(() =>
    document.documentElement.scrollWidth > document.documentElement.clientWidth)

  expect(overflows).toBe(false)
})
