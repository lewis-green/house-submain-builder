import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { api } from '../src/api/client'

// The real provider would go and fetch the identity provider's metadata; the
// gate only needs what useAuth says.
const auth = {
  isLoading: false,
  isAuthenticated: false,
  error: undefined as Error | undefined,
  user: undefined as { access_token: string } | undefined,
  signinRedirect: vi.fn(),
}
vi.mock('react-oidc-context', () => ({
  AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  useAuth: () => auth,
}))

const config = { oidcAuthority: 'https://keycloak/realms/house', oidcClientId: 'panel-ui' }

/** Through the same dynamic import main.tsx uses, so the chunk boundary is what is tested. */
const load = async () => (await import('../src/auth/SignedIn')).default

describe('SignedIn', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    auth.isAuthenticated = false
    auth.user = undefined
  })

  it('asks to sign in before showing the houses', async () => {
    const SignedIn = await load()

    render(<SignedIn config={config}><h1>Houses</h1></SignedIn>)

    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Houses' })).not.toBeInTheDocument()
  })

  it('shows the app and hands the token to the api client once signed in', async () => {
    auth.isAuthenticated = true
    auth.user = { access_token: 'token-xyz' }
    const fetchMock = vi.fn(async () => new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const SignedIn = await load()

    render(<SignedIn config={config}><h1>Houses</h1></SignedIn>)

    expect(screen.getByRole('heading', { name: 'Houses' })).toBeInTheDocument()
    await api.get('/projects')
    expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].headers).toMatchObject({ authorization: 'Bearer token-xyz' })
  })
})
