import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EditSubmainPage } from '../src/routes/SubmainForm'
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
  extraFixtures: [{ deviceTypeId: 'meter', quantity: 1 }], circuitCount: 4, deviceCount: 5, locationId: 'l1',
}

const locations = [
  { id: 'l1', name: 'Plant room', notes: null, sequence: 0 },
  { id: 'l2', name: 'Garage', notes: null, sequence: 1 },
]

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

function stubApi(patchResponse: () => Response = () => json(submain), stored: CircuitResponse[] = circuits) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/catalogue/enclosures') return json(enclosures)
    if (url === '/api/catalogue/device-types') return json([])
    if (url === '/api/submains/s1' && init?.method === 'PATCH') return patchResponse()
    if (url === '/api/submains/s1') return json(submain)
    if (url === '/api/submains/s1/circuits') return json(stored)
    if (url === '/api/submains/s1/design/preview') return json(design)
    if (url === '/api/projects/p1/locations') return json(locations)
    return json({})
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const bodiesOf = (fetchMock: ReturnType<typeof stubApi>, url: string, method: string) =>
  fetchMock.mock.calls.filter(c => c[0] === url && c[1]?.method === method).map(c => JSON.parse(String(c[1]!.body)))

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/panels/s1/edit']}>
      <Routes>
        <Route path="/panels/:submainId/edit" element={<EditSubmainPage />} />
        <Route path="/panels/:submainId" element={<><h1>Panel screen</h1><StatusRegion /></>} />
      </Routes>
    </MemoryRouter>,
  )

const saveButton = () => screen.getByRole('button', { name: /save and rebuild/i })

describe('EditSubmainPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('fills the form with the panel as it was built', async () => {
    stubApi()

    renderPage()

    expect(await screen.findByLabelText(/automation panel name/i)).toHaveValue('Ground floor')
    expect(screen.getByRole('heading', { level: 1, name: 'Edit Ground floor' })).toBeInTheDocument()
    expect(screen.getByLabelText('Enclosure')).toHaveValue('e0')
    expect(screen.getByLabelText(/cables enter at the bottom/i)).toBeChecked()
    expect(screen.getByLabelText(/fit a main isolator/i)).not.toBeChecked()
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('2')
    expect(screen.getByLabelText('Switched')).toHaveValue('1')
    expect(screen.getByLabelText('Blinds')).toHaveValue('0')
    expect(screen.getByLabelText(/^tape name/i)).toHaveValue('Kitchen plinth')
    expect(screen.getByLabelText(/watts per metre/i)).toHaveValue(9.6)
    expect(screen.getByRole('link', { name: /back to panel/i })).toHaveAttribute('href', '/panels/s1')
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
    const name = await screen.findByLabelText(/automation panel name/i)
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
      locationId: 'l1',
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

  it('opens on the panel\'s location and moves it to another with the save', async () => {
    const fetchMock = stubApi()

    renderPage()
    const location = await screen.findByLabelText('Location')
    expect(location).toHaveValue('l1')
    expect([...(location as HTMLSelectElement).options].map(o => o.text)).toEqual(['Not placed', 'Plant room', 'Garage'])
    await userEvent.selectOptions(location, 'l2')
    await waitFor(() => expect(saveButton()).toBeEnabled())
    await userEvent.click(saveButton())

    await screen.findByRole('heading', { name: /panel screen/i })
    expect(bodiesOf(fetchMock, '/api/submains/s1', 'PATCH')[0]).toMatchObject({ locationId: 'l2' })
  })

  it('unplaces the panel when Not placed is chosen', async () => {
    const fetchMock = stubApi()

    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Location'), '')
    await waitFor(() => expect(saveButton()).toBeEnabled())
    await userEvent.click(saveButton())

    await screen.findByRole('heading', { name: /panel screen/i })
    expect(bodiesOf(fetchMock, '/api/submains/s1', 'PATCH')[0].locationId).toBeNull()
  })

  it('names the circuits a save would remove, next to the button', async () => {
    stubApi()

    renderPage()
    await screen.findByLabelText(/automation panel name/i)
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

  it('ties the warning to the button for a screen reader, and says it politely as it changes', async () => {
    stubApi()

    renderPage()
    await screen.findByLabelText(/automation panel name/i)
    expect(saveButton()).not.toHaveAttribute('aria-describedby')

    await userEvent.click(screen.getByRole('button', { name: /fewer switched/i }))

    expect(saveButton()).toHaveAccessibleDescription('Saving removes: Landing')
    const warning = document.getElementById(saveButton().getAttribute('aria-describedby')!)!
    expect(warning.closest('[aria-live="polite"]')).not.toBeNull()
    // Polite, and not a second status region competing with the app's own.
    expect(screen.getAllByRole('status')).toHaveLength(1)
  })

  it('names the first few circuits a save would remove and counts the rest', async () => {
    const many: CircuitResponse[] = Array.from({ length: 8 }, (_, i) => ({
      id: `w${i + 1}`, type: 'Switched', name: `Switched ${i + 1}`, room: null, sequence: i + 1, wattsPerMetre: null, lengthMetres: null,
    }))
    stubApi(undefined, [...many, { ...circuits[0], sequence: 9 }])

    renderPage()
    const fewer = await screen.findByRole('button', { name: /fewer switched/i })
    for (let i = 0; i < 8; i++) await userEvent.click(fewer)

    expect(saveButton()).toHaveAccessibleDescription(
      'Saving removes: Switched 1, Switched 2, Switched 3, Switched 4, Switched 5 and 3 more')
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
    stubApi(() => json({ title: 'One or more validation errors occurred.', errors: { name: ['An automation panel name is required.'] } }, 400))

    renderPage()
    await userEvent.clear(await screen.findByLabelText(/automation panel name/i))
    await waitFor(() => expect(saveButton()).toBeEnabled())
    await userEvent.click(saveButton())

    expect(await screen.findByText('An automation panel name is required.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /panel screen/i })).not.toBeInTheDocument()
  })

  it('will not save over a rename made elsewhere since the form opened, and Reload shows it', async () => {
    const fetchMock = stubApi()
    const answer = fetchMock.getMockImplementation()!
    let renamed = false
    fetchMock.mockImplementation(async (url, init) => url === '/api/submains/s1/circuits' && renamed
      ? json(circuits.map(c => c.id === 't1' ? { ...c, name: 'Island plinth', room: 'Kitchen island' } : c))
      : answer(url, init))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(saveButton()).toBeEnabled())
    renamed = true
    await userEvent.click(saveButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('This panel changed since you opened it. Reload to see the changes.')
    expect(bodiesOf(fetchMock, '/api/submains/s1', 'PATCH')).toHaveLength(0)

    await userEvent.click(screen.getByRole('button', { name: 'Reload' }))

    expect(await screen.findByDisplayValue('Island plinth')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    // Reloaded, the form starts again from the panel as it now is.
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('2')
  })

  it('will not save over submain details changed elsewhere either', async () => {
    const fetchMock = stubApi()
    const answer = fetchMock.getMockImplementation()!
    let edited = false
    fetchMock.mockImplementation(async (url, init) => url === '/api/submains/s1' && init?.method === 'GET' && edited
      ? json({ ...submain, notes: 'Moved to the garage' })
      : answer(url, init))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(saveButton()).toBeEnabled())
    edited = true
    await userEvent.click(saveButton())

    expect(await screen.findByRole('alert')).toHaveTextContent(/changed since you opened it/i)
    expect(bodiesOf(fetchMock, '/api/submains/s1', 'PATCH')).toHaveLength(0)
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
    expect(screen.queryByLabelText(/automation panel name/i)).not.toBeInTheDocument()

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByLabelText(/automation panel name/i)).toHaveValue('Ground floor')
  })
})
