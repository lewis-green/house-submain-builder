import { expect, test } from '@playwright/test'
import { API, seedSubmain, threeDimmed } from './fixtures'

test('a location is added on the house page, a panel placed in it from its edit form, and the house lists it there', async ({ page, request }) => {
  const { submainId } = await seedSubmain(request, threeDimmed)
  const { projectId } = await (await request.get(`${API}/submains/${submainId}`)).json()

  await page.goto(`/projects/${projectId}`)
  // No locations yet: the house keeps its two lists.
  await expect(page.getByRole('region', { name: 'Automation panels' })).toBeVisible()
  await page.getByRole('button', { name: 'Add location' }).tap()
  const sheet = page.getByRole('dialog', { name: 'Add location' })
  await sheet.getByLabel('Location name').fill('Plant room')
  await sheet.getByRole('button', { name: 'Add location' }).tap()

  // Not placed yet, so the panel waits under Not placed, and the new location is empty.
  const plantRoom = page.getByRole('region', { name: 'Plant room' })
  await expect(plantRoom.getByText('Nothing here yet')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Not placed' }).getByRole('link', { name: /E2E automation panel/ })).toBeVisible()

  await page.goto(`/panels/${submainId}/edit`)
  await page.getByLabel('Location').selectOption({ label: 'Plant room' })
  const save = page.getByRole('button', { name: /save and rebuild/i })
  await expect(save).toBeEnabled()
  await save.tap()

  await expect(page).toHaveURL(new RegExp(`/panels/${submainId}$`))
  await expect(page.getByText(/^Plant room · \d+ rows of/)).toBeVisible()

  await page.getByRole('link', { name: 'Back to house' }).tap()
  await expect(plantRoom.getByRole('link', { name: /E2E automation panel/ })).toHaveAttribute('href', `/panels/${submainId}`)
  await expect(page.getByRole('region', { name: 'Not placed' })).toHaveCount(0)
})
