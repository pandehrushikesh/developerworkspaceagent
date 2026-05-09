import { useState } from 'react'
import type { ExecutionStep, ToolResult } from '../types'
import './ExecutionTrace.css'

interface ExecutionTraceProps {
  steps: ExecutionStep[]
  toolResults: ToolResult[]
}

export default function ExecutionTrace({ steps, toolResults }: ExecutionTraceProps) {
  const [stepsOpen, setStepsOpen] = useState(false)
  const [toolsOpen, setToolsOpen] = useState(false)

  const failedSteps = steps.filter(s => !s.success).length
  const failedTools = toolResults.filter(t => !t.success).length

  return (
    <div className="trace">
      {steps.length > 0 && (
        <div className="trace-group">
          <button
            className="trace-toggle"
            onClick={() => setStepsOpen(o => !o)}
          >
            <span className="trace-arrow">{stepsOpen ? '▾' : '▸'}</span>
            <span className="trace-label">
              {steps.length} execution step{steps.length !== 1 ? 's' : ''}
            </span>
            {failedSteps > 0 && (
              <span className="trace-badge error">{failedSteps} failed</span>
            )}
          </button>
          {stepsOpen && (
            <ul className="trace-list">
              {steps.map((step, i) => (
                <li key={i} className={`trace-item ${step.success ? '' : 'trace-item-fail'}`}>
                  <span className="trace-icon">{step.success ? '✓' : '✗'}</span>
                  <span className="trace-desc">{step.description}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {toolResults.length > 0 && (
        <div className="trace-group">
          <button
            className="trace-toggle"
            onClick={() => setToolsOpen(o => !o)}
          >
            <span className="trace-arrow">{toolsOpen ? '▾' : '▸'}</span>
            <span className="trace-label">
              {toolResults.length} tool call{toolResults.length !== 1 ? 's' : ''}
            </span>
            {failedTools > 0 && (
              <span className="trace-badge error">{failedTools} failed</span>
            )}
          </button>
          {toolsOpen && (
            <ul className="trace-list">
              {toolResults.map((tool, i) => (
                <li key={i} className={`trace-item ${tool.success ? '' : 'trace-item-fail'}`}>
                  <span className="trace-icon">{tool.success ? '✓' : '✗'}</span>
                  <code className="trace-tool-name">{tool.toolName}</code>
                  {tool.errorMessage && (
                    <span className="trace-error"> — {tool.errorMessage}</span>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
