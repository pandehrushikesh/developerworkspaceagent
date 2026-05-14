import type { QueryResponse, StreamEvent } from './types'

export async function sendQuery(
  workspacePath: string,
  prompt: string,
  signal?: AbortSignal,
): Promise<QueryResponse> {
  const response = await fetch('/api/query', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ workspacePath, prompt }),
    signal,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(`API error ${response.status}: ${text}`)
  }

  return response.json() as Promise<QueryResponse>
}

export async function* streamQuery(
  workspacePath: string,
  prompt: string,
  signal?: AbortSignal,
): AsyncGenerator<StreamEvent> {
  const response = await fetch('/api/query/stream', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ workspacePath, prompt }),
    signal,
  })

  if (!response.ok || !response.body) {
    const text = await response.text().catch(() => '')
    throw new Error(`API error ${response.status}: ${text}`)
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })
      const parts = buffer.split('\n\n')
      buffer = parts.pop() ?? ''

      for (const part of parts) {
        const line = part.trim()
        if (!line.startsWith('data: ')) continue
        const json = line.slice(6).trim()
        if (!json) continue
        yield JSON.parse(json) as StreamEvent
      }
    }
  } finally {
    reader.releaseLock()
  }
}

export async function checkHealth(): Promise<boolean> {
  try {
    const response = await fetch('/api/health', { method: 'GET' })
    return response.ok
  } catch {
    return false
  }
}
