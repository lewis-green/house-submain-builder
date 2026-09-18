import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BomPage } from '../src/routes/BomPage'
import type { BomView } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const bom: BomView = {
  lines: [
    { catalogueId: 'a', partNumber: 'SHELLY-DIM', description: 'Shelly Pro Dimmer 2PM', quantity: 3, panelMounted: true },
    { catalogueId: 'b', partNumber: '2003-7646', description: 'WAGO TOPJOB S', quantity: 27, panelMounted: true },
    { catalogueId: 'c', partNumber: 'DRIVER-100', description: '24V LED driver', quantity: 1, panelMounted: false },
  ],
}

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/submains/s1/bom']}>
      <Routes><Route path="/submains/:submainId/bom" element={<BomPage />} /></Routes>
    </MemoryRouter>,
  )

describe('BomPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists parts with their quantities', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(bom)))

    renderPage()

    expect(await screen.findByText('Shelly Pro Dimmer 2PM')).toBeInTheDocument()
    expect(screen.getByText('× 27')).toBeInTheDocument()
    expect(screen.getByText('2003-7646')).toBeInTheDocument()
  })

  it('shows no money anywhere', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(bom)))

    renderPage()
    await screen.findByText('Shelly Pro Dimmer 2PM')

    expect(document.body.textContent).not.toMatch(/£|total|cost|price/i)
  })

  it('marks a part that is not mounted in the panel', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(bom)))

    renderPage()

    expect(await screen.findByText('external')).toBeInTheDocument()
  })

  it('explains that nothing has been issued rather than showing a bare error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Not Found' }, 404)))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/nothing has been issued yet/i)
  })

  it('says so when there is nothing to order', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ lines: [] })))

    renderPage()

    expect(await screen.findByText(/nothing to order/i)).toBeInTheDocument()
  })

  it('offers the csv download for the scope it is showing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(bom)))

    renderPage()

    const link = await screen.findByRole('link', { name: /download csv/i })
    expect(link).toHaveAttribute('href', '/api/submains/s1/bom.csv')
  })

  it('goes back to the panel it belongs to', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(bom)))

    renderPage()

    const back = await screen.findByRole('link', { name: /back/i })
    expect(back).toHaveAttribute('href', '/submains/s1/panel')
  })
})
