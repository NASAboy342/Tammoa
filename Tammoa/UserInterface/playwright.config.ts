import { defineConfig } from '@playwright/test'

// Needs the dev servers: Vite on 5173 (proxying /api to the backend on 8000). Use the run-test skill first.
// Start the backend with TAMMOA_DATA_DIR=<temp folder> so tests never touch real flows or connections.
//
// WebKit: the macOS desktop window is a WKWebView, so native-fidelity.spec.ts runs only there
// (npx playwright install webkit once; run with: npx playwright test --project=webkit).
export default defineConfig({
  testDir: 'e2e',
  use: { baseURL: 'http://localhost:5173' },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' }, testIgnore: /native-fidelity\.spec\.ts/ },
    { name: 'webkit', use: { browserName: 'webkit' }, testMatch: /native-fidelity\.spec\.ts/ },
  ],
})
