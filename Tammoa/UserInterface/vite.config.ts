import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// The dev backend (Debug build) listens on 8000 or, when that is taken, on a random free port and writes it to this file.
// Read on every request, so restarting the backend on another port needs no restart of Vite.
const apiPortFile = path.join(os.tmpdir(), 'tammoa-api-port')
function apiTarget(): string {
  try {
    const port = Number(fs.readFileSync(apiPortFile, 'utf8').trim())
    if (Number.isInteger(port) && port >= 1024 && port <= 65535) return `http://localhost:${port}`
  } catch {
    /* no file: the backend is not running, or runs on the default port */
  }
  return 'http://localhost:8000'
}

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:8000',
        configure: (proxy) => {
          const web = proxy.web.bind(proxy) as (...args: unknown[]) => void
          const patched = (req: unknown, res: unknown, options: object, callback?: unknown) => {
            const withTarget = { ...options, target: apiTarget() }
            // http-proxy reads its arguments by position: an explicit `undefined` callback would make it drop the options
            return callback ? web(req, res, withTarget, callback) : web(req, res, withTarget)
          }
          Object.assign(proxy, { web: patched }) // choose the target per request
        },
      },
    },
  },
  build: { outDir: 'dist', emptyOutDir: true },
  test: { environment: 'jsdom', include: ['src/**/*.spec.ts'] },
})
