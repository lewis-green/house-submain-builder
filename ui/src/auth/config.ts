import type { AuthProviderProps } from 'react-oidc-context'

const authority = import.meta.env.VITE_OIDC_AUTHORITY as string | undefined
const clientId = import.meta.env.VITE_OIDC_CLIENT_ID as string | undefined

/** Auth is off when no authority is configured, matching HouseConfig__AuthEnabled=false on the API. */
export const authEnabled = Boolean(authority && clientId)

export const oidcConfig: AuthProviderProps = {
  authority: authority ?? '',
  client_id: clientId ?? '',
  redirect_uri: window.location.origin,
  onSigninCallback: () => {
    window.history.replaceState({}, document.title, window.location.pathname)
  },
}
