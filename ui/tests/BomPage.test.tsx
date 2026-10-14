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

    // An empty state inside the page, with a way on, rather than an alert that replaces it.
    expect(await screen.findByText(/nothing has been issued yet/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: /go to the panel/i })).toHaveAttribute('href', '/submains/s1/panel')
    expect(screen.getByRole('link', { name: /back/i })).toHaveAttribute('href', '/submains/s1/panel')
  })

  it('says so when there is nothing to order', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ lines: [] })))

    renderPage()

    expect(await screen.findByText(/nothing to order/i)).toBeInTheDocument()
  })

  it('says whose issued drawing is empty, in a whole sentence', async () => {
    // A fresh Response per call: a body reads once, and two pages are rendered here.
    vi.stubGlobal('fetch', vi.fn(async () => json({ lines: [] })))

    renderPage()
    expect(await screen.findByText('The issued panel has no parts in it.')).toBeInTheDocument()

    render(
      <MemoryRouter initialEntries={['/racks/r1/bom']}>
        <Routes><Route path="/racks/:rackId/bom" element={<BomPage />} /></Routes>
      </MemoryRouter>,
    )
    expect(await screen.findByText('The issued rack has no parts in it.')).toBeInTheDocument()
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

  it('lists a rack\'s parts from the rack\'s address, with the csv and the way back to the rack', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => url === '/api/racks/r1/bom'
      ? json({ lines: [{ catalogueId: 'k', partNumber: 'T-KIT', description: 'Rack kit', quantity: 1, panelMounted: true }] })
      : json({ title: 'Not Found' }, 404)))

    render(
      <MemoryRouter initialEntries={['/racks/r1/bom']}>
        <Routes><Route path="/racks/:rackId/bom" element={<BomPage />} /></Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Rack kit')).toBeInTheDocument()
    expect(screen.getByText('T-KIT')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /download csv/i })).toHaveAttribute('href', '/api/racks/r1/bom.csv')
    const back = screen.getByRole('link', { name: /back/i })
    expect(back).toHaveTextContent('Rack')
    expect(back).toHaveAttribute('href', '/racks/r1')
  })
})
