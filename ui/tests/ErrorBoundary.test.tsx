import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ErrorBoundary } from '../src/components/ErrorBoundary'

/** Throws on its first render only, the way a one-off bad response would. */
let broken = true
function Flaky() {
  if (broken) throw new Error('fieldErrors is undefined')
  return <p>Back on its feet</p>
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    broken = true
    // React logs the caught error; that is expected here and not a failure.
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => vi.restoreAllMocks())

  it('shows a designed screen with a way on instead of a blank page', () => {
    render(<ErrorBoundary><Flaky /></ErrorBoundary>)

    expect(screen.getByRole('heading', { level: 1, name: /something went wrong/i })).toBeInTheDocument()
    expect(screen.getByText(/fieldErrors is undefined/)).toBeInTheDocument()
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /houses list/i })).toHaveAttribute('href', '/')
    expect(document.title).toMatch(/something went wrong/i)
  })

  it('recovers when Try again is tapped and the render succeeds', async () => {
    render(<ErrorBoundary><Flaky /></ErrorBoundary>)

    broken = false
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(screen.getByText('Back on its feet')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /something went wrong/i })).not.toBeInTheDocument()
  })

  it('renders its children untouched when nothing throws', () => {
    broken = false
    render(<ErrorBoundary><Flaky /></ErrorBoundary>)

    expect(screen.getByText('Back on its feet')).toBeInTheDocument()
  })
})
