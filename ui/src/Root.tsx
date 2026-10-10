import { Suspense, lazy } from 'react'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { authEnabled, type RuntimeConfig } from './auth/runtimeConfig'
import { ErrorBoundary } from './components/ErrorBoundary'
import { Spinner } from './components/Spinner'

// Fetched only when sign-in is on: see SignedIn.tsx for why it is its own chunk.
const SignedIn = lazy(() => import('./auth/SignedIn'))

/** The app, behind sign-in when the runtime config names an identity provider. */
export function Root({ config }: { config: RuntimeConfig }) {
  const routed = (
    <BrowserRouter>
      <ErrorBoundary>
        <App />
      </ErrorBoundary>
    </BrowserRouter>
  )

  if (!authEnabled(config)) return routed

  return (
    <Suspense fallback={<Spinner label="Signing in" />}>
      <SignedIn config={config}>{routed}</SignedIn>
    </Suspense>
  )
}
