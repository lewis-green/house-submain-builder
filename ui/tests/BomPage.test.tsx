import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BomPage } from '../src/routes/BomPage'
import type { BomView } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const priced: BomView = {
  lines: [
    { catalogueId: 'a', partNumber: 'SHELLY-DIM', description: 'Shelly Pro Dimmer 2PM', quantity: 3, unitCost: 60, lineTotal: 180 },
    { catalogueId: 'b', partNumber: '2003-7646', description: 'WAGO TOPJOB S', quantity: 27, unitCost: 1.5, lineTotal: 40.5 },
  ],
  total: 220.5,
  unpricedLines: 0,
  priced: true,
}

const unpriced: BomView = {
  lines: [{ catalogueId: 'a', partNumber: 'SHELLY-DIM', description: 'Shelly Pro Dimmer 2PM', quantity: 3, unitCost: 0, lineTotal: 0 }],
  total: null,
  unpricedLines: 1,
  priced: false,
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
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(priced)))

    renderPage()

    expect(await screen.findByText('Shelly Pro Dimmer 2PM')).toBeInTheDocument()
    expect(screen.getByText('× 27')).toBeInTheDocument()
    expect(screen.getByText('2003-7646')).toBeInTheDocument()
  })

  it('shows a total when everything is priced', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(priced)))

    renderPage()

    expect(await screen.findByText(/Total £220.50/)).toBeInTheDocument()
  })

  it('says not priced rather than showing a zero total', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(unpriced)))

    renderPage()

    expect(await screen.findByText(/Not priced \(1 of 1 parts have no cost\)/)).toBeInTheDocument()
    expect(screen.queryByText(/£0.00/)).not.toBeInTheDocument()
  })

  it('explains that nothing has been issued rather than showing a bare error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Not Found' }, 404)))

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/nothing has been issued yet/i)
  })

  it('offers the csv download for the scope it is showing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(priced)))

    renderPage()

    const link = await screen.findByRole('link', { name: /download csv/i })
    expect(link).toHaveAttribute('href', '/api/submains/s1/bom.csv')
  })
})
