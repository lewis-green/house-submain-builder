import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PanelPage } from '../src/routes/PanelPage'
import type { DesignResponse } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const submain = {
  id: 's1', projectId: 'p1', name: 'Ground Floor', reference: null,
  feedCableSize: null, originBreakerAmps: null, phase: null,
  enclosureTypeId: 'e1', ruleSetId: 'r1', notes: null,
  layoutVersion: 4, circuitCount: 1, deviceCount: 1,
}

/**
 * A fresh Response per call: a body can only be read once, and the page loads
 * the layout and the submain together.
 */
const stub = (layout: () => Response, rest?: (url: string) => Response | undefined) =>
  vi.fn(async (url: string) => {
    if (url.endsWith('/layout')) return layout()
    if (url === '/api/submains/s1') return json(submain)
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
        channels: [{ channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen ceiling', isSpare: false }],
      },
    ],
  },
  diagnostics,
  bom: [],
  summary: { rowsUsed: 2, slotsUsed: 3, totalSlots: 216, deviceCount: 1, spareChannels: 0, bomTotal: 0 },
})

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/submains/s1/panel']}>
      <Routes><Route path="/submains/:submainId/panel" element={<PanelPage />} /></Routes>
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

  it('reloads rather than erroring when an edit conflicts', async () => {
    const fetchMock = stub(
      () => json(design()),
      url => url.includes('/channels/') ? json({ message: 'changed', currentLayoutVersion: 9 }, 409) : undefined)
    vi.stubGlobal('fetch', fetchMock)

    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: /dimmer 1/i }))
    await userEvent.click(screen.getByRole('button', { name: /free this channel/i }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/reloading/i))
  })

  it('tells the engineer when a submain has no panel yet', async () => {
    vi.stubGlobal('fetch', stub(() => json({
      ...design(), layout: { rows: 0, slotsPerRow: 0, devices: [] },
    })))

    renderPage()

    expect(await screen.findByText(/no panel yet/i)).toBeInTheDocument()
  })
})
