import { defineConfig, devices } from '@playwright/test'
import { stack } from './e2e/stack'

/**
 * Runs in WebKit with iPhone emulation, because the people this is for use
 * it in Mobile Safari: touch input, the Safari toolbar, and WebKit's rules on
 * popups and gestures are what the suite has to exercise.
 *
 * The suite brings up its own stack and tears it down again: a throwaway
 * Postgres in Docker, an API on its own port pointed at it, and a Vite on its
 * own port proxying to that API (see e2e/stack.ts). The houses it creates
 * never touch a development database, and nothing has to be running first —
 * only Docker, the .NET SDK and a restored API build.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  // The tests share one API and one database; running them in parallel makes
  // one test's revision another test's "latest".
  workers: 1,
  globalSetup: './e2e/global-setup.ts',
  globalTeardown: './e2e/global-teardown.ts',
  use: {
    baseURL: stack.uiUrl,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'iphone-webkit', use: { ...devices['iPhone 15'], browserName: 'webkit' } },
  ],
  // Vite starts here, before global setup, and proxies /api to the API that
  // global setup then brings up.
  webServer: {
    command: `npm run dev -- --port ${stack.uiPort} --strictPort`,
    url: stack.uiUrl,
    env: { API_URL: stack.apiUrl },
    reuseExistingServer: false,
    timeout: 60_000,
  },
})
