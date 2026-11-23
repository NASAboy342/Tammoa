import http from 'node:http'
import type { AddressInfo } from 'node:net'

export interface Seen {
  method: string
  path: string
  authorization?: string
  body?: any
}

/**
 * A tiny stand-in for an Ollama server, so the connection and Ollama card flows are tested without a real model.
 * /api/chat STREAMS (newline-delimited JSON, like the real one). With `hold: true` it sends the first pieces at once and then WAITS
 * until the test calls release(): that makes "the answer is visible while the model is still writing" deterministic.
 * With `holdToolCalls` the model stays SILENT (like the real one while it writes a tool call) until the test calls releaseToolCalls().
 * With `toolScript` (and a request that offers tools) the model first asks for the scripted tool calls, one list per model call, then answers.
 */
export async function startFakeOllama(options: { key?: string; models?: string[]; hold?: boolean; holdToolCalls?: boolean; toolScript?: { name: string; arguments: Record<string, unknown> }[][] } = {}) {
  const seen: Seen[] = []
  const models = options.models ?? ['tiny:1b', 'big:70b']
  let release: () => void = () => {}
  let released = !options.hold
  const gate = new Promise<void>((resolve) => (release = () => ((released = true), resolve())))
  let releaseTools: () => void = () => {}
  const toolGate = options.holdToolCalls ? new Promise<void>((resolve) => (releaseTools = resolve)) : Promise.resolve()
  let aborted = 0
  let finished = 0

  const server = http.createServer((req, res) => {
    let raw = ''
    req.on('data', (c) => (raw += c))
    req.on('end', async () => {
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
        if (options.toolScript && body.tools?.length) {
          const step = body.messages.filter((m: any) => m.role === 'assistant').length
          const calls = options.toolScript[step]
          if (calls) {
            await toolGate
            res.writeHead(200, { 'Content-Type': 'application/x-ndjson' })
            res.write(JSON.stringify({ model: body.model, message: { role: 'assistant', content: '', tool_calls: calls.map((c) => ({ function: { name: c.name, arguments: c.arguments } })) }, done: false }) + '\n')
            res.end(JSON.stringify({ model: body.model, message: { role: 'assistant', content: '' }, done: true, done_reason: 'stop', prompt_eval_count: 3, eval_count: 2, total_duration: 500_000_000 }) + '\n')
            return
          }
        }
        const user = [...body.messages].reverse().find((m: any) => m.role === 'user')?.content
        const answer = `fake answer to: ${user}`
        const words = answer.split(/(?<= )/) // pieces that keep their trailing space
        const thoughts = body.think === true ? ['Let me think. ', 'The user asks something. ', 'I will answer plainly.'] : []
        res.writeHead(200, { 'Content-Type': 'application/x-ndjson' })
        let closed = false
        res.on('close', () => { if (!res.writableEnded) { closed = true; aborted++ } })
        const line = (o: unknown) => !closed && res.write(JSON.stringify(o) + '\n')
        const piece = (message: object) => line({ model: body.model, message: { role: 'assistant', content: '', ...message }, done: false })
        for (const t of thoughts) piece({ thinking: t })
        const early = Math.min(2, words.length)
        for (const w of words.slice(0, early)) piece({ content: w })
        if (!released) await gate // the model is "still writing": the test looks at the live view now
        for (const w of words.slice(early)) { piece({ content: w }); await new Promise((r) => setTimeout(r, 5)) }
        if (!closed) {
          line({ model: body.model, message: { role: 'assistant', content: '' }, done: true, done_reason: 'stop', prompt_eval_count: 4, eval_count: 5, total_duration: 1_234_000_000 })
          res.end()
          finished++
        }
        return
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
    /** Lets a held answer finish. */
    release: () => release(),
    /** Lets a held tool call (the model's silence) come through. */
    releaseToolCalls: () => releaseTools(),
    /** How many chat streams the client abandoned before they finished (e.g. a cancelled run). */
    aborted: () => aborted,
    finished: () => finished,
    close: () => { release(); releaseTools(); return new Promise<void>((resolve) => { server.closeAllConnections?.(); server.close(() => resolve()) }) },
  }
}
