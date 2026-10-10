import { type ChildProcess, execFileSync, spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { connect } from 'node:net'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * The throwaway stack the e2e suite runs against: its own Postgres container,
 * its own API, and (started by Playwright's webServer) its own Vite. Every port
 * is unusual so it never collides with a development stack, and every one can
 * be overridden with an environment variable.
 *
 * A separate database server, rather than a scratch database on the
 * developer's own, so the suite needs to know nothing about where that is or
 * what the password might be — and so a test can never reach the wrong one.
 */
export const stack = {
  dbPort: Number(process.env.E2E_DB_PORT ?? 55434),
  apiPort: Number(process.env.E2E_API_PORT ?? 5021),
  // Not 5174: that is where Vite goes when a developer's own 5173 is taken,
  // and a dev server sitting there would stop the suite from starting.
  uiPort: Number(process.env.E2E_UI_PORT ?? 5199),
  get apiUrl() { return `http://localhost:${this.apiPort}` },
  get uiUrl() { return `http://localhost:${this.uiPort}` },
}

const CONTAINER = 'house-panel-e2e-pg'
const API_PROJECT = fileURLToPath(new URL('../../src/PubInvest.HouseConfig.Api', import.meta.url))

let api: ChildProcess | null = null

/**
 * Homebrew's bin is not on the PATH of every process that can launch this
 * suite (an editor's test runner, for one), so the usual places are tried too.
 */
function dockerBinary(): string {
  const candidates = [
    process.env.E2E_DOCKER,
    ...(process.env.PATH ?? '').split(':').map(dir => join(dir, 'docker')),
    '/opt/homebrew/bin/docker',
    '/usr/local/bin/docker',
  ]
  const found = candidates.find(path => path && existsSync(path))
  if (!found) {
    throw new Error('The e2e suite runs a throwaway Postgres in Docker, and no docker binary was found. Install Docker, or set E2E_DOCKER to it.')
  }
  return found
}

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

/** The Postgres image opens its TCP port only once it is ready to take connections. */
async function waitForPort(port: number, timeoutMs: number) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const open = await new Promise<boolean>(done => {
      const socket = connect({ port, host: '127.0.0.1' })
      socket.once('connect', () => { socket.destroy(); done(true) })
      socket.once('error', () => done(false))
    })
    if (open) return
    await sleep(250)
  }
  throw new Error(`Nothing is listening on port ${port} after ${timeoutMs / 1000}s`)
}

/** /health checks the database too, and the API only listens once it has migrated and seeded. */
async function waitForHealth(url: string, timeoutMs: number) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    if (api?.exitCode !== null && api?.exitCode !== undefined) throw new Error(`The e2e API exited with code ${api.exitCode}`)
    const ok = await fetch(`${url}/health`).then(r => r.ok).catch(() => false)
    if (ok) return
    await sleep(500)
  }
  throw new Error(`The e2e API at ${url} was not healthy after ${timeoutMs / 1000}s`)
}

export async function startStack() {
  const docker = dockerBinary()

  // A container left behind by a run that was killed would hold the name and
  // the port, and old data besides; it goes first.
  try { execFileSync(docker, ['rm', '-f', CONTAINER], { stdio: 'ignore' }) } catch { /* nothing to remove */ }
  execFileSync(docker, [
    'run', '-d', '--rm', '--name', CONTAINER,
    '-e', 'POSTGRES_PASSWORD=postgres',
    '-p', `${stack.dbPort}:5432`,
    'postgres:17',
  ], { stdio: 'ignore' })
  await waitForPort(stack.dbPort, 60_000)

  // The API migrates and seeds the catalogue on startup, so a fresh server is
  // a complete one. Its own process group, so the app `dotnet run` starts
  // inside goes down with it.
  api = spawn(process.env.E2E_DOTNET ?? 'dotnet', ['run', '--no-restore', '--no-launch-profile', '--project', API_PROJECT], {
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: stack.apiUrl,
      ConnectionStrings__HouseConfig: `Host=localhost;Port=${stack.dbPort};Database=HouseConfig;Username=postgres;Password=postgres`,
    },
    detached: true,
    stdio: ['ignore', 'ignore', 'inherit'],
  })
  await waitForHealth(stack.apiUrl, 120_000)
}

export function stopStack() {
  if (api?.pid && api.exitCode === null) {
    try { process.kill(-api.pid, 'SIGTERM') } catch { /* already gone */ }
  }
  api = null
  try { execFileSync(dockerBinary(), ['rm', '-f', CONTAINER], { stdio: 'ignore' }) } catch { /* already gone */ }
}
