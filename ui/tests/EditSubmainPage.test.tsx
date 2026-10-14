import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EditSubmainPage } from '../src/routes/NewSubmainPage'
import { StatusRegion } from '../src/components/StatusRegion'
import type { CircuitResponse, DesignResponse, SubmainResponse } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const enclosures = [{
  id: 'e0', manufacturer: 'Hager', model: 'VML', rows: 2, slotsPerRow: 72, ipRating: 'IP30',
  totalSlots: 144, description: 'Hager VML small',
}, {
  id: 'e1', manufacturer: 'Hager', model: 'VML', rows: 3, slotsPerRow: 72, ipRating: 'IP30',
  totalSlots: 216, description: 'Hager VML 3 row x 24 way',
}]

// Built on the smaller box, turned over and with no isolator: none of it the
// create form's defaults, so a prefill that fell back to them would show.
const submain: SubmainResponse = {
  id: 's1', projectId: 'p1', name: 'Ground floor', reference: 'DB-2', feedCableSize: '10mm²',
  originBreakerAmps: 40, phase: 'L1', enclosureTypeId: 'e0', ruleSetId: 'r1', notes: 'Under the stairs',
  layoutVersion: 3, hasIsolator: false, terminalsAtBottom: true,
  extraFixtures: [{ deviceTypeId: 'meter', quantity: 1 }], circuitCount: 4, deviceCount: 5,
}

const circuits: CircuitResponse[] = [
  { id: 'd1', type: 'DimmedLighting', name: 'Hall lights', room: 'Hall', sequence: 1, wattsPerMetre: null, lengthMetres: null },
  { id: 'd2', type: 'DimmedLighting', name: 'Lighting 2', room: null, sequence: 2, wattsPerMetre: null, lengthMetres: null },
  { id: 'w1', type: 'Switched', name: 'Landing', room: 'Landing', sequence: 3, wattsPerMetre: null, lengthMetres: null },
  { id: 't1', type: 'LedTape', name: 'Kitchen plinth', room: 'Kitchen', sequence: 4, wattsPerMetre: 9.6, lengthMetres: 4 },
]

const design: DesignResponse = {
  submainId: 's1',
  layoutVersion: 3,
  layout: {
    rows: 2, slotsPerRow: 72,
    devices: [{
      id: null, deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
      moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None', channels: [],
    }],
  },
  diagnostics: [], bom: [],
  summary: { rowsUsed: 2, slotsUsed: 3, totalSlots: 144, deviceCount: 1, spareChannels: 0 },
}

function stubApi(patchResponse: () => Response = () => json(submain)) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/catalogue/enclosures') return json(enclosures)
    if (url === '/api/catalogue/device-types') return json([])
    if (url === '/api/submains/s1' && init?.method === 'PATCH') return patchResponse()
    if (url === '/api/submains/s1') return json(submain)
    if (url === '/api/submains/s1/circuits') return json(circuits)
    if (url === '/api/submains/s1/design/preview') return json(design)
    return json({})
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const bodiesOf = (fetchMock: ReturnType<typeof stubApi>, url: string, method: string) =>
  fetchMock.mock.calls.filter(c => c[0] === url && c[1]?.method === method).map(c => JSON.parse(String(c[1]!.body)))

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/submains/s1/edit']}>
      <Routes>
        <Route path="/submains/:submainId/edit" element={<EditSubmainPage />} />
        <Route path="/submains/:submainId/panel" element={<><h1>Panel screen</h1><StatusRegion /></>} />
      </Routes>
    </MemoryRouter>,
  )

const saveButton = () => screen.getByRole('button', { name: /save and rebuild/i })

describe('EditSubmainPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('fills the form with the panel as it was built', async () => {
    stubApi()

    renderPage()

    expect(await screen.findByLabelText(/submain name/i)).toHaveValue('Ground floor')
    expect(screen.getByRole('heading', { level: 1, name: 'Edit Ground floor' })).toBeInTheDocument()
    expect(screen.getByLabelText('Enclosure')).toHaveValue('e0')
    expect(screen.getByLabelText(/cables enter at the bottom/i)).toBeChecked()
    expect(screen.getByLabelText(/fit a main isolator/i)).not.toBeChecked()
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('2')
    expect(screen.getByLabelText('Switched')).toHaveValue('1')
    expect(screen.getByLabelText('Blinds')).toHaveValue('0')
    expect(screen.getByLabelText(/^tape name/i)).toHaveValue('Kitchen plinth')
    expect(screen.getByLabelText(/watts per metre/i)).toHaveValue(9.6)
    expect(screen.getByRole('link', { name: /back to panel/i })).toHaveAttribute('href', '/submains/s1/panel')
    expect(saveButton()).toBeInTheDocument()
  })

  it('previews against the stored submain, sending the kept circuits by id', async () => {
    const fetchMock = stubApi()

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))

    await waitFor(() => expect(bodiesOf(fetchMock, '/api/submains/s1/design/preview', 'POST')).not.toHaveLength(0))
    const previews = bodiesOf(fetchMock, '/api/submains/s1/design/preview', 'POST')
    expect(previews.at(-1).circuits).toEqual([
      { id: 'd1', type: 'DimmedLighting', name: 'Hall lights', room: 'Hall', sequence: 1 },
      { id: 'd2', type: 'DimmedLighting', name: 'Lighting 2', room: null, sequence: 2 },
      { type: 'DimmedLighting', name: 'Lighting 3', sequence: 3 },
      { id: 'w1', type: 'Switched', name: 'Landing', room: 'Landing', sequence: 4 },
      { id: 't1', type: 'LedTape', name: 'Kitchen plinth', room: 'Kitchen', sequence: 5, wattsPerMetre: 9.6, lengthMetres: 4 },
    ])
    // Nothing is previewed as a new submain in the house.
    expect(fetchMock.mock.calls.map(c => c[0])).not.toContain('/api/projects/p1/design/preview')
  })

  it('saves everything in one PATCH with a rebuild, keeping the fields the form does not show, and goes back to the panel', async () => {
    const fetchMock = stubApi()

    renderPage()
    const name = await screen.findByLabelText(/submain name/i)
    await userEvent.clear(name)
    await userEvent.type(name, 'Ground floor east')
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(saveButton()).toBeEnabled())

    await userEvent.click(saveButton())

    expect(await screen.findByRole('heading', { name: /panel screen/i })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/panel rebuilt/i)
    const [patch, ...more] = bodiesOf(fetchMock, '/api/submains/s1', 'PATCH')
    expect(more).toHaveLength(0)
    expect(patch).toMatchObject({
      name: 'Ground floor east',
      reference: 'DB-2',
      feedCableSize: '10mm²',
      originBreakerAmps: 40,
      phase: 'L1',
      ruleSetId: 'r1',
      notes: 'Under the stairs',
      enclosureTypeId: 'e0',
      hasIsolator: false,
      terminalsAtBottom: true,
      extraFixtures: [{ deviceTypeId: 'meter', quantity: 1 }],
      rebuild: true,
    })
    expect(patch.circuits.map((c: { id?: string; name: string }) => [c.id, c.name])).toEqual([
      ['d1', 'Hall lights'], ['d2', 'Lighting 2'], [undefined, 'Lighting 3'], ['w1', 'Landing'], ['t1', 'Kitchen plinth'],
    ])
  })

  it('names the circuits a save would remove, next to the button', async () => {
    stubApi()

    renderPage()
    await screen.findByLabelText(/submain name/i)
    expect(screen.queryByText(/saving removes/i)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /fewer dimmed lighting/i }))
    await userEvent.click(screen.getByRole('button', { name: /fewer switched/i }))

    const warning = screen.getByText(/saving removes/i).closest('p')!
    expect(warning).toHaveTextContent('Saving removes: Lighting 2, Landing')
    expect(warning.compareDocumentPosition(saveButton()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    // Put back, nothing is lost and the warning goes.
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    await userEvent.click(screen.getByRole('button', { name: /more switched/i }))
    expect(screen.queryByText(/saving removes/i)).not.toBeInTheDocument()
  })

  it('stays on the form and says why when the generator refuses the rebuild', async () => {
    const refused = {
      ...design,
      diagnostics: [{ severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'This design needs 3 rows of 24 modules; Hager VML has 2.', suggestion: 'Use a bigger box.' }],
    }
    stubApi(() => json(refused, 422))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(saveButton()).toBeEnabled())
    await userEvent.click(saveButton())

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not saved: this design needs 3 rows/i))
    expect(screen.queryByRole('heading', { name: /panel screen/i })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('3')
  })

  it('shows a refused name by the name field, as the create form does', async () => {
    stubApi(() => json({ title: 'One or more validation errors occurred.', errors: { name: ['A submain name is required.'] } }, 400))

    renderPage()
    await userEvent.clear(await screen.findByLabelText(/submain name/i))
    await waitFor(() => expect(saveButton()).toBeEnabled())
    await userEvent.click(saveButton())

    expect(await screen.findByText('A submain name is required.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /panel screen/i })).not.toBeInTheDocument()
  })

  it('offers Try again when the panel fails to load', async () => {
    let failing = true
    stubApi()
    const fetchMock = vi.mocked(fetch)
    const answer = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) =>
      input === '/api/submains/s1/circuits' && failing ? json({ title: 'Circuits down' }, 500) : answer(input, init))

    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent(/circuits down/i)
    expect(screen.queryByLabelText(/submain name/i)).not.toBeInTheDocument()

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByLabelText(/submain name/i)).toHaveValue('Ground floor')
  })
})
