import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../src/App'
import type { DesignResponse, SubmainResponse } from '../src/api/types'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const submain: SubmainResponse = {
  id: 's1', projectId: 'p1', name: 'Ground Floor', reference: null,
  feedCableSize: null, originBreakerAmps: null, phase: null,
  enclosureTypeId: 'e1', ruleSetId: 'r1', notes: null,
  hasIsolator: true, terminalsAtBottom: false, extraFixtures: [],
  layoutVersion: 2, circuitCount: 3, deviceCount: 5,
}

const design: DesignResponse = {
  submainId: 's1', layoutVersion: 2,
  layout: {
    rows: 3, slotsPerRow: 72,
    devices: [{
      id: 'd1', deviceTypeId: 'dt', category: 'Dimmer240', rowIndex: 1, startSlot: 0,
      moduleWidth: 3, label: 'Dimmer 1', terminalRole: 'None',
      channels: [{ channelIndex: 0, circuitId: 'c1', circuitName: 'Kitchen', circuitRoom: null, isSpare: false }],
    }],
  },
  diagnostics: [], bom: [],
  summary: { rowsUsed: 2, slotsUsed: 3, totalSlots: 216, deviceCount: 1, spareChannels: 0 },
}

/** Routes the real app defines. A Back link must land on one of these. */
const routed = (url: string) =>
  ['/', '/catalogue', '/projects/p1', '/projects/p1/bom', '/projects/p1/panels/new',
   '/panels/s1', '/panels/s1/bom', '/panels/s1/edit'].includes(url)

function stubApi() {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/layout')) return json(design)
    if (url === '/api/submains/s1') return json(submain)
    if (url.endsWith('/bom')) return json({ lines: [], total: null, unpricedLines: 0, priced: false })
    if (url === '/api/projects/p1') return json({ id: 'p1', name: 'Willow House', address: null, notes: null, submainCount: 1 })
    if (url === '/api/projects/p1/submains') return json([submain])
    return json([])
  }))
}

describe('Back navigation', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('takes you from a panel to the house it belongs to', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/panels/s1']}><App /></MemoryRouter>)

    const back = await screen.findByRole('link', { name: /back/i })
    const target = back.getAttribute('href')!

    expect(target).toBe('/projects/p1')
    expect(routed(target)).toBe(true)

    await userEvent.click(back)
    expect(await screen.findByRole('heading', { name: 'Willow House' })).toBeInTheDocument()
  })

  it('names the submain rather than just saying Panel', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/panels/s1']}><App /></MemoryRouter>)

    expect(await screen.findByRole('heading', { name: 'Ground Floor' })).toBeInTheDocument()
  })

  it('takes you from a submain parts list back to its panel', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/panels/s1/bom']}><App /></MemoryRouter>)

    const back = await screen.findByRole('link', { name: /back/i })
    const target = back.getAttribute('href')!

    expect(target).toBe('/panels/s1')
    expect(routed(target)).toBe(true)
  })

  it('takes you from a panel into its edit and back to the panel', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/panels/s1']}><App /></MemoryRouter>)

    const edit = await screen.findByRole('link', { name: /^edit$/i })
    expect(routed(edit.getAttribute('href')!)).toBe(true)
    await userEvent.click(edit)

    const back = await screen.findByRole('link', { name: /back to panel/i })
    expect(back.getAttribute('href')).toBe('/panels/s1')
  })

  it('takes you from a house parts list back to the house', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/projects/p1/bom']}><App /></MemoryRouter>)

    const back = await screen.findByRole('link', { name: /back/i })

    expect(back.getAttribute('href')).toBe('/projects/p1')
  })

  it('shows something for an address that matches no route', async () => {
    stubApi()

    render(<MemoryRouter initialEntries={['/submains/s1']}><App /></MemoryRouter>)

    // This exact address is where the old Back link went, and it rendered
    // nothing at all — which reads as a broken app rather than a wrong link.
    expect(await screen.findByRole('heading', { name: /nothing here/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /back to houses/i })).toBeInTheDocument()
  })
})

function Where() {
  const { pathname } = useLocation()
  return <output aria-label="location">{pathname}</output>
}

describe('Old automation panel addresses', () => {
  beforeEach(() => vi.restoreAllMocks())

  it.each([
    ['/submains/s1/panel', '/panels/s1'],
    ['/submains/s1/edit', '/panels/s1/edit'],
    ['/submains/s1/bom', '/panels/s1/bom'],
    ['/projects/p1/submains/new', '/projects/p1/panels/new'],
  ])('sends %s to %s', async (oldUrl, newUrl) => {
    stubApi()

    render(<MemoryRouter initialEntries={[oldUrl]}><App /><Where /></MemoryRouter>)

    await waitFor(() => expect(screen.getByLabelText('location').textContent).toBe(newUrl))
    expect(screen.queryByRole('heading', { name: /nothing here/i })).not.toBeInTheDocument()
  })
})
