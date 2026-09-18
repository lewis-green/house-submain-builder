import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { IssueButton } from '../src/panel/IssueButton'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

describe('IssueButton', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('issues a revision and opens its pdf', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ id: 'r1' })))
    const open = vi.fn()
    vi.stubGlobal('open', open)

    render(<IssueButton submainId="s1" />)
    await userEvent.click(screen.getByRole('button', { name: /issue/i }))

    await waitFor(() => expect(open).toHaveBeenCalledWith(
      '/api/revisions/r1/export/pdf', '_blank', 'noopener'))
  })

  it('tells the engineer when issuing is refused instead of doing nothing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      json({ title: 'This submain has no panel yet.' }, 400)))
    vi.stubGlobal('open', vi.fn())

    render(<IssueButton submainId="s1" />)
    await userEvent.click(screen.getByRole('button', { name: /issue/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/no panel yet/i)
  })

  it('does not open a pdf when issuing failed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'nope' }, 400)))
    const open = vi.fn()
    vi.stubGlobal('open', open)

    render(<IssueButton submainId="s1" />)
    await userEvent.click(screen.getByRole('button', { name: /issue/i }))

    await screen.findByRole('alert')
    expect(open).not.toHaveBeenCalled()
  })
})
