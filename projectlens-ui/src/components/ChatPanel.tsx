import { useEffect, useRef } from 'react'
import type { Message } from '../types'
import MessageItem from './MessageItem'
import PromptInput from './PromptInput'
import './ChatPanel.css'

interface ChatPanelProps {
  messages: Message[]
  loading: boolean
  workspaceReady: boolean
  onSend: (prompt: string) => void
}

export default function ChatPanel({ messages, loading, workspaceReady, onSend }: ChatPanelProps) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  return (
    <div className="chat-panel">
      <div className="chat-messages">
        {messages.length === 0 ? (
          <div className="chat-empty">
            {workspaceReady ? (
              <>
                <div className="chat-empty-icon">⬡</div>
                <h2 className="chat-empty-title">Workspace ready</h2>
                <p className="chat-empty-sub">
                  Ask anything — what a function does, how files are connected,
                  where something is defined, or ask the agent to make a change.
                </p>
                <ul className="chat-empty-examples">
                  <li onClick={() => onSend('Give me an overview of this codebase')}>
                    Give me an overview of this codebase
                  </li>
                  <li onClick={() => onSend('How does the agent orchestration loop work?')}>
                    How does the agent orchestration loop work?
                  </li>
                  <li onClick={() => onSend('What tools are available and what do they do?')}>
                    What tools are available and what do they do?
                  </li>
                </ul>
              </>
            ) : (
              <>
                <div className="chat-empty-icon">⬡</div>
                <h2 className="chat-empty-title">ProjectLens</h2>
                <p className="chat-empty-sub">
                  Set a workspace path in the sidebar to get started.
                </p>
              </>
            )}
          </div>
        ) : (
          messages.map(message => (
            <MessageItem key={message.id} message={message} />
          ))
        )}
        <div ref={bottomRef} />
      </div>

      <PromptInput
        onSend={onSend}
        disabled={loading || !workspaceReady}
        placeholder={
          !workspaceReady
            ? 'Set a workspace path first…'
            : loading
              ? 'Agent is thinking…'
              : undefined
        }
      />
    </div>
  )
}
