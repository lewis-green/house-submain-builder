import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { RackPage } from '../src/routes/RackPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack = {
  id: 'r1', projectId: 'p1', name: 'Comms cupboard', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet', heightU: 12,
  usedU: 1, fedFromSubmainId: 's1', fedFromSubmainName: 'Ground floor', notes: null, layoutVersion: 3,
}
const groups = [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }]
const switch1: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 'sw48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, portGroups: groups, poeBudgetW: 720, ports: [],
}
const layout = (items: RackItemResponse[] = [switch1], layoutVersion = 3): RackLayoutResponse =>
  ({ rackId: 'r1', heightU: 12, freeU: 12 - items.length, layoutVersion, items })
const catalogue = [
  { id: 'sw48', manufacturer: 'Ubiquiti', model: 'USW-Pro-Max-48-PoE', partNumber: 'USW', kind: 'Switch', heightU: 1, portGroups: groups, poeBudgetW: 720, requiredAccessoryId: null, active: true, description: 'Ubiquiti USW-Pro-Max-48-PoE', portCount: 6 },
  { id: 'pp24', manufacturer: 'Generic', model: '24-port patch panel', partNumber: 'PP', kind: 'PatchPanel', heightU: 1, portGroups: [{ name: 'RJ45', from: 1, to: 24, poeTick: true }], poeBudgetW: null, requiredAccessoryId: null, active: true, description: 'Generic 24-port patch panel', portCount: 24 },
  { id: 'kit', manufacturer: 'Ubiquiti', model: 'Rack kit', partNumber: 'KIT', kind: 'Accessory', heightU: 0, portGroups: [], poeBudgetW: null, requiredAccessoryId: null, active: true, description: 'Ubiquiti Rack kit', portCount: 0 },
  { id: 'old', manufacturer: 'Ubiquiti', model: 'Retired switch', partNumber: 'OLD', kind: 'Switch', heightU: 1, portGroups: groups, poeBudgetW: null, requiredAccessoryId: null, active: false, description: 'Ubiquiti Retired switch', portCount: 6 },
]

function stub(handlers: Partial<Record<string, (init?: RequestInit) => Response>> = {}, current: () => RackLayoutResponse = () => layout()) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url.split('?')[0]}`
    if (handlers[key]) return handlers[key]!(init)
    if (url === '/api/racks/r1') return json(rack)
    if (url === '/api/racks/r1/layout') return json(current())
    if (url === '/api/catalogue/rack-device-types') return json(catalogue)
    if (url === '/api/catalogue/rack-types') return json([{ id: 'c12', heightU: 12, description: 'Generic 12U wall cabinet' }])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const show = () => render(
  <MemoryRouter initialEntries={['/racks/r1']}>
    <Routes><Route path="/racks/:rackId" element={<RackPage />} /></Routes>
  </MemoryRouter>,
)

describe('RackPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('draws every U and each item where the server put it, with the cabinet and feed in the title block', async () => {
    stub()
    show()

    expect(await screen.findByRole('heading', { name: 'Comms cupboard' })).toBeInTheDocument()
    expect(screen.getByText('Generic 12U wall cabinet · fed from Ground floor')).toBeInTheDocument()
    const drawing = screen.getByRole('group', { name: 'Rack, 12U, 11U free' })
    expect(within(drawing).getByText('U12')).toBeInTheDocument()
    expect(within(drawing).getByText('U1')).toBeInTheDocument()
    expect(within(drawing).getByRole('button', { name: /^Switch 1, Ubiquiti USW-Pro-Max-48-PoE, U12/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Bill of materials' })).toHaveAttribute('href', '/racks/r1/bom')
  })

  it('opens an item\'s sheet on a tap', async () => {
    stub()
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))

    expect(screen.getByRole('dialog', { name: /Switch 1/ })).toBeInTheDocument()
    expect(screen.getByLabelText('Port 1 label')).toBeInTheDocument()
  })

  it('offers only active switches and patch panels, adds the one picked and redraws', async () => {
    const fetchMock = stub({
      'POST /api/racks/r1/items': () => json(layout([switch1, { ...switch1, id: 'i2', rackDeviceTypeId: 'pp24', kind: 'PatchPanel', label: 'Patch panel 1', topU: 11, model: 'Generic 24-port patch panel' }], 4), 201),
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add to rack' }))
    const sheet = screen.getByRole('dialog', { name: 'Add to rack' })
    expect(within(sheet).queryByText('Rack kit')).not.toBeInTheDocument()
    expect(within(sheet).queryByText('Retired switch')).not.toBeInTheDocument()
    await userEvent.click(within(sheet).getByRole('button', { name: /24-port patch panel/ }))

    expect(await screen.findByRole('button', { name: /^Patch panel 1/ })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Add to rack' })).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Patch panel 1 added in U11')
    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body))).toEqual({ rackDeviceTypeId: 'pp24' })
  })

  it('shows a refusal in the sheet, in the server\'s words', async () => {
    stub({ 'POST /api/racks/r1/items': () => json({ message: 'No room in this rack: 0U free, and the switch needs 1U.' }, 422) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add to rack' }))
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Add to rack' })).getByRole('button', { name: /USW-Pro-Max-48-PoE/ }))

    expect(await screen.findByText(/No room in this rack: 0U free/)).toBeInTheDocument()
  })

  it('invites a first item when the rack is empty and offers no PDF yet', async () => {
    stub({}, () => layout([]))
    show()

    expect(await screen.findByText('Nothing in this rack yet')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /issue pdf/i })).not.toBeInTheDocument()
  })

  it('moves an item from its sheet against the layout it was drawn from', async () => {
    const fetchMock = stub({ 'PATCH /api/rack-items/i1/position': () => json({ layoutVersion: 4 }) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Move to'), '10')

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/rack-items/i1/position')!
    expect(JSON.parse(String(init!.body))).toEqual({ topU: 10, basedOnLayoutVersion: 3 })
    expect(await screen.findByRole('status')).toHaveTextContent('Moved to U10')
  })

  it('reloads instead of failing when the rack changed elsewhere', async () => {
    let version = 3
    const fetchMock = stub(
      { 'PATCH /api/rack-items/i1/position': () => { version = 5; return json({ message: 'This rack changed since you loaded it.', currentLayoutVersion: 5 }, 409) } },
      () => layout([switch1], version),
    )
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Move to'), '10')

    expect(await screen.findByRole('status')).toHaveTextContent(/changed elsewhere/)
    expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/layout').length).toBeGreaterThan(1)
  })

  it('removes an item with the layout version it was drawn from', async () => {
    const fetchMock = stub({ 'DELETE /api/rack-items/i1': () => json(layout([], 4)) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Remove from rack' }))
    await userEvent.click(screen.getByRole('button', { name: 'Yes, remove it' }))

    expect(await screen.findByText('Nothing in this rack yet')).toBeInTheDocument()
    expect(fetchMock.mock.calls.map(c => c[0])).toContain('/api/rack-items/i1?basedOnLayoutVersion=3')
  })

  it('saves a port label and makes the PDF ask again whether it is current', async () => {
    const fetchMock = stub({ 'PUT /api/rack-items/i1/ports/1': () => json({ number: 1, label: 'Kitchen AP', poe: false }) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    const revisionsBefore = fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length
    await userEvent.type(screen.getByLabelText('Port 1 label'), 'Kitchen AP{Enter}')

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/rack-items/i1/ports/1')!
    expect(JSON.parse(String(init!.body))).toEqual({ label: 'Kitchen AP', poe: false })
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length).toBeGreaterThan(revisionsBefore))
  })

  it('saves the rack settings and makes the PDF ask again whether it is current', async () => {
    // Stateful, as the server is: the reload after the save must see the new name.
    let name = rack.name
    const fetchMock = stub({
      'PATCH /api/racks/r1': () => { name = 'Plant room'; return json({ ...rack, name }) },
      'GET /api/racks/r1': () => json({ ...rack, name }),
    })
    show()

    await screen.findByRole('button', { name: /^Switch 1/ })
    const revisionsBefore = fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length
    await userEvent.clear(screen.getByLabelText('Rack name'))
    await userEvent.type(screen.getByLabelText('Rack name'), 'Plant room')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(await screen.findByRole('heading', { name: 'Plant room' })).toBeInTheDocument()
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/revisions').length).toBeGreaterThan(revisionsBefore))
  })

  it('reloads the rack when a settings save finds it changed elsewhere', async () => {
    const fetchMock = stub({ 'PATCH /api/racks/r1': () => json({ message: 'This rack changed since you loaded it.', currentLayoutVersion: 5 }, 409) })
    show()

    await screen.findByRole('button', { name: /^Switch 1/ })
    const layoutsBefore = fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/layout').length
    await userEvent.type(screen.getByLabelText('Rack name'), ' 2')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(await screen.findByRole('status')).toHaveTextContent('This rack changed elsewhere — reloading')
    await vi.waitFor(() =>
      expect(fetchMock.mock.calls.filter(([url]) => url === '/api/racks/r1/layout').length).toBeGreaterThan(layoutsBefore))
  })
})
