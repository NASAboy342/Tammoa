import http from 'node:http'
import type { AddressInfo } from 'node:net'

export interface Seen {
  method: string
  path: string
  authorization?: string
  body?: any
}

/** A tiny stand-in for an Ollama server, so the connection and Ollama card flows are tested without a real model. */
export async function startFakeOllama(options: { key?: string; models?: string[] } = {}) {
  const seen: Seen[] = []
  const models = options.models ?? ['tiny:1b', 'big:70b']
  const server = http.createServer((req, res) => {
    let raw = ''
    req.on('data', (c) => (raw += c))
    req.on('end', () => {
      const body = raw ? JSON.parse(raw) : undefined
      const authorization = req.headers.authorization
      seen.push({ method: req.method ?? '', path: req.url ?? '', authorization, body })
      const send = (status: number, payload: unknown) => {
        res.writeHead(status, { 'Content-Type': 'application/json' })
        res.end(JSON.stringify(payload))
      }
      if (options.key && authorization !== `Bearer ${options.key}`) return send(401, { error: 'unauthorized' })
      if (req.url === '/api/version') return send(200, { version: '0.0.1-fake' })
      if (req.url === '/api/tags') return send(200, { models: models.map((name) => ({ name })) })
      if (req.url === '/api/chat') {
        if (body.model === 'missing') return send(404, { error: "model 'missing' not found" })
        const user = [...body.messages].reverse().find((m: any) => m.role === 'user')?.content
        return send(200, {
          model: body.model,
          message: { role: 'assistant', content: `fake answer to: ${user}` },
          done: true,
          done_reason: 'stop',
          prompt_eval_count: 4,
          eval_count: 5,
          total_duration: 1_234_000_000,
        })
      }
      send(404, { error: 'not found' })
    })
  })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}`
  return {
    url,
    seen,
    chats: () => seen.filter((s) => s.path === '/api/chat'),
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  }
}
