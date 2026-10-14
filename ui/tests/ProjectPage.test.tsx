import { render, screen, within } from '@testing-library/react'
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
})
