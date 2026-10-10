import type { ReactNode } from 'react'
import { AuthProvider } from 'react-oidc-context'
import { AuthGate } from './AuthGate'
import { oidcSettings, type RuntimeConfig } from './runtimeConfig'

/**
 * The whole sign-in path behind one module boundary, so main.tsx can import
 * it only when an identity provider is configured. The OIDC library is about
 * a fifth of the app's JavaScript, and an install that leaves sign-in off —
 * development, and every deployment with the two settings blank — never runs
 * a line of it, so it should not download it either. A default export is
 * what React.lazy wants.
 */
export default function SignedIn({ config, children }: { config: RuntimeConfig; children: ReactNode }) {
  return (
    <AuthProvider {...oidcSettings(config)}>
      <AuthGate>{children}</AuthGate>
    </AuthProvider>
  )
}
