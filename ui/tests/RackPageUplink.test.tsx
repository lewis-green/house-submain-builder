import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { HouseSwitch, RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { RackPage } from '../src/routes/RackPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack = {
  id: 'r1', projectId: 'p1', name: 'Comms', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet', heightU: 12,
  usedU: 1, fedFromSubmainId: null, fedFromSubmainName: null, notes: null, layoutVersion: 3, locationId: null,
}
const groups = [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }]
const switch1: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 'sw48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, portGroups: groups, poeBudgetW: 720, ports: [], uplink: null, downlinks: [],
}
const layout = (item: RackItemResponse = switch1, layoutVersion = 3): RackLayoutResponse =>
  ({ rackId: 'r1', heightU: 12, freeU: 11, layoutVersion, items: [item] })
const switches: HouseSwitch[] = [
  { id: 'i1', label: 'Switch 1', model: 'Ubiquiti USW-Pro-Max-48-PoE', rackId: 'r1', rackName: 'Comms', locationId: null, locationName: null, portGroups: groups, uplinkPort: null, downlinks: [], uplinkToItemId: null },
  { id: 'agg', label: 'Switch 1', model: 'Ubiquiti USW-Aggregation', rackId: 'r2', rackName: 'Core', locationId: null, locationName: null, portGroups: [{ name: 'SFP+', from: 1, to: 8, poeTick: false }], uplinkPort: null, downlinks: [], uplinkToItemId: null },
]
const linked: RackItemResponse = {
  ...switch1,
  uplink: { port: 5, toItemId: 'agg', toPort: 3, toText: null, toLabel: 'Switch 1', toRackId: 'r2', toRackName: 'Core', toLocationName: null },
}

function stub(handlers: Partial<Record<string, (init?: RequestInit) => Response | Promise<Response>>>, current: () => RackLayoutResponse = () => layout()) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const key = `${init?.method ?? 'GET'} ${url.split('?')[0]}`
    if (handlers[key]) return handlers[key]!(init)
    if (url === '/api/racks/r1') return json(rack)
    if (url === '/api/racks/r1/layout') return json(current())
    if (url === '/api/projects/p1/switches') return json(switches)
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

const calls = (fetchMock: ReturnType<typeof stub>, url: string) => fetchMock.mock.calls.filter(([u]) => u === url).length

describe('RackPage uplinks', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('saves an uplink into a switch in another rack, says so on the port and asks again whether the PDF is current', async () => {
    const fetchMock = stub({ 'PUT /api/rack-items/i1/uplink': () => json(layout(linked)) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    await userEvent.selectOptions(await screen.findByLabelText('Switch'), 'agg')
    const revisionsBefore = calls(fetchMock, '/api/racks/r1/revisions')
    await userEvent.selectOptions(screen.getByLabelText('Its port'), '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save uplink' }))

    const [, init] = fetchMock.mock.calls.find(([url]) => url === '/api/rack-items/i1/uplink')!
    expect(JSON.parse(String(init!.body))).toEqual({ port: 5, toItemId: 'agg', toPort: 3 })
    expect(await screen.findByLabelText('Port 5 label')).toHaveAccessibleDescription('Uplink to Switch 1 · Core · port 3')
    expect(screen.getByRole('status')).toHaveTextContent('Uplink saved')
    await vi.waitFor(() => expect(calls(fetchMock, '/api/racks/r1/revisions')).toBeGreaterThan(revisionsBefore))
  })

  it('removes an uplink', async () => {
    const fetchMock = stub({ 'DELETE /api/rack-items/i1/uplink': () => json(layout()) }, () => layout(linked))
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.click(await screen.findByRole('button', { name: 'Remove uplink' }))

    expect(fetchMock.mock.calls.some(([url, i]) => url === '/api/rack-items/i1/uplink' && i?.method === 'DELETE')).toBe(true)
    expect(await screen.findByText(/No uplink recorded/)).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Uplink removed')
  })

  /** Another rack may have removed the switch this one went to since the screen loaded. */
  it('fetches the rack again when a switch is opened, so a link cleared elsewhere is not shown', async () => {
    let current = layout(linked)
    stub({}, () => current)
    show()

    await screen.findByRole('button', { name: /^Switch 1/ })
    current = layout()
    await userEvent.click(screen.getByRole('button', { name: /^Switch 1/ }))

    expect(await screen.findByText(/No uplink recorded/)).toBeInTheDocument()
    expect(screen.queryByText(/Uplink to Switch 1/)).not.toBeInTheDocument()
  })

  it('reloads when the switch was removed elsewhere before the save', async () => {
    const fetchMock = stub({ 'PUT /api/rack-items/i1/uplink': () => json({ title: 'Not Found' }, 404) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'text')
    await userEvent.type(screen.getByLabelText('What it plugs into'), 'UDM Pro')
    const layoutsBefore = calls(fetchMock, '/api/racks/r1/layout')
    await userEvent.click(screen.getByRole('button', { name: 'Save uplink' }))

    expect(await screen.findByRole('status')).toHaveTextContent(/no longer in this rack/)
    await vi.waitFor(() => expect(calls(fetchMock, '/api/racks/r1/layout')).toBeGreaterThan(layoutsBefore))
  })
  /** A refetch that set off before a newer layout arrived must not undo it. */
  it('keeps the newer layout when the one fetched on opening a switch comes back older', async () => {
    let release!: (response: Response) => void
    let layoutCalls = 0
    stub({
      'GET /api/racks/r1/layout': () => {
        layoutCalls += 1
        // The first load is the rack as it is now; the refetch on opening the
        // switch is held back and comes back as it was a version before.
        return layoutCalls === 1 ? json(layout(linked, 4)) : new Promise<Response>(done => { release = done })
      },
    })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await vi.waitFor(() => expect(layoutCalls).toBe(2))
    release(json(layout(switch1, 3)))

    await new Promise(done => setTimeout(done, 50))
    expect(screen.getByLabelText('Port 5 label')).toHaveAccessibleDescription('Uplink to Switch 1 · Core · port 3')
  })

  it('tries the house\'s switches again from the sheet when they did not load', async () => {
    let failing = true
    stub({ 'GET /api/projects/p1/switches': () => (failing ? json({}, 500) : json(switches)) })
    show()

    await userEvent.click(await screen.findByRole('button', { name: /^Switch 1/ }))
    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    expect(await screen.findByRole('alert')).toHaveTextContent(/did not load/)
    failing = false
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('option', { name: 'Switch 1 · Ubiquiti USW-Aggregation' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
