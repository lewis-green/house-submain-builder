/**
 * Settings read when the app starts, not when it was built.
 *
 * The UI ships as one image that any environment can run, so anything that
 * differs between environments has to arrive at runtime. The container writes
 * /config.json on start from its environment; in development the file shipped
 * in public/ is served instead, with everything blank.
 */
export interface RuntimeConfig {
  oidcAuthority: string
  oidcClientId: string
}

const blank: RuntimeConfig = { oidcAuthority: '', oidcClientId: '' }

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    // no-store: a stale config.json would point a redeployed app at the wrong
    // identity provider, and nothing would look wrong until someone signed in.
    const response = await fetch('/config.json', { cache: 'no-store' })
    if (!response.ok) return blank

    const body = await response.json()
    return {
      oidcAuthority: typeof body?.oidcAuthority === 'string' ? body.oidcAuthority : '',
      oidcClientId: typeof body?.oidcClientId === 'string' ? body.oidcClientId : '',
    }
  } catch {
    // A missing or unreadable config means no identity provider, which is a
    // working state: it pairs with HouseConfig__AuthEnabled=false on the API.
    return blank
  }
}

export const authEnabled = (config: RuntimeConfig) =>
  Boolean(config.oidcAuthority && config.oidcClientId)

export function oidcSettings(config: RuntimeConfig) {
  return {
    authority: config.oidcAuthority,
    client_id: config.oidcClientId,
    redirect_uri: window.location.origin,
    onSigninCallback: () => {
      window.history.replaceState({}, document.title, window.location.pathname)
    },
  }
}
