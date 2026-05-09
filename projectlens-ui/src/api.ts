import type { QueryResponse } from './types'

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

export async function checkHealth(): Promise<boolean> {
  try {
    const response = await fetch('/api/health', { method: 'GET' })
    return response.ok
  } catch {
    return false
  }
}
