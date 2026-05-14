import type { Message } from '../types'
import ExecutionTrace from './ExecutionTrace'
import EvidencePanel from './EvidencePanel'
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
            <div className="thinking-stream">
              {(message.streamingOutput) ? (
                <div className="answer-text streaming">
                  {formatAnswer(message.streamingOutput)}
                  <span className="stream-cursor" />
                </div>
              ) : (
                <div className="thinking">
                  <span className="thinking-dot" />
                  <span className="thinking-dot" />
                  <span className="thinking-dot" />
                </div>
              )}
              {(message.streamingSteps?.length || message.streamingToolResults?.length) ? (
                <ExecutionTrace
                  steps={message.streamingSteps ?? []}
                  toolResults={message.streamingToolResults ?? []}
                />
              ) : null}
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
              <EvidencePanel
                items={response.evidenceItems ?? []}
                assessment={response.finalAssessment}
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
