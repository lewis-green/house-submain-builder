import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PanelPage } from '../src/routes/PanelPage'
import type { DesignResponse } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const deviceTypes = [
  {
    id: 'meter', manufacturer: 'Shelly', model: 'Pro 3EM', partNumber: 'SHELLY-PRO-3EM-120',
    category: 'EnergyMeter', moduleWidth: 9, channelCount: 3,
    maxLoadPerChannelW: null, maxTotalLoadW: null, active: true,
  },
]

const submain = {
  id: 's1', projectId: 'p1', name: 'Ground Floor', reference: null,
  feedCableSize: null, originBreakerAmps: null, phase: null,
  enclosureTypeId: 'e1', ruleSetId: 'r1', notes: null,
  hasIsolator: true, terminalsAtBottom: false, extraFixtures: [],
  layoutVersion: 4, circuitCount: 1, deviceCount: 1,
}

/**
 * A fresh Response per call: a body can only be read once, and the page loads
 * the layout and the submain together.
 */
const stub = (layout: () => Response, rest?: (url: string) => Response | undefined) =>
  vi.fn(async (url: string, _init?: RequestInit) => {
    if (url.endsWith('/layout')) return layout()
    if (url === '/api/submains/s1') return json(submain)
    if (url === '/api/projects/p1/rooms') return json(['Kitchen', 'Snug'])
    if (url === '/api/catalogue/device-types') return json(deviceTypes)
    return rest?.(url) ?? json({})
  })

const design = (diagnostics: DesignResponse['diagnostics'] = []): DesignResponse => ({
  submainId: 's1',
  layoutVersion: 4,
  layout: {
    rows: 4,
    slotsPerRow: 54,
    devices: [
      {
        id: 'd1', deviceTypeId: 'dim', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
        moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
        channels: [{
          channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling',
          circuitRoom: 'Kitchen', isSpare: false,
        }],
      },
    ],
  },
  diagnostics,
  bom: [],
  summary: { rowsUsed: 2, slotsUsed: 3, totalSlots: 216, deviceCount: 1, spareChannels: 0 },
})

/** The body of the one PATCH sent to `url`, parsed. */
const patchBody = (mock: ReturnType<typeof stub>, url: string) => {
  const call = mock.mock.calls.find(c => c[1]?.method === 'PATCH' && c[0] === url)

  expect(call, `no PATCH to ${url}`).toBeDefined()
  return JSON.parse(String(call![1]!.body))
}

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/panels/s1']}>
      <Routes><Route path="/panels/:submainId" element={<PanelPage />} /></Routes>
    </MemoryRouter>,
  )

describe('PanelPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('draws the stored layout', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()

    expect(await screen.findByRole('button', { name: /dimmer 1/i })).toBeInTheDocument()
  })

  it('loads the stored layout, not a fresh preview', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })

    expect(fetchMock.mock.calls.map(c => c[0])).toContain('/api/submains/s1/layout')
    expect(fetchMock.mock.calls.map(c => c[0])).not.toContain('/api/submains/s1/design/preview')
  })

  it('shows a diagnostic with its suggestion', async () => {
    vi.stubGlobal('fetch', stub(() => json(design([
      { severity: 'Warning', code: 'POSITION_OVERRIDE_DROPPED', message: 'Dimmer 2 moved', suggestion: 'Drag it again' },
    ]))))

    renderPage()

    expect(await screen.findByText(/dimmer 2 moved/i)).toBeInTheDocument()
    expect(screen.getByText(/drag it again/i)).toBeInTheDocument()
  })

  it('opens the device sheet when a device is tapped', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))

    expect(await screen.findByRole('dialog')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Kitchen ceiling')).toBeInTheDocument()
  })

  it('asks the server again whether the PDF is current after a circuit is renamed', async () => {
    // The server decides staleness by comparing snapshots; the page only has to
    // ask again after a save. Here the list says "current" until the rename lands.
    let renamed = false
    const fetchMock = stub(
      () => json(design()),
      url => {
        if (url === '/api/submains/s1/revisions') {
          return json([{ id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current: !renamed }])
        }
        if (url === '/api/circuits/c1') { renamed = true; return json({ id: 'c1', name: 'Island pendants', room: 'Kitchen' }) }
        return undefined
      })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    expect(await screen.findByRole('link', { name: /open pdf \(revision 1\)/i })).toBeInTheDocument()
    expect(screen.queryByText(/changed since that pdf/i)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /dimmer 1/i }))
    const name = await screen.findByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Island pendants{Enter}')

    expect(await screen.findByText(/changed since that pdf was issued/i)).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(c => c[0] === '/api/submains/s1/revisions').length).toBeGreaterThanOrEqual(2)
  })

  it('says when a circuit save fails and sends the same words again on the next Return', async () => {
    let failures = 1
    const fetchMock = stub(
      () => json(design()),
      url => {
        if (url !== '/api/circuits/c1') return undefined
        if (failures-- > 0) return json({ title: 'Store down' }, 500)
        return json({ id: 'c1', name: 'Retry me', room: 'Kitchen' })
      })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))
    const name = await screen.findByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Retry me{Enter}')

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not saved: store down/i))
    expect(name).toHaveValue('Retry me')

    await userEvent.click(name)
    await userEvent.keyboard('{Enter}')

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved retry me/i))
    expect(fetchMock.mock.calls.filter(c => c[0] === '/api/circuits/c1' && c[1]?.method === 'PATCH')).toHaveLength(2)
  })

  it('reloads rather than erroring when an edit conflicts', async () => {
    const fetchMock = stub(
      () => json(design()),
      url => url.includes('/channels/') ? json({ message: 'changed', currentLayoutVersion: 9 }, 409) : undefined)
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))
    await userEvent.click(screen.getByRole('button', { name: /free this channel/i }))
    await userEvent.click(screen.getByRole('button', { name: /^free it$/i }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/reloading/i))
  })

  it('shows the install options as the submain has them stored', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()

    expect(await screen.findByLabelText(/fit a main isolator/i)).toBeChecked()
    expect(screen.getByLabelText(/cables enter at the bottom/i)).not.toBeChecked()
  })

  it('saves the option and rebuilds the panel in one request when the install changes', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByLabelText(/cables enter at the bottom/i))

    await waitFor(() => {
      expect(patchBody(fetchMock, '/api/submains/s1')).toMatchObject({ terminalsAtBottom: true, rebuild: true })
    })

    // One transaction on the server, so there is no second call for a failure to fall between.
    expect(fetchMock.mock.calls.map(c => c[0])).not.toContain('/api/submains/s1/design/generate')
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/panel rebuilt/i))
  })

  it('rebuilds once when a fixture is added twice in the same tick', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Device to add'), 'meter')
    // Before React has rendered the controls disabled: only a synchronous guard stops the second rebuild.
    const add = screen.getByRole('button', { name: 'Add' })
    await act(async () => { add.click(); add.click() })

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/panel rebuilt/i))
    expect(fetchMock.mock.calls.filter(c => c[0] === '/api/submains/s1' && c[1]?.method === 'PATCH')).toHaveLength(1)
  })

  it('leaves the toggle as stored and says why when the server refuses to rebuild', async () => {
    // The refused design comes back as a 422 with its diagnostics; nothing was stored.
    const refused = {
      ...design(),
      diagnostics: [{ severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'This design needs 5 rows of 18 modules; the box has 4.', suggestion: null }],
    }
    const underlying = stub(() => json(design()))
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) =>
      url === '/api/submains/s1' && init?.method === 'PATCH' ? json(refused, 422) : underlying(url, init)))

    renderPage()
    const toggle = await screen.findByLabelText(/cables enter at the bottom/i)
    await userEvent.click(toggle)

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/panel not rebuilt: this design needs 5 rows/i))
    // The server stored nothing, so the option reads as it did; no half-applied state.
    expect(screen.getByLabelText(/cables enter at the bottom/i)).not.toBeChecked()
    expect(screen.getByLabelText(/cables enter at the bottom/i)).toBeEnabled()
  })

  it('says so in Other devices, with a retry, when the catalogue fails to load', async () => {
    let failing = true
    const underlying = stub(() => json(design()))
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) =>
      url === '/api/catalogue/device-types' && failing ? json({ title: 'Catalogue down' }, 500) : underlying(url, init)))

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/device catalogue could not be loaded/i)
    expect(alert).toHaveTextContent(/catalogue down/i)
    expect(screen.queryByLabelText('Device to add')).not.toBeInTheDocument()

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByLabelText('Device to add')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('turning the isolator off does not also send the other option', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByLabelText(/fit a main isolator/i))

    await waitFor(() => {
      const body = patchBody(fetchMock, '/api/submains/s1')
      expect(body.hasIsolator).toBe(false)
      expect(body).not.toHaveProperty('terminalsAtBottom')
    })
  })

  it('keeps the details the panel screen does not show when an install option rebuilds it', async () => {
    // PATCH replaces these with whatever it is sent: leaving them out would blank
    // them, and a missing ruleset would quietly become the default one.
    const detailed = {
      ...submain, reference: 'DB-2', feedCableSize: '10mm²', originBreakerAmps: 40, phase: 'L1',
      ruleSetId: 'r7', notes: 'Under the stairs',
    }
    const underlying = stub(() => json(design()))
    const fetchMock = vi.fn(async (url: string, init?: RequestInit) =>
      url === '/api/submains/s1' ? json(detailed) : underlying(url, init))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByLabelText(/fit a main isolator/i))

    await waitFor(() => expect(patchBody(fetchMock as unknown as ReturnType<typeof stub>, '/api/submains/s1')).toMatchObject({
      reference: 'DB-2', feedCableSize: '10mm²', originBreakerAmps: 40, phase: 'L1', ruleSetId: 'r7', notes: 'Under the stairs',
      hasIsolator: false, rebuild: true,
    }))
  })

  it('says when a circuit has been saved, and keeps saying it for a screen reader', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))
    const name = screen.getByLabelText('Circuit name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Island pendants')
    await userEvent.tab()

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved island pendants/i))
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite')
  })

  it('keeps the keyboard in the field being typed in while a save lands', async () => {
    const fetchMock = stub(() => json(design()))
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))

    // Room commits when the thumb moves on to Circuit name; the PATCH and the
    // reload then land while the next word is being typed.
    const room = screen.getByLabelText('Room')
    await userEvent.clear(room)
    await userEvent.type(room, 'Snug')
    await userEvent.tab()
    const name = screen.getByLabelText('Circuit name')
    expect(document.activeElement).toBe(name)
    await userEvent.type(name, ' Isl')

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/saved/i))
    await waitFor(() => expect(fetchMock.mock.calls.filter(c => String(c[0]).endsWith('/layout'))).toHaveLength(2))

    expect(document.activeElement).toBe(screen.getByLabelText('Circuit name'))
    expect(screen.getByLabelText('Circuit name')).toHaveValue('Kitchen ceiling Isl')
  })

  it('puts the drawing before the install options, the PDF action in the action bar and the parts list in the nav', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })

    const drawing = screen.getByRole('group', { name: /^panel/i })
    const options = screen.getByText(/install options/i)
    expect(drawing.compareDocumentPosition(options) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByRole('button', { name: /issue pdf/i })).toBeInTheDocument()
    // Parts list is a place to go, so it lives with the title and leaves the bar to the PDF pair.
    const bom = screen.getByRole('link', { name: /parts list/i })
    expect(screen.getByRole('navigation').contains(bom)).toBe(true)
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(document.title).toMatch(/ground floor/i)
  })

  it('offers Edit beside the parts list, back into the form that built the panel', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })

    const edit = screen.getByRole('link', { name: /^edit$/i })
    expect(edit).toHaveAttribute('href', '/panels/s1/edit')
    expect(screen.getByRole('navigation').contains(edit)).toBe(true)
  })

  it('keeps the row labels outside the drawing, where they stay on screen, and offers an overview strip', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })

    const svg = screen.getByRole('group', { name: /^panel/i })
    expect(svg.textContent).not.toMatch(/Row 1/)
    const gutter = screen.getByText('Row 2').parentElement!
    expect(gutter.className).toMatch(/sticky/)
    expect(gutter.querySelectorAll('div')).toHaveLength(4)
    // jsdom has no layout, so the strip cannot know what is in view and says so plainly.
    expect(screen.getByText(/18 modules wide\. scroll sideways/i)).toBeInTheDocument()
  })

  it('says when the devices are too small to tap, so Fit width is honest about what it shows', async () => {
    vi.stubGlobal('fetch', stub(() => json(design())))

    renderPage()
    await screen.findByRole('button', { name: /dimmer 1/i })
    expect(screen.getByText(/tap a device to name its circuits/i)).toBeInTheDocument()

    // Four steps out from 1× is 0.41×: a one-module dimmer is 12px wide.
    for (let i = 0; i < 4; i++) await userEvent.click(screen.getByRole('button', { name: 'Zoom out' }))

    expect(screen.getByText(/too small to tap/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '1×' }))
    expect(screen.getByText(/tap a device to name its circuits/i)).toBeInTheDocument()
  })

  it('tells the engineer when a submain has no layout yet', async () => {
    vi.stubGlobal('fetch', stub(() => json({
      ...design(), layout: { rows: 0, slotsPerRow: 0, devices: [] },
    })))

    renderPage()

    expect(await screen.findByText(/no layout yet/i)).toBeInTheDocument()
  })

  describe('terminal order', () => {
    const withTerminal = (): DesignResponse => {
      const d = design()
      d.layout.devices.push({
        id: 't1', deviceTypeId: 'term', category: 'Terminal240', rowIndex: 0, startSlot: 0,
        moduleWidth: 1, label: 'C1', terminalRole: 'All', circuitId: 'c1', channels: [],
      } as DesignResponse['layout']['devices'][number])
      return d
    }
    const terminalsBody = {
      layoutVersion: 4,
      circuits: [
        { circuitId: 'c1', name: 'Kitchen ceiling', room: 'Kitchen', type: 'DimmedLighting', terminalCount: 1 },
        { circuitId: 'c2', name: 'Hall', room: null, type: 'DimmedLighting', terminalCount: 1 },
      ],
    }
    const terminalStub = () => stub(
      () => json(withTerminal()),
      url => (url === '/api/submains/s1/terminals' ? json(terminalsBody) : undefined))

    it('opens the terminal order from the terminals', async () => {
      vi.stubGlobal('fetch', terminalStub())

      renderPage()
      await userEvent.click(await screen.findByRole('button', { name: /Terminals C1/ }))

      expect(await screen.findByRole('dialog', { name: 'Terminal order' })).toBeInTheDocument()
    })

    const openAndSave = async () => {
      await userEvent.click(await screen.findByRole('button', { name: /Terminals C1/ }))
      await userEvent.click(await screen.findByRole('button', { name: 'Move Hall up' }))
      await userEvent.click(screen.getByRole('button', { name: 'Save order' }))
    }

    it('keeps the sheet open and says why when the rebuild is refused', async () => {
      const base = terminalStub()
      vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) =>
        url === '/api/submains/s1/terminals' && init?.method === 'PUT'
          ? json({ diagnostics: [{ severity: 'Error', code: 'NO_ROOM', message: 'No room for 4 more terminals' }] }, 422)
          : base(url, init)))

      renderPage()
      await openAndSave()

      expect(await screen.findByRole('dialog', { name: 'Terminal order' })).toBeInTheDocument()
      expect(await screen.findByRole('alert')).toHaveTextContent(/no room for 4 more terminals/i)
    })

    it('closes the sheet and reloads the layout on a stale version', async () => {
      const base = terminalStub()
      const fetchMock = vi.fn(async (url: string, init?: RequestInit) =>
        url === '/api/submains/s1/terminals' && init?.method === 'PUT'
          ? json({ title: 'Conflict', conflict: true }, 409)
          : base(url, init))
      vi.stubGlobal('fetch', fetchMock)

      renderPage()
      await screen.findByRole('button', { name: /Terminals C1/ })
      const layoutLoads = () => fetchMock.mock.calls.filter(c => c[0] === '/api/submains/s1/layout').length
      const before = layoutLoads()
      await openAndSave()

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      await waitFor(() => expect(layoutLoads()).toBeGreaterThan(before))
    })

    it('says so and opens no sheet when the terminals cannot be loaded', async () => {
      vi.stubGlobal('fetch', stub(
        () => json(withTerminal()),
        url => (url === '/api/submains/s1/terminals' ? json({ title: 'Boom' }, 500) : undefined)))

      renderPage()
      await userEvent.click(await screen.findByRole('button', { name: /Terminals C1/ }))

      expect(await screen.findByText(/Terminals not loaded/)).toBeInTheDocument()
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('saves the order with the layout version it was based on', async () => {
      const fetchMock = terminalStub()
      vi.stubGlobal('fetch', fetchMock)

      renderPage()
      await userEvent.click(await screen.findByRole('button', { name: /Terminals C1/ }))
      await userEvent.click(await screen.findByRole('button', { name: 'Move Hall up' }))
      await userEvent.click(screen.getByRole('button', { name: 'Save order' }))

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      const put = fetchMock.mock.calls.find(c => c[1]?.method === 'PUT' && c[0] === '/api/submains/s1/terminals')
      expect(put).toBeDefined()
      expect(JSON.parse(String(put![1]!.body))).toEqual({
        layoutVersion: 4,
        circuits: [{ circuitId: 'c2', terminalCount: 1 }, { circuitId: 'c1', terminalCount: 1 }],
      })
    })
  })
})
