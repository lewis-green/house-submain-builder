import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Root } from './Root'
import { loadRuntimeConfig } from './auth/runtimeConfig'
import './index.css'

// Settings arrive before the first render, so nobody is shown a signed-out app
// for a moment while the identity provider is still being looked up.
void loadRuntimeConfig().then((config) => {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <Root config={config} />
    </StrictMode>,
  )
})
