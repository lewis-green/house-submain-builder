import { defineConfig, devices } from '@playwright/test'

/**
 * Needs a running stack:
 *   docker-compose up -d postgres
 *   dotnet run --project src/PubInvest.HouseConfig.Api
 * Vite is started by the webServer block below.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  use: {
    baseURL: 'http://localhost:5173',
    ...devices['Pixel 7'],
    trace: 'retain-on-failure',
    // PW_CHANNEL=chrome runs against a locally installed Chrome, for machines
    // where the bundled chromium download is blocked. CI leaves it unset.
    channel: process.env.PW_CHANNEL,
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 60_000,
  },
})
