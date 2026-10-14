import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NewRackPage } from '../src/routes/NewRackPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const cabinets = [
  { id: 'c6', manufacturer: 'Generic', model: '6U wall cabinet', partNumber: 'A6', heightU: 6, mounting: 'Wall', description: 'Generic 6U wall cabinet' },
  { id: 'c12', manufacturer: 'Generic', model: '12U wall cabinet', partNumber: 'A12', heightU: 12, mounting: 'Wall', description: 'Generic 12U wall cabinet' },
]

function stub(post: () => Response) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') return post()
    if (url === '/api/catalogue/rack-types') return json(cabinets)
    if (url === '/api/projects/p1/submains') return json([{ id: 's1', name: 'Ground floor' }])
    return json([])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const show = () => render(
  <MemoryRouter initialEntries={['/projects/p1/racks/new']}>
    <Routes>
      <Route path="/projects/:projectId/racks/new" element={<NewRackPage />} />
      <Route path="/racks/:rackId" element={<p>Rack page</p>} />
    </Routes>
  </MemoryRouter>,
)

describe('NewRackPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('adds the rack with its cabinet and feed, then opens it', async () => {
    const fetchMock = stub(() => json({ id: 'r1', name: 'Comms cupboard' }, 201))
    show()

    await userEvent.type(await screen.findByLabelText('Rack name'), 'Comms cupboard')
    expect(screen.getByLabelText('Cabinet')).toHaveValue('c12')
    await userEvent.selectOptions(screen.getByLabelText('Fed from'), 's1')
    await userEvent.click(screen.getByRole('button', { name: 'Add rack' }))

    expect(await screen.findByText('Rack page')).toBeInTheDocument()
    const [url, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(url).toBe('/api/projects/p1/racks')
    expect(JSON.parse(String(init!.body))).toEqual({ name: 'Comms cupboard', rackTypeId: 'c12', fedFromSubmainId: 's1', locationId: null })
  })

  it('places the new rack in the location chosen', async () => {
    const fetchMock = stub(() => json({ id: 'r1', name: 'Comms cupboard' }, 201))
    const underlying = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (url: string, init?: RequestInit) => url === '/api/projects/p1/locations'
      ? json([{ id: 'l1', name: 'Plant room', notes: null, sequence: 0 }])
      : underlying(url, init))
    show()

    await userEvent.type(await screen.findByLabelText('Rack name'), 'Comms cupboard')
    expect(screen.getByLabelText('Location')).toHaveValue('')
    await userEvent.selectOptions(screen.getByLabelText('Location'), 'l1')
    await userEvent.click(screen.getByRole('button', { name: 'Add rack' }))

    await screen.findByText('Rack page')
    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body)).locationId).toBe('l1')
  })

  it('can be fed from no automation panel', async () => {
    const fetchMock = stub(() => json({ id: 'r1', name: 'Loft' }, 201))
    show()

    await userEvent.type(await screen.findByLabelText('Rack name'), 'Loft')
    await userEvent.click(screen.getByRole('button', { name: 'Add rack' }))

    const [, init] = fetchMock.mock.calls.find(([, i]) => i?.method === 'POST')!
    expect(JSON.parse(String(init!.body)).fedFromSubmainId).toBeNull()
  })

  it('shows the server\'s field errors where they belong and keeps what was typed', async () => {
    stub(() => json({ title: 'One or more validation errors occurred.', errors: { name: ['A rack name is required.'] } }, 400))
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Add rack' }))

    expect(await screen.findByText('A rack name is required.')).toBeInTheDocument()
    expect(screen.getByLabelText('Cabinet')).toHaveValue('c12')
  })
})
