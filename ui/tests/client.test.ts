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

  it('does not treat a 422 as a conflict', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ message: 'no room' }, 422)))

    const error = await api.patch('/devices/1/position', {}).catch(e => e) as ApiError

    expect(error.conflict).toBe(false)
    expect(error.status).toBe(422)
    expect(error.message).toBe('no room')
  })

  it('handles an empty body without throwing on JSON.parse', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))

    await expect(api.post('/things')).resolves.toBeNull()
  })
})
