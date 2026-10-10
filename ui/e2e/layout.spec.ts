import { expect, test } from '@playwright/test'
import { API, seedSubmain, threeDimmed } from './fixtures'

/**
 * The screens, at the four widths an iPhone can be. Nothing may make the page
 * scroll sideways: a page that pans under the thumb breaks every drag.
 */
for (const width of [320, 375, 393, 430]) {
  test(`no horizontal overflow at ${width}px`, async ({ page, request }) => {
    const { submainId } = await seedSubmain(request, threeDimmed)
    const submain = await (await request.get(`${API}/submains/${submainId}`)).json()

    await page.setViewportSize({ width, height: 740 })

    const screens = [
      '/',
      `/projects/${submain.projectId}`,
      `/projects/${submain.projectId}/submains/new`,
      `/submains/${submainId}/panel`,
      `/submains/${submainId}/bom`,
      '/catalogue',
      '/nope',
    ]

    for (const url of screens) {
      await page.goto(url, { waitUntil: 'networkidle' })
      if (url.endsWith('/new')) {
        await page.getByRole('button', { name: /more dimmed lighting/i }).tap()
        await page.getByRole('button', { name: /add tape circuit/i }).tap()
        await page.waitForTimeout(600)
      }
      const overflow = await page.evaluate(() =>
        document.documentElement.scrollWidth - document.documentElement.clientWidth)
      expect(overflow, `${url} at ${width}px`).toBeLessThanOrEqual(0)
    }
  })
}

/**
 * The same screens with the text doubled — the largest iOS text size — plus
 * the device sheet. Nothing may scroll sideways, and the sheet's title must
 * wrap rather than be cut: "Dimmer…" loses the one word that says which.
 */
for (const width of [320, 375, 393, 430]) {
  test(`no horizontal overflow at ${width}px with 200% text, and the sheet title wraps`, async ({ page, request }) => {
    const { submainId } = await seedSubmain(request, threeDimmed)
    const submain = await (await request.get(`${API}/submains/${submainId}`)).json()

    await page.setViewportSize({ width, height: 740 })
    const doubleText = () => page.evaluate(() => { document.documentElement.style.fontSize = '32px' })
    const overflow = () => page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth)

    const screens = [
      '/',
      `/projects/${submain.projectId}`,
      `/projects/${submain.projectId}/submains/new`,
      `/submains/${submainId}/panel`,
      `/submains/${submainId}/bom`,
      '/catalogue',
      '/nope',
    ]

    for (const url of screens) {
      await page.goto(url, { waitUntil: 'networkidle' })
      if (url.endsWith('/new')) {
        await page.getByRole('button', { name: /more dimmed lighting/i }).tap()
        await page.getByRole('button', { name: /add tape circuit/i }).tap()
        await page.waitForTimeout(600)
      }
      if (url.endsWith('/panel')) await page.locator('details').evaluate(el => { (el as HTMLDetailsElement).open = true })
      await doubleText()
      await page.waitForTimeout(300)
      expect(await overflow(), `${url} at ${width}px, 200% text`).toBeLessThanOrEqual(0)
    }

    await page.goto(`/submains/${submainId}/panel`, { waitUntil: 'networkidle' })
    await doubleText()
    await page.getByRole('button', { name: /^Dimmer 1/ }).tap()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    const title = await dialog.locator('h2').evaluate(h2 => ({
      text: h2.textContent, clipped: h2.scrollWidth > h2.clientWidth + 1, overflow: getComputedStyle(h2).textOverflow,
    }))
    expect(title.text).toBe('Dimmer 1')
    expect(title.clipped).toBe(false)
    expect(title.overflow).not.toBe('ellipsis')
    expect(await dialog.evaluate(d => d.scrollWidth - d.clientWidth)).toBeLessThanOrEqual(0)
    expect(await overflow()).toBeLessThanOrEqual(0)
  })
}

test('the wizard previews without leaving a submain in the house', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, {
    data: { name: `E2E ghost ${Date.now()}` },
  })).json()

  await page.goto(`/projects/${project.id}/submains/new`)
  await page.getByRole('button', { name: /more dimmed lighting/i }).tap()
  await expect(page.getByText(/1 dimmer/i)).toBeVisible()

  // Back out.
  await page.getByRole('link', { name: /back to house/i }).tap()
  await expect(page.getByRole('heading', { level: 1, name: project.name })).toBeVisible()

  const submains = await (await request.get(`${API}/projects/${project.id}/submains`)).json()
  expect(submains).toEqual([])
})

test('a failed Generate leaves no submain in the house, and a retry makes exactly one', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, {
    data: { name: `E2E orphan ${Date.now()}` },
  })).json()
  const submainsOf = async () => (await request.get(`${API}/projects/${project.id}/submains`)).json()

  // The generator falls over on the one call that creates and lays out the submain.
  await page.route('**/api/projects/*/submains', r => r.request().method() === 'POST'
    ? r.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ title: 'Generator down' }) })
    : r.continue())

  await page.goto(`/projects/${project.id}/submains/new`)
  await page.getByLabel(/submain name/i).fill('Kept inputs')
  await page.getByRole('button', { name: /more dimmed lighting/i }).tap()
  const generate = page.getByRole('button', { name: /generate panel/i })
  await expect(generate).toBeEnabled()
  await generate.tap()

  await expect(page.getByRole('status')).toContainText(/not generated: generator down/i)
  await expect(page.getByLabel(/submain name/i)).toHaveValue('Kept inputs')
  expect(await submainsOf()).toEqual([])

  // The server is back: the same tap creates one submain, with its panel, and goes to it.
  await page.unroute('**/api/projects/*/submains')
  await generate.tap()
  await expect(page.getByRole('heading', { level: 1, name: 'Kept inputs' })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Dimmer 1/ })).toBeVisible()
  const submains = await submainsOf()
  expect(submains).toHaveLength(1)
  expect(submains[0].deviceCount).toBeGreaterThan(0)
})

test('Return adds a house and the typed name is kept when the API refuses', async ({ page }) => {
  await page.goto('/')
  const name = `E2E Return ${Date.now()}`
  await page.getByLabel('House name').fill(name)
  await page.keyboard.press('Enter')

  await expect(page.getByRole('link', { name: new RegExp(name) })).toBeVisible()

  // A blank name is refused by the API: the error sits by the field, and the page stays.
  await page.getByLabel('Address').fill('Somewhere')
  await page.getByRole('button', { name: /add house/i }).tap()
  await expect(page.getByText(/name is required/i)).toBeVisible()
  await expect(page.getByLabel('Address')).toHaveValue('Somewhere')
})

test('a failed preview says so, keeps the counts and holds Generate back until it succeeds', async ({ page, request }) => {
  const project = await (await request.post(`${API}/projects`, {
    data: { name: `E2E preview ${Date.now()}` },
  })).json()
  const pageErrors: string[] = []
  page.on('pageerror', e => pageErrors.push(e.message))

  // What the proxy answers when the API is down: an HTML page, not JSON.
  await page.route('**/api/projects/*/design/preview', r =>
    r.fulfill({ status: 502, contentType: 'text/html', body: '<html>Bad Gateway</html>' }))
  await page.goto(`/projects/${project.id}/submains/new`)
  await page.getByRole('button', { name: /more dimmed lighting/i }).tap()

  await expect(page.getByRole('alert')).toContainText(/not responding \(502\)/i)
  await expect(page.getByRole('button', { name: 'Generate panel' })).toBeDisabled()
  await expect(page.getByText(/could not be checked/i)).toBeVisible()
  await expect(page.getByRole('textbox', { name: 'Dimmed lighting' })).toHaveValue('1')

  await page.unroute('**/api/projects/*/design/preview')
  await page.getByRole('button', { name: /try again/i }).tap()

  await expect(page.getByRole('button', { name: 'Generate panel' })).toBeEnabled()
  await expect(page.getByText(/1 dimmer/i)).toBeVisible()
  expect(pageErrors).toEqual([])
})

test('an HTML error page from the proxy leaves the app standing, with the typed house kept', async ({ page }) => {
  const pageErrors: string[] = []
  page.on('pageerror', e => pageErrors.push(e.message))

  await page.goto('/')
  await page.route('**/api/projects', r => r.request().method() === 'POST'
    ? r.fulfill({ status: 502, contentType: 'text/html', body: '<html>Bad Gateway</html>' })
    : r.continue())
  await page.getByLabel('House name').fill('Keep me')
  await page.getByLabel('Address').fill('Keep me too')
  await page.getByRole('button', { name: /add house/i }).tap()

  await expect(page.getByRole('status')).toContainText(/not added: the server is not responding \(502\)/i)
  await expect(page.getByLabel('House name')).toHaveValue('Keep me')
  await expect(page.getByLabel('Address')).toHaveValue('Keep me too')
  await expect(page.getByRole('main')).toBeVisible()
  expect(pageErrors).toEqual([])
})

test('every screen has a title of its own and the Home Screen metadata', async ({ page }) => {
  await page.goto('/')
  await expect(page).toHaveTitle(/Houses · House Panel Designer/)

  const meta = await page.evaluate(() => ({
    viewport: document.querySelector('meta[name=viewport]')?.getAttribute('content'),
    themeColors: document.querySelectorAll('meta[name=theme-color]').length,
    appleIcon: document.querySelector('link[rel=apple-touch-icon]')?.getAttribute('href'),
    manifest: document.querySelector('link[rel=manifest]')?.getAttribute('href'),
    capable: document.querySelector('meta[name=apple-mobile-web-app-capable]')?.getAttribute('content'),
  }))
  expect(meta.viewport).toContain('viewport-fit=cover')
  expect(meta.themeColors).toBe(2)
  expect(meta.capable).toBe('yes')

  for (const href of [meta.appleIcon!, meta.manifest!]) {
    const response = await page.request.get(href!)
    expect(response.ok(), href).toBe(true)
  }

  await page.goto('/catalogue')
  await expect(page).toHaveTitle(/Catalogue · House Panel Designer/)
})
