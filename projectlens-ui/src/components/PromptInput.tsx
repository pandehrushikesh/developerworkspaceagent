import { useState, useRef } from 'react'
import './PromptInput.css'

interface PromptInputProps {
  onSend: (prompt: string) => void
  disabled: boolean
  placeholder?: string
}

export default function PromptInput({ onSend, disabled, placeholder }: PromptInputProps) {
  const [value, setValue] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  function handleSend() {
    const trimmed = value.trim()
    if (!trimmed || disabled) return
    onSend(trimmed)
    setValue('')
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto'
    }
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  function handleInput(e: React.ChangeEvent<HTMLTextAreaElement>) {
    setValue(e.target.value)
    const el = e.target
    el.style.height = 'auto'
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`
  }

  return (
    <div className="prompt-bar">
      <div className={`prompt-input-wrap ${disabled ? 'prompt-disabled' : ''}`}>
        <textarea
          ref={textareaRef}
          className="prompt-textarea"
          value={value}
          onChange={handleInput}
          onKeyDown={handleKeyDown}
          placeholder={placeholder ?? 'Ask anything about the codebase…'}
          rows={1}
          disabled={disabled}
          spellCheck={false}
        />
        <button
          className="prompt-send"
          onClick={handleSend}
          disabled={disabled || !value.trim()}
          title="Send (Enter)"
        >
          {disabled ? (
            <span className="send-spinner" />
          ) : (
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
              <path d="M1.5 1.5l13 6.5-13 6.5V9.5l9-1.5-9-1.5V1.5z" />
            </svg>
          )}
        </button>
      </div>
      <p className="prompt-hint">Enter to send · Shift+Enter for new line</p>
    </div>
  )
}
