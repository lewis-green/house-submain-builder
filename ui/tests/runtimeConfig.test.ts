import { beforeEach, describe, expect, it, vi } from 'vitest'
import { authEnabled, loadRuntimeConfig, oidcSettings } from '../src/auth/runtimeConfig'

const served = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

describe('runtime config', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('reads the identity provider from the served config', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      served({ oidcAuthority: 'https://keycloak/realms/house', oidcClientId: 'panel-ui' })))

    const config = await loadRuntimeConfig()

    expect(config.oidcAuthority).toBe('https://keycloak/realms/house')
    expect(config.oidcClientId).toBe('panel-ui')
    expect(authEnabled(config)).toBe(true)
  })

  it('never caches the config, so a redeploy is not answered from an old copy', async () => {
    const fetchMock = vi.fn().mockResolvedValue(served({ oidcAuthority: '', oidcClientId: '' }))
    vi.stubGlobal('fetch', fetchMock)

    await loadRuntimeConfig()

    expect(fetchMock).toHaveBeenCalledWith('/config.json', { cache: 'no-store' })
  })

  it('leaves auth off when the container set no identity provider', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(served({ oidcAuthority: '', oidcClientId: '' })))

    expect(authEnabled(await loadRuntimeConfig())).toBe(false)
  })

  it('leaves auth off when only one of the two settings is present', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      served({ oidcAuthority: 'https://keycloak/realms/house', oidcClientId: '' })))

    expect(authEnabled(await loadRuntimeConfig())).toBe(false)
  })

  it('runs without auth when no config is served at all', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('Not found', { status: 404 })))

    expect(authEnabled(await loadRuntimeConfig())).toBe(false)
  })

  it('runs without auth when the config cannot be fetched', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('network')))

    expect(authEnabled(await loadRuntimeConfig())).toBe(false)
  })

  it('ignores values of the wrong type rather than handing them to oidc-client', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(served({ oidcAuthority: 42, oidcClientId: null })))

    expect(await loadRuntimeConfig()).toEqual({ oidcAuthority: '', oidcClientId: '' })
  })

  it('signs in back to this origin', () => {
    const settings = oidcSettings({ oidcAuthority: 'https://keycloak/realms/house', oidcClientId: 'panel-ui' })

    expect(settings.authority).toBe('https://keycloak/realms/house')
    expect(settings.client_id).toBe('panel-ui')
    expect(settings.redirect_uri).toBe(window.location.origin)
  })
})
