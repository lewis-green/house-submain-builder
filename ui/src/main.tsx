import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider } from 'react-oidc-context'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { AuthGate } from './auth/AuthGate'
import { authEnabled, loadRuntimeConfig, oidcSettings } from './auth/runtimeConfig'
import './index.css'

const routed = (
  <BrowserRouter>
    <App />
  </BrowserRouter>
)

// Settings arrive before the first render, so nobody is shown a signed-out app
// for a moment while the identity provider is still being looked up.
void loadRuntimeConfig().then((config) => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      {authEnabled(config)
        ? (
          <AuthProvider {...oidcSettings(config)}>
            <AuthGate>{routed}</AuthGate>
          </AuthProvider>
        )
        : routed}
    </StrictMode>,
  )
})
