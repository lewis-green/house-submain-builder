export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly fieldErrors: Record<string, string[]> = {},
    readonly currentLayoutVersion?: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }

  /** A 409 means someone else changed the panel: reload, don't report a failure. */
  get conflict() {
    return this.status === 409
  }
}

let getToken: () => string | undefined = () => undefined

/** Set once by the auth provider. Kept out of React so non-component code can call the API. */
export function setTokenGetter(fn: () => string | undefined) {
  getToken = fn
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['content-type'] = 'application/json'

  const token = getToken()
  if (token) headers.authorization = `Bearer ${token}`

  const response = await fetch(`/api${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const text = await response.text()
  const payload = text ? JSON.parse(text) : null

  if (response.ok) return payload as T

  throw new ApiError(
    response.status,
    payload?.title ?? payload?.message ?? `Request failed (${response.status})`,
    payload?.errors ?? {},
    payload?.currentLayoutVersion,
  )
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
}
