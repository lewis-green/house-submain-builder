import { act, render, screen, waitFor } from '@testing-library/react'
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
    expect(screen.getByText(/2 automation panels/)).toBeInTheDocument()
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

  it('keeps the typed house and says so when the API answers with an HTML error page', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json([]))
      .mockResolvedValueOnce(new Response('<html>Bad Gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText(/no houses yet/i)

    await userEvent.type(screen.getByLabelText('House name'), 'Keep me')
    await userEvent.type(screen.getByLabelText('Address'), 'Keep me too')
    await userEvent.click(screen.getByRole('button', { name: /add house/i }))

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not added: the server is not responding \(502\)/i))
    expect(screen.getByLabelText('House name')).toHaveValue('Keep me')
    expect(screen.getByLabelText('Address')).toHaveValue('Keep me too')
    expect(screen.getByRole('button', { name: /add house/i })).toBeEnabled()
  })

  it('says what the API refused when no field owns the error, instead of swallowing it', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json([]))
      .mockResolvedValueOnce(json(
        { title: 'One or more validation errors occurred.', errors: { notes: ['Notes are too long.'] } }, 400, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText(/no houses yet/i)

    await userEvent.type(screen.getByLabelText('House name'), 'Keep me')
    await userEvent.click(screen.getByRole('button', { name: /add house/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/not added: notes are too long/i))
    // Said once: the alert is itself a live region, so no status toast repeats it.
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
    expect(screen.getByLabelText('House name')).toHaveValue('Keep me')
  })

  it('clears the last attempt\'s message before saying what this one did', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(json([]))
      .mockResolvedValueOnce(new Response('<html>Bad Gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }))
      .mockResolvedValueOnce(json(
        { title: 'invalid', errors: { name: ['A project name is required.'] } }, 400, 'application/problem+json'))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText(/no houses yet/i)

    await userEvent.type(screen.getByLabelText('House name'), 'Keep me')
    await userEvent.click(screen.getByRole('button', { name: /add house/i }))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/not added: the server is not responding/i))

    // A different refusal, owned by the field: the old reason must not sit beside it.
    await userEvent.clear(screen.getByLabelText('House name'))
    await userEvent.click(screen.getByRole('button', { name: /add house/i }))

    await waitFor(() => expect(screen.getByText('A project name is required.')).toBeInTheDocument())
    expect(screen.getByRole('status')).toBeEmptyDOMElement()
  })

  it('adds one house when the button is activated twice in the same tick', async () => {
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? json({ id: 'p2', name: 'Once', address: null, notes: null, submainCount: 0 }, 201) : json(projects))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)
    await screen.findByText('Willow House')
    await userEvent.type(screen.getByLabelText('House name'), 'Once')

    // Before React has rendered the button disabled, so only a synchronous guard can stop the second.
    const add = screen.getByRole('button', { name: /add house/i })
    await act(async () => { add.click(); add.click() })

    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent(/added once/i))
    expect(fetchMock.mock.calls.filter(c => c[1]?.method === 'POST')).toHaveLength(1)
  })

  it('shows a retry when the list cannot be loaded at all', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Server exploded' }, 500)))

    render(<MemoryRouter><ProjectsPage /></MemoryRouter>)

    expect(await screen.findByRole('alert')).toHaveTextContent('Server exploded')
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })
})
