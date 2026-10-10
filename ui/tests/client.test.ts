import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../src/api/client'

const json = (body: unknown, status: number, type = 'application/json') =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': type } })

describe('api client', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('returns parsed json on success', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ id: 'abc' }, 200)))

    await expect(api.get('/projects')).resolves.toEqual({ id: 'abc' })
  })

  it('marks a 409 as a conflict so callers can revert instead of erroring', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      json({ message: 'changed', currentLayoutVersion: 7 }, 409)))

    const error = await api.patch('/devices/1/position', {}).catch(e => e) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.conflict).toBe(true)
    expect(error.currentLayoutVersion).toBe(7)
  })

  it('surfaces validation problems field by field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      json({ title: 'invalid', errors: { name: ['A circuit name is required.'] } }, 400, 'application/problem+json')))

    const error = await api.post('/projects', {}).catch(e => e) as ApiError

    expect(error.fieldErrors.name).toEqual(['A circuit name is required.'])
  })

  it('separates the errors no field on the form owns, so a form can say them instead of swallowing them', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({
      title: 'One or more validation errors occurred.',
      errors: { name: ['A name is required.'], design: ['No ruleset is available.'], 'circuits[3].type': ["'Blinds' is not a circuit type."] },
    }, 400, 'application/problem+json')))

    const error = await api.post('/projects/p1/submains', {}).catch(e => e) as ApiError

    expect(error.errorsOutside(['name'])).toEqual(['No ruleset is available.', "'Blinds' is not a circuit type."])
    expect(error.errorsOutside(['name', 'design', 'circuits[3].type'])).toEqual([])
  })

  it('does not treat a 422 as a conflict', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ message: 'no room' }, 422)))

    const error = await api.patch('/devices/1/position', {}).catch(e => e) as ApiError

    expect(error.conflict).toBe(false)
    expect(error.status).toBe(422)
    expect(error.message).toBe('no room')
  })

  it('says what the generator refused, from the diagnostics a 422 carries', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({
      diagnostics: [
        { severity: 'Warning', code: 'X', message: 'Minor' },
        { severity: 'Error', code: 'ENCLOSURE_TOO_SMALL', message: 'This design needs 4 rows; the box has 3.' },
      ],
    }, 422)))

    const error = await api.post('/projects/p/submains', {}).catch(e => e) as ApiError

    expect(error.status).toBe(422)
    expect(error.message).toBe('This design needs 4 rows; the box has 3.')
  })

  it('handles an empty body without throwing on JSON.parse', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(api.post('/things')).resolves.toBeNull()
  })

  // What nginx in production and the Vite proxy in development send when the
  // API is down or restarting: an HTML page, not a problem document.
  it('turns an HTML 502 into an ApiError that says the server is not responding', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response('<html><body>Bad Gateway</body></html>', { status: 502, headers: { 'content-type': 'text/html' } })))

    const error = await api.post('/projects', { name: 'x' }).catch(e => e) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(502)
    expect(error.message).toMatch(/not responding \(502\)/)
    expect(error.fieldErrors).toEqual({})
  })

  it('turns an empty 500 into an ApiError with a message and no field errors', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))

    const error = await api.get('/projects').catch(e => e) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(500)
    expect(error.message).toMatch(/500/)
    expect(error.fieldErrors).toEqual({})
  })

  it('treats an unreadable 200 body as a failed request rather than a crash', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response('<!doctype html><title>Login</title>', { status: 200, headers: { 'content-type': 'text/html' } })))

    const error = await api.get('/projects').catch(e => e) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.message).toMatch(/could not read/i)
    expect(error.fieldErrors).toEqual({})
  })

  it('reports a network failure as an ApiError too', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Load failed')))

    const error = await api.get('/projects').catch(e => e) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(0)
    expect(error.message).toMatch(/could not reach the server/i)
    expect(error.fieldErrors).toEqual({})
  })
})
