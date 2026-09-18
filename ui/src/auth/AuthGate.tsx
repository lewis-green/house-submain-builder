import { useEffect, type ReactNode } from 'react'
import { useAuth } from 'react-oidc-context'
import { setTokenGetter } from '../api/client'
import { Button } from '../components/Button'
import { ErrorNote } from '../components/ErrorNote'
import { Spinner } from '../components/Spinner'

export function AuthGate({ children }: { children: ReactNode }) {
  const auth = useAuth()

  useEffect(() => {
    setTokenGetter(() => auth.user?.access_token)
  }, [auth.user])

  if (auth.isLoading) return <Spinner label="Signing in" />
  if (auth.error) return <ErrorNote message={auth.error.message} onRetry={() => void auth.signinRedirect()} />

  if (!auth.isAuthenticated) {
    return (
      <div className="space-y-4 p-6">
        <h1 className="text-xl font-semibold">House Panel Designer</h1>
        <p className="text-slate-600">Sign in to see your houses.</p>
        <Button onClick={() => void auth.signinRedirect()}>Sign in</Button>
      </div>
    )
  }

  return <>{children}</>
}
