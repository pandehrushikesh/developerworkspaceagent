import type { Message } from '../types'
import ExecutionTrace from './ExecutionTrace'
import './MessageItem.css'

interface MessageItemProps {
  message: Message
}

export default function MessageItem({ message }: MessageItemProps) {
  const { prompt, response, loading } = message

  return (
    <div className="message-pair">
      <div className="message message-user">
        <div className="message-bubble message-bubble-user">
          <p>{prompt}</p>
        </div>
      </div>

      <div className="message message-agent">
        <div className="message-avatar">⬡</div>
        <div className="message-bubble message-bubble-agent">
          {loading ? (
            <div className="thinking">
              <span className="thinking-dot" />
              <span className="thinking-dot" />
              <span className="thinking-dot" />
            </div>
          ) : response ? (
            <>
              {response.success ? (
                <div className="answer-text">
                  {response.output
                    ? formatAnswer(response.output)
                    : <span className="answer-empty">No output returned.</span>
                  }
                </div>
              ) : (
                <div className="answer-error">
                  <span className="error-icon">⚠</span>
                  {response.errorMessage ?? 'The agent did not complete successfully.'}
                </div>
              )}
              <ExecutionTrace
                steps={response.executionSteps}
                toolResults={response.toolResults}
              />
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function formatAnswer(text: string): React.ReactNode {
  const lines = text.split('\n')
  const elements: React.ReactNode[] = []
  let codeLines: string[] = []
  let inCode = false

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    if (line.startsWith('```')) {
      if (inCode) {
        elements.push(<pre key={i}><code>{codeLines.join('\n')}</code></pre>)
        codeLines = []
        inCode = false
      } else {
        inCode = true
      }
    } else if (inCode) {
      codeLines.push(line)
    } else if (line.trim() === '') {
      elements.push(<br key={i} />)
    } else {
      elements.push(<p key={i}>{line}</p>)
    }
  }

  if (inCode && codeLines.length > 0) {
    elements.push(<pre key="last-code"><code>{codeLines.join('\n')}</code></pre>)
  }

  return <>{elements}</>
}
