import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AuthProvider } from 'react-oidc-context'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { AuthGate } from './auth/AuthGate'
import { authEnabled, oidcConfig } from './auth/config'
import './index.css'

const routed = (
  <BrowserRouter>
    <App />
  </BrowserRouter>
)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {authEnabled
      ? <AuthProvider {...oidcConfig}><AuthGate>{routed}</AuthGate></AuthProvider>
      : routed}
  </StrictMode>,
)
