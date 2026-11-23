/** Plain text (for example a Markdown document). Non-2xx → Error(message from the JSON error body, else the status). */
export async function requestText(url: string): Promise<string> {
  const res = await fetch(url)
  const text = await res.text()
  if (!res.ok) {
    let message = `Request failed (${res.status})`
    try {
      message = (JSON.parse(text) as { error?: string }).error ?? message
    } catch {
      /* not JSON: keep the status message */
    }
    throw new Error(message)
  }
  return text
}

/** The only place that calls fetch. JSON in/out, non-2xx → Error(message), 204 → undefined. */
export async function requestJson<T>(url: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(url, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
  })
  if (res.status === 204) return undefined as T
  const text = await res.text()
  const body = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new Error((body as { error?: string } | undefined)?.error ?? `Request failed (${res.status})`)
  return body as T
}
