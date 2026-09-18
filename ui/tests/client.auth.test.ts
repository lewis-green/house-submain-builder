import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api, setTokenGetter } from '../src/api/client'

const ok = () => new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } })
const headersOf = (mock: ReturnType<typeof vi.fn>) => mock.mock.calls[0][1].headers as Record<string, string>

describe('api client auth', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    setTokenGetter(() => undefined)
  })

  it('sends no authorization header when there is no token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(ok())
    vi.stubGlobal('fetch', fetchMock)

    await api.get('/projects')

    expect(headersOf(fetchMock).authorization).toBeUndefined()
  })

  it('sends a bearer token when one is available', async () => {
    const fetchMock = vi.fn().mockResolvedValue(ok())
    vi.stubGlobal('fetch', fetchMock)
    setTokenGetter(() => 'token-abc')

    await api.get('/projects')

    expect(headersOf(fetchMock).authorization).toBe('Bearer token-abc')
  })

  it('reads the token afresh on every call rather than caching a stale one', async () => {
    // A fresh Response per call: a body can only be read once.
    const fetchMock = vi.fn().mockImplementation(async () => ok())
    vi.stubGlobal('fetch', fetchMock)

    let current = 'first'
    setTokenGetter(() => current)

    await api.get('/projects')
    current = 'second'
    await api.get('/projects')

    expect((fetchMock.mock.calls[0][1].headers as Record<string, string>).authorization).toBe('Bearer first')
    expect((fetchMock.mock.calls[1][1].headers as Record<string, string>).authorization).toBe('Bearer second')
  })
})
