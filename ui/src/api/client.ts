export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>
  readonly currentLayoutVersion?: number

  constructor(
    status: number,
    message: string,
    fieldErrors: Record<string, string[]> = {},
    currentLayoutVersion?: number,
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
    this.currentLayoutVersion = currentLayoutVersion
  }

  /** A 409 means someone else changed the panel: reload, don't report a failure. */
  get conflict() {
    return this.status === 409
  }

  /**
   * The messages filed under keys no field on the form owns — `design`,
   * `circuits[3].type`, a key added to the API later. A form that only shows
   * the errors for its fields would swallow these, and the screen would look
   * healthy with nothing done; they belong at the form's level instead.
   */
  errorsOutside(shownFields: string[]): string[] {
    return Object.entries(this.fieldErrors)
      .filter(([field]) => !shownFields.includes(field))
      .flatMap(([, messages]) => messages)
  }
}

let getToken: () => string | undefined = () => undefined

/** Set once by the auth provider. Kept out of React so non-component code can call the API. */
export function setTokenGetter(fn: () => string | undefined) {
  getToken = fn
}

/** What the API puts in a problem response. Every field is optional, because not every error comes from the API. */
interface Problem {
  title?: string
  message?: string
  errors?: Record<string, string[]>
  currentLayoutVersion?: number
  /** A 422 from the generator is the refused design itself; its diagnostics say why. */
  diagnostics?: { severity: string; message: string }[]
}

/** The first thing the generator could not do, in its own words — the only part of a refused design worth a status line. */
const firstError = (problem: Problem) =>
  problem.diagnostics?.find(d => d.severity === 'Error')?.message

/**
 * The body as JSON when it is JSON, null when it is empty, and undefined when
 * it is anything else — the HTML page nginx or the Vite proxy sends when the
 * API is down or restarting, for instance. That page is not a parse error for
 * the app to crash on; it is a failed request like any other.
 */
function parseBody(text: string): unknown {
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    return undefined
  }
}

/** Said in words an electrician can act on, for responses that carry no message of their own. */
function describe(status: number): string {
  if (status === 502 || status === 503 || status === 504) return `The server is not responding (${status}). Try again in a moment.`
  if (status === 500) return 'The server hit a problem (500). Try again.'
  if (status === 401 || status === 403) return `You are not signed in, or not allowed to do this (${status}).`
  if (status === 404) return 'That is no longer there (404).'
  if (status === 422) return 'The panel cannot be laid out that way (422).'
  return `Request failed (${status}).`
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['content-type'] = 'application/json'

  const token = getToken()
  if (token) headers.authorization = `Bearer ${token}`

  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    // fetch rejects only when no reply came back at all: no signal in the
    // plant room, a dropped connection, the server unreachable.
    throw new ApiError(0, 'Could not reach the server. Check the connection and try again.')
  }

  const payload = parseBody(await response.text())

  if (response.ok) {
    if (payload === undefined) throw new ApiError(response.status, 'The server sent a reply the app could not read. Try again.')
    return payload as T
  }

  const problem: Problem = typeof payload === 'object' && payload !== null ? payload : {}
  throw new ApiError(
    response.status,
    problem.title ?? problem.message ?? firstError(problem) ?? describe(response.status),
    problem.errors ?? {},
    problem.currentLayoutVersion,
  )
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body ?? {}),
  patch: <T>(path: string, body: unknown) => request<T>('PATCH', path, body),
}
