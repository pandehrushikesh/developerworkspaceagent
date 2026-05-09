import { useState } from 'react'
import type { Message } from './types'
import { sendQuery } from './api'
import Sidebar from './components/Sidebar'
import ChatPanel from './components/ChatPanel'
import './App.css'

export default function App() {
  const [workspacePath, setWorkspacePath] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [loading, setLoading] = useState(false)

  async function handleSend(prompt: string) {
    if (!workspacePath.trim() || !prompt.trim() || loading) return

    const id = crypto.randomUUID()
    const newMessage: Message = { id, prompt, loading: true, timestamp: new Date() }
    setMessages(prev => [...prev, newMessage])
    setLoading(true)

    try {
      const response = await sendQuery(workspacePath.trim(), prompt.trim())
      setMessages(prev =>
        prev.map(m => m.id === id ? { ...m, response, loading: false } : m),
      )
    } catch (err) {
      const errorResponse = {
        success: false,
        errorMessage: err instanceof Error ? err.message : 'An unexpected error occurred.',
        executionSteps: [],
        toolResults: [],
      }
      setMessages(prev =>
        prev.map(m => m.id === id ? { ...m, response: errorResponse, loading: false } : m),
      )
    } finally {
      setLoading(false)
    }
  }

  function handleClear() {
    setMessages([])
  }

  return (
    <div className="app">
      <Sidebar
        workspacePath={workspacePath}
        onWorkspaceChange={setWorkspacePath}
        messageCount={messages.length}
        onClear={handleClear}
      />
      <ChatPanel
        messages={messages}
        loading={loading}
        workspaceReady={workspacePath.trim().length > 0}
        onSend={handleSend}
      />
    </div>
  )
}
