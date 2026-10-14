import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectPage } from '../src/routes/ProjectPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack = {
  id: 'r1', projectId: 'p1', name: 'Comms cupboard', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet',
  heightU: 12, usedU: 4, fedFromSubmainId: 's1', fedFromSubmainName: 'Ground floor', notes: null, layoutVersion: 3,
}

function stub(racks: unknown[]) {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url === '/api/projects/p1') return json({ id: 'p1', name: 'Willow House', address: null, notes: null, submainCount: 1 })
    if (url === '/api/projects/p1/submains') return json([{ id: 's1', name: 'Ground floor', deviceCount: 3, circuitCount: 8 }])
    if (url === '/api/projects/p1/racks') return json(racks)
    return json([])
  }))
}

const show = () => render(
  <MemoryRouter initialEntries={['/projects/p1']}>
    <Routes><Route path="/projects/:projectId" element={<ProjectPage />} /></Routes>
  </MemoryRouter>,
)

describe('ProjectPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists automation panels and network racks side by side, each with its own Add button', async () => {
    stub([rack])
    show()

    const submains = await screen.findByRole('region', { name: 'Automation panels' })
    expect(within(submains).getByRole('link', { name: 'Add automation panel' })).toHaveAttribute('href', '/projects/p1/panels/new')

    const racks = screen.getByRole('region', { name: 'Network racks' })
    expect(within(racks).getByRole('link', { name: 'Add rack' })).toHaveAttribute('href', '/projects/p1/racks/new')
    const row = within(racks).getByRole('link', { name: /Comms cupboard/ })
    expect(row).toHaveAttribute('href', '/racks/r1')
    expect(row).toHaveTextContent('Generic 12U wall cabinet · 4 of 12U used')
    expect(row).toHaveTextContent('Fed from Ground floor')
  })

  it('invites a first rack when the house has none', async () => {
    stub([])
    show()

    const racks = await screen.findByRole('region', { name: 'Network racks' })
    expect(within(racks).getByText('No network racks yet')).toBeInTheDocument()
  })

  it('keeps the two lists while the house has no locations, and offers to add one', async () => {
    stub([rack])
    show()

    await screen.findByRole('region', { name: 'Automation panels' })
    expect(screen.getByRole('region', { name: 'Network racks' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add location' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Not placed' })).not.toBeInTheDocument()
  })
})

describe('ProjectPage with locations', () => {
  beforeEach(() => vi.restoreAllMocks())

  const plant = { id: 'l1', name: 'Plant room', notes: 'By the boiler', sequence: 0 }
  const garage = { id: 'l2', name: 'Garage', notes: null, sequence: 1 }
  const panels = [
    { id: 's1', name: 'Ground floor', deviceCount: 3, circuitCount: 8, locationId: 'l1' },
    { id: 's2', name: 'Loft', deviceCount: 0, circuitCount: 0, locationId: null },
  ]

  function stubHouse(locations: unknown[], handlers: Record<string, (init?: RequestInit) => Response> = {}) {
    let current = locations
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      const key = `${init?.method ?? 'GET'} ${url}`
      if (handlers[key]) return handlers[key](init)
      if (url === '/api/projects/p1') return json({ id: 'p1', name: 'Willow House', address: null, notes: null, submainCount: 2 })
      if (url === '/api/projects/p1/submains') return json(panels)
      if (url === '/api/projects/p1/racks') return json([{ ...rack, locationId: 'l1' }])
      if (url === '/api/projects/p1/locations') return json(current)
      return json([])
    })
    vi.stubGlobal('fetch', fetchMock)
    return { fetchMock, setLocations: (next: unknown[]) => { current = next } }
  }

  const bodyOf = (fetchMock: ReturnType<typeof vi.fn>, method: string, url: string) => {
    const call = fetchMock.mock.calls.find(c => c[0] === url && (c[1] as RequestInit | undefined)?.method === method)
    expect(call, `no ${method} to ${url}`).toBeDefined()
    return JSON.parse(String((call![1] as RequestInit).body))
  }

  it('groups the panels and racks by location, in order, with what is not placed last', async () => {
    stubHouse([plant, garage])
    show()

    const plantRoom = await screen.findByRole('region', { name: 'Plant room' })
    const panel = within(plantRoom).getByRole('link', { name: /^Automation panel\s*Ground floor/ })
    expect(panel).toHaveAttribute('href', '/panels/s1')
    expect(panel).toHaveTextContent('Automation panel')
    const rackRow = within(plantRoom).getByRole('link', { name: /Comms cupboard/ })
    expect(rackRow).toHaveAttribute('href', '/racks/r1')
    expect(rackRow).toHaveTextContent('Rack')

    expect(within(screen.getByRole('region', { name: 'Garage' })).getByText('Nothing here yet')).toBeInTheDocument()
    const unplaced = screen.getByRole('region', { name: 'Not placed' })
    expect(within(unplaced).getByRole('link', { name: /Loft/ })).toHaveAttribute('href', '/panels/s2')

    const order = screen.getAllByRole('heading', { level: 2 }).map(h => h.textContent)
    expect(order).toEqual(['Plant room', 'Garage', 'Not placed'])
    expect(screen.queryByRole('region', { name: 'Automation panels' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Add automation panel' })).toHaveAttribute('href', '/projects/p1/panels/new')
    expect(screen.getByRole('link', { name: 'Add rack' })).toHaveAttribute('href', '/projects/p1/racks/new')
  })

  it('leaves Not placed out when everything is placed', async () => {
    stubHouse([plant], {
      'GET /api/projects/p1/submains': () => json([panels[0]]),
    })
    show()

    await screen.findByRole('region', { name: 'Plant room' })
    expect(screen.queryByRole('region', { name: 'Not placed' })).not.toBeInTheDocument()
  })

  it('adds a location from a sheet and shows it', async () => {
    const { fetchMock, setLocations } = stubHouse([], {
      'POST /api/projects/p1/locations': () => {
        setLocations([garage])
        return json(garage, 201)
      },
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add location' }))
    const sheet = screen.getByRole('dialog', { name: 'Add location' })
    await userEvent.type(within(sheet).getByLabelText('Location name'), 'Garage')
    await userEvent.click(within(sheet).getByRole('button', { name: 'Add location' }))

    expect(await screen.findByRole('region', { name: 'Garage' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(bodyOf(fetchMock, 'POST', '/api/projects/p1/locations')).toEqual({ name: 'Garage' })
    expect(screen.getByRole('status')).toHaveTextContent('Added Garage')
  })

  it('shows a name already used in the house under the field, and keeps the sheet open', async () => {
    stubHouse([plant], {
      'POST /api/projects/p1/locations': () => json({
        title: 'One or more validation errors occurred.',
        errors: { name: ['There is already a location called Plant room in this house.'] },
      }, 400),
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add location' }))
    const sheet = screen.getByRole('dialog', { name: 'Add location' })
    await userEvent.type(within(sheet).getByLabelText('Location name'), 'plant room')
    await userEvent.click(within(sheet).getByRole('button', { name: 'Add location' }))

    expect(await within(sheet).findByText('There is already a location called Plant room in this house.')).toBeInTheDocument()
    expect(within(sheet).getByLabelText('Location name')).toHaveValue('plant room')
  })

  it('renames a location, keeping its notes', async () => {
    const { fetchMock, setLocations } = stubHouse([plant, garage], {
      'PATCH /api/locations/l1': () => {
        setLocations([{ ...plant, name: 'Boiler room' }, garage])
        return json({ ...plant, name: 'Boiler room' })
      },
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Rename or remove Plant room' }))
    const sheet = screen.getByRole('dialog', { name: 'Plant room' })
    const field = within(sheet).getByLabelText('Location name')
    await userEvent.clear(field)
    await userEvent.type(field, 'Boiler room')
    await userEvent.click(within(sheet).getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('region', { name: 'Boiler room' })).toBeInTheDocument()
    expect(bodyOf(fetchMock, 'PATCH', '/api/locations/l1')).toEqual({ name: 'Boiler room', notes: 'By the boiler' })
  })

  it('removes a location after asking, saying what is in it stays in the house', async () => {
    const { fetchMock, setLocations } = stubHouse([plant, garage], {
      'DELETE /api/locations/l1': () => {
        setLocations([garage])
        return new Response(null, { status: 204 })
      },
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Rename or remove Plant room' }))
    const sheet = screen.getByRole('dialog', { name: 'Plant room' })
    await userEvent.click(within(sheet).getByRole('button', { name: 'Remove location' }))
    const question = within(sheet).getByRole('group', { name: /Remove Plant room\?/ })
    expect(question).toHaveTextContent('Its automation panels and racks stay in the house, not placed.')
    await userEvent.click(within(question).getByRole('button', { name: 'Yes, remove it' }))

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Plant room' })).not.toBeInTheDocument())
    expect(fetchMock.mock.calls.some(c => c[0] === '/api/locations/l1' && (c[1] as RequestInit)?.method === 'DELETE')).toBe(true)
  })
})
