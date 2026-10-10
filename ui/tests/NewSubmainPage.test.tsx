import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NewSubmainPage } from '../src/routes/NewSubmainPage'
import type { DesignResponse } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

// The small one first, as a catalogue might list them; the wizard should still start on the big one.
const enclosures = [{
  id: 'e0', manufacturer: 'Hager', model: 'VML', rows: 2, slotsPerRow: 72, ipRating: 'IP30',
  totalSlots: 144, description: 'Hager VML small',
}, {
  id: 'e1', manufacturer: 'Hager', model: 'VML', rows: 3, slotsPerRow: 72, ipRating: 'IP30',
  totalSlots: 216, description: 'Hager VML 3 row x 24 way',
}]

const design: DesignResponse = {
  submainId: '00000000-0000-0000-0000-000000000000',
  layoutVersion: 0,
  layout: {
    rows: 3, slotsPerRow: 72,
    devices: [{
      id: null, deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
      moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None', channels: [],
    }],
  },
  diagnostics: [], bom: [],
  summary: { rowsUsed: 2, slotsUsed: 3, totalSlots: 216, deviceCount: 1, spareChannels: 0 },
}

function stubApi(previewResponse: () => Response = () => json(design)) {
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === '/api/catalogue/enclosures') return json(enclosures)
    if (url === '/api/catalogue/device-types') return json([])
    if (url === '/api/projects/p1/design/preview') return previewResponse()
    if (url === '/api/projects/p1/submains' && init?.method === 'POST') return json({ id: 's9' })
    if (url === '/api/submains/s9/design/generate') return json(design)
    return json({})
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/projects/p1/submains/new']}>
      <Routes>
        <Route path="/projects/:projectId/submains/new" element={<NewSubmainPage />} />
        <Route path="/submains/:submainId/panel" element={<h1>Panel screen</h1>} />
      </Routes>
    </MemoryRouter>,
  )

describe('NewSubmainPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('previews without creating a submain, so backing out leaves nothing behind', async () => {
    const fetchMock = stubApi()

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))

    await waitFor(() => expect(screen.getByText(/1 dimmer/i)).toBeInTheDocument())

    const calls = fetchMock.mock.calls.map(c => [c[0], c[1]?.method ?? 'GET'])
    expect(calls).toContainEqual(['/api/projects/p1/design/preview', 'POST'])
    expect(calls).not.toContainEqual(['/api/projects/p1/submains', 'POST'])
  })

  it('says why Generate is disabled, next to the button', async () => {
    stubApi()

    renderPage()

    const generate = await screen.findByRole('button', { name: /generate panel/i })
    expect(generate).toBeDisabled()
    expect(screen.getByText(/add at least one circuit/i)).toBeInTheDocument()
  })

  it('creates the submain and generates its panel in one call, only on Generate', async () => {
    const fetchMock = stubApi()

    renderPage()
    await userEvent.type(await screen.findByLabelText(/submain name/i), 'Ground floor')
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())

    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    expect(await screen.findByRole('heading', { name: /panel screen/i })).toBeInTheDocument()
    const creates = fetchMock.mock.calls.filter(c => c[0] === '/api/projects/p1/submains' && c[1]?.method === 'POST')
    expect(creates).toHaveLength(1)
    expect(JSON.parse(String(creates[0][1]!.body))).toMatchObject({ name: 'Ground floor', enclosureTypeId: 'e1', generate: true })
    // The server lays the panel out in the same transaction; a second call would be a second thing to fail.
    expect(fetchMock.mock.calls.map(c => c[0])).not.toContain('/api/submains/s9/design/generate')
  })

  it('leaves nothing behind when Generate fails, keeps the inputs, and a retry asks once more', async () => {
    let failing = true
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/catalogue/enclosures') return json(enclosures)
      if (url === '/api/catalogue/device-types') return json([])
      if (url === '/api/projects/p1/design/preview') return json(design)
      if (url === '/api/projects/p1/submains' && init?.method === 'POST') {
        return failing ? json({ title: 'Generator down' }, 500) : json({ id: 's9' })
      }
      return json({})
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.type(await screen.findByLabelText(/submain name/i), 'Ground floor')
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not generated: generator down/i))
    expect(screen.getByLabelText(/submain name/i)).toHaveValue('Ground floor')
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('1')

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    expect(await screen.findByRole('heading', { name: /panel screen/i })).toBeInTheDocument()
    // Two attempts, two requests, and the server rolled the first one back: never a ghost submain.
    const creates = fetchMock.mock.calls.filter(c => c[0] === '/api/projects/p1/submains' && c[1]?.method === 'POST')
    expect(creates).toHaveLength(2)
    expect(fetchMock.mock.calls.map(c => c[0])).not.toContain('/api/submains/s9/design/generate')
  })

  it('says what the generator refused, in its words, and shows the preview again', async () => {
    const refused = {
      ...design,
      diagnostics: [{ severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'This design needs 4 rows of 24 modules; Hager VML has 3.', suggestion: 'Use a bigger box.' }],
    }
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/catalogue/enclosures') return json(enclosures)
      if (url === '/api/catalogue/device-types') return json([])
      if (url === '/api/projects/p1/design/preview') return json(design)
      if (url === '/api/projects/p1/submains' && init?.method === 'POST') return json(refused, 422)
      return json({})
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not generated: this design needs 4 rows/i))
    expect(screen.getByRole('button', { name: /generate panel/i })).toBeInTheDocument()
  })

  it('says what the server refused when no field owns the error, instead of looking healthy with nothing made', async () => {
    // What a missing ruleset or an enclosure the catalogue no longer has comes back as: a
    // ValidationProblem whose key is `design`, which no field on the form renders.
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/catalogue/enclosures') return json(enclosures)
      if (url === '/api/catalogue/device-types') return json([])
      if (url === '/api/projects/p1/design/preview') return json(design)
      if (url === '/api/projects/p1/submains' && init?.method === 'POST') {
        return json({ title: 'One or more validation errors occurred.', errors: { design: ['No ruleset is available. Seed one before generating a design.'] } }, 400)
      }
      return json({})
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    // Shown beside the button it answers to, and said once: the note is an alert,
    // so it is announced without a status toast repeating it.
    const note = await screen.findByRole('alert')
    expect(note).toHaveTextContent(/no ruleset is available/i)
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
    expect(note.compareDocumentPosition(screen.getByRole('button', { name: /generate panel/i })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.queryByRole('heading', { name: /panel screen/i })).not.toBeInTheDocument()

    // The note is about the inputs as they were: change them and it goes.
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('clears the last attempt\'s message before saying what this one did', async () => {
    let attempt = 0
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/catalogue/enclosures') return json(enclosures)
      if (url === '/api/catalogue/device-types') return json([])
      if (url === '/api/projects/p1/design/preview') return json(design)
      if (url === '/api/projects/p1/submains' && init?.method === 'POST') {
        return ++attempt === 1
          ? json({ title: 'Generator down' }, 500)
          : json({ title: 'invalid', errors: { name: ['That name is already used in this house.'] } }, 400)
      }
      return json({})
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.type(await screen.findByLabelText(/submain name/i), 'Ground floor')
    await userEvent.click(screen.getByRole('button', { name: /more dimmed lighting/i }))
    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not generated: generator down/i))

    // A different refusal, owned by the name field: the old reason must not sit beside it.
    await userEvent.click(screen.getByRole('button', { name: /generate panel/i }))

    await waitFor(() => expect(screen.getByText(/that name is already used/i)).toBeInTheDocument())
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
  })

  it('sends one request when Generate is activated twice in the same tick', async () => {
    const fetchMock = stubApi()

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))
    const generate = await screen.findByRole('button', { name: /generate panel/i })
    await waitFor(() => expect(generate).toBeEnabled())

    // Two clicks before React has re-rendered the button disabled: the `disabled`
    // state alone cannot stop the second, so the guard has to be synchronous.
    await act(async () => { generate.click(); generate.click() })

    expect(await screen.findByRole('heading', { name: /panel screen/i })).toBeInTheDocument()
    const creates = fetchMock.mock.calls.filter(c => c[0] === '/api/projects/p1/submains' && c[1]?.method === 'POST')
    expect(creates).toHaveLength(1)
  })

  it('offers Try again when the catalogue fails to load, like every other screen', async () => {
    let failing = true
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url === '/api/catalogue/enclosures') return failing ? json({ title: 'Catalogue down' }, 500) : json(enclosures)
      if (url === '/api/catalogue/device-types') return json([])
      return json({})
    }))

    renderPage()
    expect(await screen.findByRole('alert')).toHaveTextContent(/catalogue down/i)
    expect(screen.queryByLabelText(/submain name/i)).not.toBeInTheDocument()

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByLabelText(/submain name/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a designed error with a retry when the preview fails, and holds Generate back until it succeeds', async () => {
    let failing = true
    stubApi(() => failing ? json({ title: 'Preview failed' }, 500) : json(design))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/preview failed/i)
    expect(screen.getByRole('button', { name: /generate panel/i })).toBeDisabled()
    expect(screen.getByText(/could not be checked/i)).toBeInTheDocument()
    // The counts entered stay where they are.
    expect(screen.getByLabelText('Dimmed lighting')).toHaveValue('1')

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByText(/1 dimmer/i)).toBeInTheDocument()
  })

  it('stays on its feet when the proxy answers the preview with an HTML error page', async () => {
    stubApi(() => new Response('<html>Bad Gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/not responding \(502\)/i)
    expect(screen.getByLabelText(/submain name/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /generate panel/i })).toBeDisabled()
  })

  it('holds Generate back, and says so, while a changed design is still being checked', async () => {
    stubApi()

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /more dimmed lighting/i }))

    expect(screen.getByRole('button', { name: /generate panel/i })).toBeDisabled()
    expect(screen.getByText(/checking the design/i)).toBeInTheDocument()

    await waitFor(() => expect(screen.getByRole('button', { name: /generate panel/i })).toBeEnabled())
    expect(screen.queryByText(/checking the design/i)).not.toBeInTheDocument()
  })

  it('starts on the largest enclosure and leads each option with its size', async () => {
    stubApi()

    renderPage()

    const select = await screen.findByLabelText('Enclosure')
    await waitFor(() => expect(select).toHaveValue('e1'))
    const options = [...select.querySelectorAll('option')].map(o => o.textContent)
    expect(options).toEqual(['2 rows × 24 modules · Hager VML', '3 rows × 24 modules · Hager VML'])
  })

  it('reads top to bottom: name, enclosure, install, circuits, tape, other devices, preview', async () => {
    stubApi()

    renderPage()
    await screen.findByLabelText(/submain name/i)

    const headings = screen.getAllByText(/^(The install|Circuits|LED tape|Other devices|What this panel needs)/)
      .map(el => el.textContent?.split(' ·')[0].trim())
    expect(headings).toEqual(['The install', 'Circuits', 'LED tape', 'Other devices', 'What this panel needs'])
  })
})
