import { useEffect, type ReactNode } from 'react'
import { useAuth } from 'react-oidc-context'
import { setTokenGetter } from '../api/client'
import { AppShell } from '../components/AppShell'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Spinner } from '../components/Spinner'
import { APP_NAME } from '../useTitle'

export function AuthGate({ children }: { children: ReactNode }) {
  const auth = useAuth()

  useEffect(() => {
    setTokenGetter(() => auth.user?.access_token)
  }, [auth.user])

  if (auth.isLoading) return <Spinner label="Signing in" />

  if (auth.error) {
    return (
      <AppShell title={APP_NAME}>
        <ErrorNote message={auth.error.message} onRetry={() => void auth.signinRedirect()} />
      </AppShell>
    )
  }

  if (!auth.isAuthenticated) {
    return (
      <AppShell title={APP_NAME} subtitle="Panel drawings, schedules and parts lists, one submain at a time.">
        <p className="mb-4 text-ink-2">Sign in to see your houses.</p>
        <Button onClick={() => void auth.signinRedirect()}>Sign in</Button>
      </AppShell>
    )
  }

  return <>{children}</>
}
