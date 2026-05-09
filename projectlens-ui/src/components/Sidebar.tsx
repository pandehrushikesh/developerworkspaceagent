import { useState, useEffect } from 'react'
import { checkHealth } from '../api'
import './Sidebar.css'

interface SidebarProps {
  workspacePath: string
  onWorkspaceChange: (path: string) => void
  messageCount: number
  onClear: () => void
}

export default function Sidebar({ workspacePath, onWorkspaceChange, messageCount, onClear }: SidebarProps) {
  const [draft, setDraft] = useState(workspacePath)
  const [apiOnline, setApiOnline] = useState<boolean | null>(null)

  useEffect(() => {
    checkHealth().then(setApiOnline)
    const interval = setInterval(() => checkHealth().then(setApiOnline), 15_000)
    return () => clearInterval(interval)
  }, [])

  function handleApply() {
    onWorkspaceChange(draft.trim())
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Enter') handleApply()
  }

  const isSet = workspacePath.trim().length > 0

  return (
    <aside className="sidebar">
      <div className="sidebar-logo">
        <span className="sidebar-logo-icon">⬡</span>
        <span className="sidebar-logo-name">ProjectLens</span>
        <span className="sidebar-version">v1.0</span>
      </div>

      <div className="sidebar-section">
        <label className="sidebar-label">API Status</label>
        <div className="api-status">
          <span className={`status-dot ${apiOnline === null ? 'checking' : apiOnline ? 'online' : 'offline'}`} />
          <span className="status-text">
            {apiOnline === null ? 'Checking…' : apiOnline ? 'Online' : 'Offline'}
          </span>
        </div>
      </div>

      <div className="sidebar-section">
        <label className="sidebar-label" htmlFor="workspace-input">Workspace Path</label>
        <textarea
          id="workspace-input"
          className="workspace-input"
          value={draft}
          onChange={e => setDraft(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="C:\path\to\your\project"
          rows={3}
          spellCheck={false}
        />
        <button
          className={`btn-apply ${isSet && draft.trim() === workspacePath ? 'btn-set' : ''}`}
          onClick={handleApply}
          disabled={!draft.trim()}
        >
          {isSet && draft.trim() === workspacePath ? '✓ Workspace Set' : 'Set Workspace'}
        </button>
      </div>

      {isSet && (
        <div className="sidebar-section">
          <label className="sidebar-label">Session</label>
          <div className="session-info">
            <span className="session-stat">{messageCount} message{messageCount !== 1 ? 's' : ''}</span>
          </div>
          {messageCount > 0 && (
            <button className="btn-clear" onClick={onClear}>
              Clear conversation
            </button>
          )}
        </div>
      )}

      <div className="sidebar-footer">
        <p className="sidebar-hint">
          Set a workspace path, then ask anything about the codebase.
        </p>
      </div>
    </aside>
  )
}
