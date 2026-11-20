import { defineConfig } from '@playwright/test'

// Needs the dev servers: Vite on 5173 (proxying /api to the backend on 8000). Use the run-test skill first.
export default defineConfig({
  testDir: 'e2e',
  use: { baseURL: 'http://localhost:5173' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
})
