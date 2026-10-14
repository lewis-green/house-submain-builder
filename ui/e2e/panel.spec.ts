import { expect, test, type Page } from '@playwright/test'
import { API, seedSubmain, threeDimmed } from './fixtures'
import { fingerDrag } from './touch'

/** Mirrors geometry.ts: a module is three slot units of 18px at zoom 1. */
const MODULE_PX = 18 * 3

/**
 * Long-press, move, release — the gesture a thumb makes.
 *
 * Waits for the save to land before returning: the move is applied optimistically
 * and saved in the background, so a test that reloads the moment the finger comes
 * up will abort its own request and see the drag undone.
 */
async function dragDevice(page: Page, label: string, byModules: number) {
  const device = page.getByRole('button', { name: new RegExp(`^${label}`) })

  const saved = page.waitForResponse(r =>
    r.request().method() === 'PATCH' && r.url().includes('/position'))

  const gesture = await fingerDrag(device, { dx: byModules * MODULE_PX, dy: 0 })
  expect(gesture.movePrevented, 'the drag must stop Safari scrolling the page').toBe(true)
  expect(gesture.endPrevented, 'the drop must not become a tap').toBe(true)

  const response = await saved
  if (!response.ok()) {
    throw new Error(`the move was refused: ${response.status()} ${await response.text()}`)
  }
}

/** For drags that are meant to be refused, where no request should be sent. */
async function dragDeviceExpectingNoSave(page: Page, label: string, byModules: number) {
  const device = page.getByRole('button', { name: new RegExp(`^${label}`) })
  await fingerDrag(device, { dx: byModules * MODULE_PX, dy: 0 })
  await page.waitForTimeout(300)
}

test('a panel generated from the wizard is drawn to scale', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)

  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Relay 1/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Terminals C1/ })).toBeVisible()
})

test('the terminal order can be changed and a circuit given several cables', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  await page.goto(`/submains/${submainId}/panel`)

  await page.getByRole('button', { name: /Terminals C1/ }).click()
  const sheet = page.getByRole('dialog', { name: 'Terminal order' })
  await expect(sheet).toBeVisible()

  await sheet.getByRole('button', { name: 'Move Hall up' }).click()
  await sheet.getByRole('button', { name: /more cables for hall/i }).click()
  await sheet.getByRole('button', { name: 'Save order' }).click()
  await expect(sheet).toBeHidden()

  await page.reload()
  await page.getByRole('button', { name: /Terminals C1/ }).click()
  const items = page.getByRole('dialog', { name: 'Terminal order' }).getByRole('listitem')
  await expect(items.nth(1)).toContainText('Hall')
  await expect(items.nth(1)).toContainText('C2.1–C2.2')
})

test('the terminal run shows a focus ring when reached by keyboard', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  await page.goto(`/submains/${submainId}/panel`)
  const run = page.getByRole('button', { name: /Terminals C1/ })
  // Reached as a keyboard user would: Tab until it has focus, bounded.
  for (let i = 0; i < 30 && !(await run.evaluate(el => el === document.activeElement)); i++) {
    await page.keyboard.press('Tab')
  }
  await expect(run).toBeFocused()
  // WebKit has been patchy drawing outlines on SVG children. A computed outline
  // style other than none is evidence the ring is drawn, not proof of pixels.
  const outline = await run.evaluate(el => getComputedStyle(el).outlineStyle)
  expect(outline).not.toBe('none')
})

test('every device is a thumb-sized target at default zoom', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()

  const boxes = await page.locator('[data-device-id]').evaluateAll(els =>
    els.map(el => { const b = el.getBoundingClientRect(); return { w: b.width, h: b.height } }))

  expect(boxes.length).toBeGreaterThan(0)
  for (const box of boxes) {
    expect(box.w).toBeGreaterThanOrEqual(44)
    expect(box.h).toBeGreaterThanOrEqual(44)
  }
})

test('tapping a device opens its channels in a sheet that locks the page behind it', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Circuit name' }).first()).toHaveValue('Kitchen ceiling')

  const lock = await page.evaluate(() => ({
    position: getComputedStyle(document.body).position,
    overflow: getComputedStyle(document.body).overflow,
  }))
  expect(lock.position).toBe('fixed')
  expect(lock.overflow).toBe('hidden')

  // Tab never leaves the dialog.
  for (let i = 0; i < 8; i++) await page.keyboard.press('Tab')
  expect(await page.evaluate(() => document.querySelector('[role=dialog]')!.contains(document.activeElement))).toBe(true)

  // Focusing a field lower down scrolls the sheet body, never the sheet
  // itself: the title, handle and Close button stay where the thumb expects them.
  await page.getByRole('textbox', { name: 'Circuit name' }).last().tap()
  const sheet = await page.evaluate(() => {
    const d = document.querySelector('[role=dialog]')!
    return { scrolled: d.scrollTop, headerTop: d.firstElementChild!.getBoundingClientRect().top, top: d.getBoundingClientRect().top }
  })
  expect(sheet.scrolled).toBe(0)
  expect(sheet.headerTop).toBeCloseTo(sheet.top, 0)
  await expect(page.getByRole('button', { name: 'Close' })).toBeInViewport()

  await page.getByRole('button', { name: 'Close' }).tap()
  await expect(dialog).toBeHidden()
  expect(await page.evaluate(() => getComputedStyle(document.body).position)).toBe('static')
})

test('a save landing while the next field is typed in leaves focus, and so the keyboard, where it is', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
  const room = page.getByRole('combobox', { name: 'Room' }).first()
  const name = page.getByRole('textbox', { name: 'Circuit name' }).first()

  // Room commits when the thumb moves on to Circuit name; the PATCH and the
  // reload land while the next word is being typed.
  await room.tap()
  await room.fill('Kitchen')
  const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/circuits/'))
  await name.tap()
  await page.keyboard.type(' Isl')
  expect((await saved).ok()).toBe(true)
  await expect(page.getByRole('status')).toContainText(/saved/i)
  await page.waitForTimeout(500)

  await expect(name).toBeFocused()
  await expect(name).toHaveValue('Kitchen ceiling Isl')
})

test('a status message stacks above the action bar rather than covering its button', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
  await page.getByRole('textbox', { name: 'Circuit name' }).first().fill('Island pendants')
  await page.keyboard.press('Enter')
  await expect(page.getByRole('status')).toContainText(/saved island pendants/i)
  await page.keyboard.press('Escape')

  const issue = page.getByRole('button', { name: /issue pdf/i })
  const box = (await issue.boundingBox())!
  const edges = await page.evaluate(([x, y]) => ({
    statusBottom: document.querySelector('[role=status] > div')!.getBoundingClientRect().bottom,
    // The status region sits in a wrapper of its own (so it can stay on screen when the bar is let into the page); the bar follows that wrapper.
    barTop: document.querySelector('[role=status]')!.parentElement!.nextElementSibling!.getBoundingClientRect().top,
    underThumb: document.elementFromPoint(x, y)?.closest('button')?.textContent ?? null,
  }), [box.x + box.width / 2, box.y + box.height / 2])

  expect(edges.statusBottom).toBeLessThanOrEqual(edges.barTop + 1)
  expect(edges.underThumb).toMatch(/issue pdf/i)
})

test('the drawing says how much of it is on screen, keeps its row labels in view and pans from the strip', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByText(/showing modules 1–\d+ of 24/i)).toBeVisible()

  const scroller = page.locator('.overflow-auto').first()
  await scroller.evaluate(el => { el.scrollLeft = 600 })
  await expect(page.getByText(/showing modules 1[0-9]–/i)).toBeVisible()

  // The gutter sticks to the scroller's left edge whatever is scrolled under it.
  const label = (await page.getByText('Row 2').boundingBox())!
  const frame = (await scroller.boundingBox())!
  expect(label.x).toBeGreaterThanOrEqual(frame.x)
  expect(label.x + label.width).toBeLessThanOrEqual(frame.x + 60)

  // Tapping the far right of the strip pans the drawing to its last modules.
  const strip = page.locator('svg[preserveAspectRatio="none"]')
  const stripBox = (await strip.boundingBox())!
  await strip.tap({ position: { x: stripBox.width - 2, y: stripBox.height / 2 } })
  expect(await scroller.evaluate(el => el.scrollLeft)).toBeGreaterThan(800)
  await expect(page.getByText(/–24 of 24/)).toBeVisible()

  // Fit width fits: the whole drawing is on screen, and the hint is honest
  // that nothing is tappable at that size.
  await page.getByRole('button', { name: 'Fit width' }).tap()
  await expect(page.getByText(/all 24 modules shown/i)).toBeVisible()
  const fitted = await scroller.evaluate(el => ({ scrollW: el.scrollWidth, clientW: el.clientWidth, left: el.scrollLeft }))
  expect(fitted.scrollW).toBeLessThanOrEqual(fitted.clientW)
  expect(fitted.left).toBe(0)
  await expect(page.getByText(/too small to tap/i)).toBeVisible()
  await page.getByRole('button', { name: '1×' }).tap()
  await expect(page.getByText(/tap a device to name its circuits/i)).toBeVisible()
})

test('a sheet closes on a backdrop tap and on Escape', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.touchscreen.tap(200, 80)
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toBeHidden()
})

test('a device can be dragged with a finger to a free slot and stays there after a reload', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  const before = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  await dragDevice(page, 'Dimmer 1', 5)

  // No sheet: the drop was a drop, not a tap.
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByRole('status')).toContainText(/moved/i)

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
  await dragDevice(page, 'Dimmer 1', 5)
  const moved = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  // Re-generate with the circuits it already has, plus one more.
  const layout = await (await request.get(`${API}/submains/${submainId}/layout`)).json()
  const existing = layout.layout.devices
    .flatMap((d: { channels: { circuitId: string | null; circuitName: string | null }[] }) => d.channels)
    .filter((c: { circuitId: string | null }) => c.circuitId)
    .map((c: { circuitId: string; circuitName: string }, i: number) => ({
      id: c.circuitId, type: 'DimmedLighting', name: c.circuitName, sequence: i + 1,
    }))

  await request.post(`${API}/submains/${submainId}/design/generate`, {
    data: { circuits: [...existing, { type: 'Switched', name: 'Extractor', sequence: existing.length + 1 }] },
  })

  await page.reload()
  const after = await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox()

  expect(after!.x).toBeCloseTo(moved!.x, 0)
})

test('zooming in grows the scrollable area so nothing is out of reach', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()

  for (let i = 0; i < 4; i++) await page.getByRole('button', { name: 'Zoom in' }).tap()

  const sizes = await page.evaluate(() => {
    const svg = document.querySelector('svg')!
    const scroller = svg.closest('.overflow-auto')!
    const r = svg.getBoundingClientRect()
    return { svgW: r.width, svgH: r.height, scrollW: scroller.scrollWidth, scrollH: scroller.scrollHeight }
  })
  expect(sizes.scrollW).toBeGreaterThanOrEqual(sizes.svgW)
  expect(sizes.scrollH).toBeGreaterThanOrEqual(sizes.svgH)
})

test('a rebuild the server cannot do leaves the install option as it was, with the reason', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  const stored = async () => (await request.get(`${API}/submains/${submainId}`)).json()

  // One request saves the option and rebuilds; if it fails, nothing happened.
  await page.route(`**/api/submains/${submainId}`, r => r.request().method() === 'PATCH'
    ? r.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ title: 'Generator down' }) })
    : r.continue())

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByText('Install options').tap()
  const toggle = page.getByLabel(/cables enter at the bottom/i)
  await expect(toggle).not.toBeChecked()
  await toggle.tap()

  await expect(page.getByRole('status')).toContainText(/panel not rebuilt: generator down/i)
  await expect(toggle).not.toBeChecked()
  await expect(toggle).toBeEnabled()
  expect((await stored()).terminalsAtBottom).toBe(false)
})

test('when the issued PDFs cannot be listed the bar says so and offers a retry, not a first "Issue"', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  await request.post(`${API}/submains/${submainId}/revisions`)

  let failing = true
  await page.route(`**/api/submains/${submainId}/revisions`, r => failing && r.request().method() === 'GET'
    ? r.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ title: 'Revisions down' }) })
    : r.continue())

  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByRole('alert')).toContainText(/could not check the issued pdfs/i)
  await expect(page.getByRole('button', { name: /issue pdf revision/i })).toBeHidden()

  failing = false
  await page.getByRole('button', { name: /try again/i }).tap()
  await expect(page.getByRole('link', { name: /open pdf \(revision 1\)/i })).toBeVisible()
  await expect(page.getByRole('alert')).toBeHidden()
})

test('issuing a PDF offers a plain link, which Safari cannot block', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)

  await page.goto(`/submains/${submainId}/panel`)
  await page.getByRole('button', { name: /issue pdf/i }).tap()

  const link = page.getByRole('link', { name: /open pdf/i })
  await expect(link).toBeVisible()
  await expect(link).toHaveAttribute('target', '_blank')

  const href = await link.getAttribute('href')
  const pdf = await request.get(href!)
  expect(pdf.ok()).toBe(true)
  expect(pdf.headers()['content-type']).toContain('application/pdf')
  expect((await pdf.body()).subarray(0, 4).toString()).toBe('%PDF')
  await expect(page.getByText(/changed since that pdf/i)).toBeHidden()

  // Renaming a circuit changes the schedule and nothing else — no layout
  // version moves — and the PDF on offer must say it is behind.
  await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
  await page.getByRole('textbox', { name: 'Circuit name' }).first().fill('Island pendants')
  await page.keyboard.press('Enter')
  await expect(page.getByRole('status')).toContainText(/saved island pendants/i)
  await page.keyboard.press('Escape')
  await expect(page.getByText(/changed since that pdf was issued/i)).toBeVisible()

  // And the bill of materials now exists for it, reached from the title block.
  await page.getByRole('navigation').getByRole('link', { name: /bill of materials/i }).tap()
  await expect(page.getByRole('heading', { name: /bill of materials/i })).toBeVisible()
  await expect(page.getByRole('link', { name: /download csv/i })).toBeVisible()
})

/**
 * Everything above is a finger. A mouse or trackpad differs in two ways that
 * matter here: a click follows pointer capture, so a capture taken by a parent
 * steals the click from a button inside it; and a mouse has no scroll to tell
 * apart from a drag, so it should not have to hold a device before moving it.
 */
test.describe('with a mouse or trackpad', () => {
  test.use({ viewport: { width: 1280, height: 900 }, hasTouch: false, isMobile: false, deviceScaleFactor: 1 })

  test('the × closes a sheet', async ({ page, request }) => {
    const { submainId } = await seedSubmain(request, threeDimmed)

    await page.goto(`/submains/${submainId}/panel`)
    await page.getByRole('button', { name: /^Dimmer 1/ }).click()
    await expect(page.getByRole('dialog')).toBeVisible()

    await page.getByRole('button', { name: 'Close' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
  })

  test('a device can be dragged straight away, without holding it first, and stays there after a reload', async ({ page, request }) => {
    const { submainId } = await seedSubmain(request, threeDimmed)

    await page.goto(`/submains/${submainId}/panel`)
    const device = page.getByRole('button', { name: /^Dimmer 1/ })
    const before = (await device.boundingBox())!
    const saved = page.waitForResponse(r => r.request().method() === 'PATCH' && r.url().includes('/position'))

    const x = before.x + before.width / 2
    const y = before.y + before.height / 2
    await page.mouse.move(x, y)
    await page.mouse.down()
    await page.mouse.move(x + 5 * MODULE_PX, y, { steps: 10 })
    await page.mouse.up()
    expect((await saved).ok()).toBe(true)
    await expect(page.getByRole('dialog'), 'a drop is not a click').toBeHidden()

    await page.reload()
    const after = (await page.getByRole('button', { name: /^Dimmer 1/ }).boundingBox())!
    expect(after.x).toBeGreaterThan(before.x + 4 * MODULE_PX)
  })
})

test('a built panel can be edited: one more dimmed circuit, saved and rebuilt, keeps the names it had', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  await page.goto(`/submains/${submainId}/panel`)
  await expect(page.getByRole('button', { name: /Kitchen ceiling/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Lighting 4/ })).toHaveCount(0)

  await page.getByRole('link', { name: 'Edit' }).tap()
  await expect(page.getByRole('heading', { level: 1, name: 'Edit E2E submain' })).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Dimmed lighting' })).toHaveValue('3')

  await page.getByRole('button', { name: /more dimmed lighting/i }).tap()
  const save = page.getByRole('button', { name: /save and rebuild/i })
  await expect(save).toBeEnabled()
  await save.tap()

  await expect(page).toHaveURL(new RegExp(`/submains/${submainId}/panel$`))
  // The panel's loading skeleton is a status too: pick the announcement by what it says.
  await expect(page.getByRole('status').filter({ hasText: /panel rebuilt/i })).toBeVisible()
  await expect(page.getByRole('button', { name: /Lighting 4/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Kitchen ceiling/ })).toBeVisible()
  const submain = await (await request.get(`${API}/submains/${submainId}`)).json()
  expect(submain.circuitCount).toBe(5)
})
