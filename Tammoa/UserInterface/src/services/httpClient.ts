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
