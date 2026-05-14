import { useState } from 'react'
import type { Message, ExecutionStep, ToolResult } from './types'
import { streamQuery } from './api'
import Sidebar from './components/Sidebar'
import ChatPanel from './components/ChatPanel'
import './App.css'

export default function App() {
  const [workspacePath, setWorkspacePath] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [loading, setLoading] = useState(false)

  function patchMessage(id: string, patch: Partial<Message>) {
    setMessages(prev => prev.map(m => m.id === id ? { ...m, ...patch } : m))
  }

  async function handleSend(prompt: string) {
    if (!workspacePath.trim() || !prompt.trim() || loading) return

    const id = crypto.randomUUID()
    const newMessage: Message = {
      id,
      prompt,
      loading: true,
      timestamp: new Date(),
      streamingSteps: [],
      streamingToolResults: [],
    }
    setMessages(prev => [...prev, newMessage])
    setLoading(true)

    const steps: ExecutionStep[] = []
    const toolResults: ToolResult[] = []

    try {
      for await (const event of streamQuery(workspacePath.trim(), prompt.trim())) {
        if (event.type === 'step') {
          steps.push({ description: event.description, success: event.success })
          patchMessage(id, { streamingSteps: [...steps] })
        } else if (event.type === 'tool_result') {
          toolResults.push({ toolName: event.toolName, success: event.success, errorMessage: event.errorMessage })
          patchMessage(id, { streamingToolResults: [...toolResults] })
        } else if (event.type === 'answer') {
          patchMessage(id, { streamingOutput: event.text })
        } else if (event.type === 'done') {
          const { type: _type, ...responseFields } = event
          patchMessage(id, {
            response: { ...responseFields },
            loading: false,
            streamingOutput: undefined,
            streamingSteps: undefined,
            streamingToolResults: undefined,
          })
        } else if (event.type === 'error') {
          patchMessage(id, {
            response: { success: false, errorMessage: event.message, executionSteps: steps, toolResults },
            loading: false,
            streamingOutput: undefined,
            streamingSteps: undefined,
            streamingToolResults: undefined,
          })
        }
      }
    } catch (err) {
      patchMessage(id, {
        response: {
          success: false,
          errorMessage: err instanceof Error ? err.message : 'An unexpected error occurred.',
          executionSteps: steps,
          toolResults,
        },
        loading: false,
        streamingOutput: undefined,
        streamingSteps: undefined,
        streamingToolResults: undefined,
      })
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
