import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ProjectsPage } from '../src/routes/ProjectsPage'

const json = (body: unknown, status = 200, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': type } })

const projects = [{ id: 'p1', name: 'Willow House', address: '1 Lane', notes: null, submainCount: 2 }]

describe('ProjectsPage', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('lists the projects it loads', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(projects)))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByText('Willow House')).toBeInTheDocument()
    expect(screen.getByText(/2 submains/)).toBeInTheDocument()
  })

  it('shows an empty state rather than a bare list', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json([])))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByText(/no houses yet/i)).toBeInTheDocument()
  })

  it('reports a field error from the API instead of failing silently', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json([]))
      .mockResolvedValueOnce(json(
        { title: 'invalid', errors: { name: ['A project name is required.'] } }, 400, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText(/no houses yet/i)

    await userEvent.click(screen.getByRole('button', { name: /add house/i }))

    await waitFor(() => expect(screen.getByText('A project name is required.')).toBeInTheDocument())
  })

  it('shows a retry when the list cannot be loaded at all', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Server exploded' }, 500)))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByRole('alert')).toHaveTextContent('Server exploded')
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })
})
