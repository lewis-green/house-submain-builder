import { Component, type ErrorInfo, type ReactNode } from 'react'
import { AppShell } from './AppShell'
import { Button } from './Button'
import { buttonClass } from './buttonClass'
import { EmptyState } from './EmptyState'

interface State {
  error: Error | null
}

/**
 * The last line of defence. A render that throws anywhere below here shows a
 * screen that says so and offers a way on, instead of React unmounting the
 * whole app to a blank page — which on site reads as "the app is broken" and
 * throws away whatever was typed. A class, because React still has no hook for
 * catching render errors.
 */
export class ErrorBoundary extends Component<{ children: ReactNode }, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error(error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <AppShell title="Something went wrong" subtitle="The app hit a problem it could not recover from on its own.">
        <EmptyState
          title="This screen could not be drawn"
          hint={`${this.state.error.message}. Nothing has been lost on the server; try again, or start from the houses list.`}
          action={(
            <div className="flex flex-wrap justify-center gap-3">
              {/* Trying again re-renders what was there; the houses link is a full reload, for when that is not enough. */}
              <Button onClick={() => this.setState({ error: null })}>Try again</Button>
              <a href="/" className={buttonClass('secondary')}>Open the houses list</a>
            </div>
          )}
        />
      </AppShell>
    )
  }
}
