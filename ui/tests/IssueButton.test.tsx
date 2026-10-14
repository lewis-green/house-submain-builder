import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { IssueButton } from '../src/panel/IssueButton'
import { StatusRegion } from '../src/components/StatusRegion'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

/** GET lists what is issued; POST issues. A fresh Response per call: a body reads once. */
const stub = (existing: unknown[], post: () => Response) =>
  vi.fn(async (_url: string, init?: RequestInit) => init?.method === 'POST' ? post() : json(existing))

const show = (syncKey = 0) => render(<><IssueButton submainId="s1" layoutVersion={4} syncKey={syncKey} /><StatusRegion /></>)

describe('IssueButton', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('issues a revision and offers its pdf as a link, never a window.open after the request', async () => {
    vi.stubGlobal('fetch', stub([], () => json({ id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current: true })))
    const open = vi.fn()
    vi.stubGlobal('open', open)

    show()
    await userEvent.click(screen.getByRole('button', { name: /issue pdf/i }))

    const link = await screen.findByRole('link', { name: /open pdf/i })
    expect(link).toHaveAttribute('href', '/api/revisions/r1/export/pdf')
    expect(link).toHaveAttribute('target', '_blank')
    expect(open).not.toHaveBeenCalled()
    expect(screen.getByRole('status')).toHaveTextContent(/revision 1 issued/i)
  })

  it('offers the latest pdf straight away when one has already been issued', async () => {
    vi.stubGlobal('fetch', stub([
      { id: 'r1', submainId: 's1', number: 1, layoutVersion: 3, issuedAt: '2026-01-01', issuedBy: 'a', current: false },
      { id: 'r2', submainId: 's1', number: 2, layoutVersion: 4, issuedAt: '2026-01-02', issuedBy: 'a', current: true },
    ], () => json({})))

    show()

    expect(await screen.findByRole('link', { name: /open pdf \(revision 2\)/i }))
      .toHaveAttribute('href', '/api/revisions/r2/export/pdf')
    expect(screen.getByRole('button', { name: /issue new revision/i })).toBeInTheDocument()
  })

  it('says when the panel has moved on since the last pdf, on the server\'s word', async () => {
    // The layout version still matches: the server knows a rename changed the
    // schedule, which the client cannot tell from the version alone.
    vi.stubGlobal('fetch', stub([
      { id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current: false },
    ], () => json({})))

    show()

    expect(await screen.findByText(/changed since that pdf was issued/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /issue new revision/i }).className).toMatch(/bg-accent/)
  })

  it('asks the server again after each save, so a rename marks the pdf stale without a reload', async () => {
    let current = true
    const fetchMock = vi.fn(async () => json([
      { id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current },
    ]))
    vi.stubGlobal('fetch', fetchMock)

    const { rerender } = show(0)
    expect(await screen.findByRole('link', { name: /open pdf/i })).toBeInTheDocument()
    expect(screen.queryByText(/changed since/i)).not.toBeInTheDocument()

    current = false
    rerender(<><IssueButton submainId="s1" layoutVersion={4} syncKey={1} /><StatusRegion /></>)

    expect(await screen.findByText(/changed since that pdf was issued/i)).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('says the number the server gave the revision, not a count of what it happened to hold', async () => {
    // Two already issued but the list is still on its way; the server says the new one is the third.
    vi.stubGlobal('fetch', stub([], () => json({ id: 'r3', submainId: 's1', number: 3, layoutVersion: 4, issuedAt: '2026-01-03', issuedBy: 'a', current: true })))

    show()
    await userEvent.click(screen.getByRole('button', { name: /issue pdf/i }))

    expect(await screen.findByRole('link', { name: /open pdf \(revision 3\)/i })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/revision 3 issued/i)
  })

  it('does not pretend nothing is issued when the list could not be loaded, and offers to try again', async () => {
    let failing = true
    vi.stubGlobal('fetch', vi.fn(async () => failing
      ? json({ title: 'Revisions down' }, 500)
      : json([{ id: 'r2', submainId: 's1', number: 2, layoutVersion: 4, issuedAt: '2026-01-02', issuedBy: 'a', current: true }])))

    show()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(/could not check the issued pdfs/i)
    expect(alert).toHaveTextContent(/revisions down/i)
    expect(screen.queryByRole('button', { name: /issue pdf revision/i })).not.toBeInTheDocument()

    failing = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(await screen.findByRole('link', { name: /open pdf \(revision 2\)/i })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the last known pdf on offer when a later check fails, and says the check failed', async () => {
    let failing = false
    const fetchMock = vi.fn(async () => failing
      ? json({ title: 'Revisions down' }, 500)
      : json([{ id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current: true }]))
    vi.stubGlobal('fetch', fetchMock)

    const { rerender } = show(0)
    expect(await screen.findByRole('link', { name: /open pdf \(revision 1\)/i })).toBeInTheDocument()

    failing = true
    rerender(<><IssueButton submainId="s1" layoutVersion={4} syncKey={1} /><StatusRegion /></>)

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not check whether that pdf is still current/i)
    expect(screen.getByRole('link', { name: /open pdf \(revision 1\)/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /issue new revision/i })).toBeInTheDocument()
  })

  it('issues one revision when the button is activated twice in the same tick', async () => {
    const fetchMock = stub([], () => json({ id: 'r1', submainId: 's1', number: 1, layoutVersion: 4, issuedAt: '2026-01-01', issuedBy: 'a', current: true }))
    vi.stubGlobal('fetch', fetchMock)

    show()
    // Before React has rendered the button disabled: a revision is numbered and
    // kept, so only a synchronous guard can stop the second going to the server.
    const issue = screen.getByRole('button', { name: /issue pdf/i })
    await act(async () => { issue.click(); issue.click() })

    expect(await screen.findByRole('link', { name: /open pdf \(revision 1\)/i })).toBeInTheDocument()
    expect(fetchMock.mock.calls.filter(c => c[1]?.method === 'POST')).toHaveLength(1)
  })

  it('tells the engineer when issuing is refused instead of doing nothing', async () => {
    vi.stubGlobal('fetch', stub([], () => json({ title: 'This automation panel has no layout yet.' }, 400)))

    show()
    await userEvent.click(screen.getByRole('button', { name: /issue pdf/i }))

    expect(await screen.findByRole('status')).toHaveTextContent(/no layout yet/i)
    expect(screen.queryByRole('link', { name: /open pdf/i })).not.toBeInTheDocument()
  })
})
