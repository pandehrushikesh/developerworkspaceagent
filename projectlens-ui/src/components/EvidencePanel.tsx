import { useState } from 'react'
import type { EvidenceItem, EvidenceAssessment, EvidenceKind } from '../types'
import './EvidencePanel.css'

interface EvidencePanelProps {
  items: EvidenceItem[]
  assessment?: EvidenceAssessment
}

const KIND_LABELS: Record<EvidenceKind, string> = {
  SearchHit: 'search',
  DirectSnippet: 'read',
  FileSummary: 'summary',
  ToolObservation: 'observation',
  GitHistory: 'git',
}

export default function EvidencePanel({ items, assessment }: EvidencePanelProps) {
  const [open, setOpen] = useState(false)
  const [activeTab, setActiveTab] = useState<'summary' | 'items'>('summary')

  if (items.length === 0 && !assessment) return null

  const uniqueSources = new Set(items.map(i => i.sourceId)).size
  const avgConfidence = items.length > 0
    ? items.reduce((sum, i) => sum + i.confidence, 0) / items.length
    : 0

  return (
    <div className="evidence">
      <button className="evidence-toggle" onClick={() => setOpen(o => !o)}>
        <span className="trace-arrow">{open ? '▾' : '▸'}</span>
        <span className="trace-label">
          {items.length} evidence item{items.length !== 1 ? 's' : ''} · {uniqueSources} source{uniqueSources !== 1 ? 's' : ''}
        </span>
        {assessment && (
          <span className={`evidence-badge ${assessment.isSufficient ? 'sufficient' : 'partial'}`}>
            {assessment.isSufficient ? 'sufficient' : 'partial'}
          </span>
        )}
      </button>

      {open && (
        <div className="evidence-body">
          <div className="evidence-tabs">
            <button
              className={`evidence-tab ${activeTab === 'summary' ? 'active' : ''}`}
              onClick={() => setActiveTab('summary')}
            >
              Summary
            </button>
            <button
              className={`evidence-tab ${activeTab === 'items' ? 'active' : ''}`}
              onClick={() => setActiveTab('items')}
            >
              Items ({items.length})
            </button>
          </div>

          {activeTab === 'summary' && (
            <div className="evidence-summary">
              <div className="evidence-scores">
                <ScoreBar label="Coverage" value={assessment?.coverageScore ?? avgConfidence} />
                <ScoreBar label="Confidence" value={assessment?.confidenceScore ?? avgConfidence} />
              </div>
              {assessment?.reason && (
                <p className="evidence-reason">{assessment.reason}</p>
              )}
              {assessment?.missingAreas && assessment.missingAreas.length > 0 && (
                <div className="evidence-missing">
                  <span className="evidence-missing-label">Missing areas:</span>
                  <ul>
                    {assessment.missingAreas.map((area, i) => (
                      <li key={i}>{area}</li>
                    ))}
                  </ul>
                </div>
              )}
              <KindBreakdown items={items} />
            </div>
          )}

          {activeTab === 'items' && (
            <ul className="evidence-list">
              {items.map((item, i) => (
                <li key={i} className="evidence-item">
                  <div className="evidence-item-header">
                    <span className={`evidence-kind evidence-kind-${item.kind.toLowerCase()}`}>
                      {KIND_LABELS[item.kind]}
                    </span>
                    <span className="evidence-source" title={item.sourceId}>
                      {truncatePath(item.sourceId)}
                    </span>
                    {item.isPartial && <span className="evidence-partial">partial</span>}
                    <span className="evidence-conf">{Math.round(item.confidence * 100)}%</span>
                  </div>
                  <p className="evidence-content">{item.content}</p>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

function ScoreBar({ label, value }: { label: string; value: number }) {
  const pct = Math.round(Math.min(1, Math.max(0, value)) * 100)
  const colorClass = pct >= 65 ? 'high' : pct >= 40 ? 'mid' : 'low'
  return (
    <div className="score-bar">
      <span className="score-label">{label}</span>
      <div className="score-track">
        <div className={`score-fill score-fill-${colorClass}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="score-value">{pct}%</span>
    </div>
  )
}

function KindBreakdown({ items }: { items: EvidenceItem[] }) {
  const counts = items.reduce<Partial<Record<EvidenceKind, number>>>((acc, item) => {
    acc[item.kind] = (acc[item.kind] ?? 0) + 1
    return acc
  }, {})

  const entries = Object.entries(counts) as [EvidenceKind, number][]
  if (entries.length === 0) return null

  return (
    <div className="evidence-breakdown">
      {entries.map(([kind, count]) => (
        <span key={kind} className={`evidence-kind evidence-kind-${kind.toLowerCase()}`}>
          {KIND_LABELS[kind]} ×{count}
        </span>
      ))}
    </div>
  )
}

function truncatePath(path: string): string {
  if (path.length <= 40) return path
  const parts = path.replace(/\\/g, '/').split('/')
  if (parts.length <= 2) return path
  return '…/' + parts.slice(-2).join('/')
}
