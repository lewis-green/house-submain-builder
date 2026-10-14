import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RackCatalogueSections } from '../src/routes/RackCatalogueSections'
import { StatusRegion } from '../src/components/StatusRegion'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const cabinet = { id: 'c12', manufacturer: 'Generic', model: '12U wall cabinet', partNumber: 'ASSUMED-CAB-12U-WALL', heightU: 12, mounting: 'Wall', description: 'Generic 12U wall cabinet' }
const groups = [{ name: '1G', from: 1, to: 32, poeTick: true }, { name: '2.5G', from: 33, to: 48, poeTick: true }, { name: 'SFP+', from: 49, to: 52, poeTick: false }]
const sw = {
  id: 'sw48', manufacturer: 'Ubiquiti', model: 'USW-Pro-Max-48-PoE', partNumber: 'USW-Pro-Max-48-PoE', kind: 'Switch',
  heightU: 1, portGroups: groups, poeBudgetW: 720, requiredAccessoryId: null, active: true,
  description: 'Ubiquiti USW-Pro-Max-48-PoE', portCount: 52,
}

function stub() {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PATCH') return json({ ...sw, ...JSON.parse(String(init.body)) })
    if (url === '/api/catalogue/rack-types') return json([cabinet])
    if (url === '/api/catalogue/rack-device-types') return json([sw])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('RackCatalogueSections', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists rack cabinets and rack items with their part numbers', async () => {
    stub()
    render(<RackCatalogueSections />)

    const cabinets = await screen.findByRole('region', { name: 'Rack cabinets' })
    expect(within(cabinets).getByText('Generic 12U wall cabinet')).toBeInTheDocument()
    expect(within(cabinets).getByText(/12U · ASSUMED-CAB-12U-WALL/)).toBeInTheDocument()
    const items = screen.getByRole('region', { name: 'Rack items' })
    expect(within(items).getByText(/52 ports · USW-Pro-Max-48-PoE/)).toBeInTheDocument()
  })

  it('saves a corrected part number and sends the ports back unchanged', async () => {
    const fetchMock = stub()
    render(<><RackCatalogueSections /><StatusRegion /></>)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit Ubiquiti USW-Pro-Max-48-PoE' }))
    const part = screen.getByLabelText('Part number')
    await userEvent.clear(part)
    await userEvent.type(part, 'USW-PRO-MAX-48-POE-UK')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const patch = fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')!
    expect(patch[0]).toBe('/api/catalogue/rack-device-types/sw48')
    const body = JSON.parse(String(patch[1]!.body))
    expect(body.partNumber).toBe('USW-PRO-MAX-48-POE-UK')
    expect(body.portGroups).toEqual(groups)
    expect(body.kind).toBe('Switch')
    expect(await screen.findByRole('status')).toHaveTextContent(/saved/i)
  })
})
