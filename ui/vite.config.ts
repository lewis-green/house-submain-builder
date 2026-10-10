/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // One origin in development, so CORS is a production concern rather than
    // something to fight locally. The target can be overridden so the e2e
    // suite can run its own Vite against its own, throwaway API.
    proxy: {
      '/api': {
        target: process.env.API_URL ?? 'http://localhost:5020',
        changeOrigin: true,
        rewrite: path => path.replace(/^\/api/, ''),
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./tests/setup.ts'],
    // e2e/ belongs to Playwright; Vitest cannot run @playwright/test specs.
    include: ['tests/**/*.{test,spec}.{ts,tsx}'],
  },
})
