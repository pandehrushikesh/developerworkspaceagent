export interface ExecutionStep {
  description: string
  success: boolean
}

export interface ToolResult {
  toolName: string
  success: boolean
  errorMessage?: string
}

export type EvidenceKind = 'SearchHit' | 'DirectSnippet' | 'FileSummary' | 'ToolObservation' | 'GitHistory'

export interface EvidenceItem {
  toolName: string
  sourceId: string
  content: string
  kind: EvidenceKind
  isPartial: boolean
  confidence: number
}

export interface EvidenceAssessment {
  isSufficient: boolean
  coverageScore: number
  confidenceScore: number
  reason: string
  missingAreas: string[]
}

export interface QueryResponse {
  success: boolean
  output?: string
  errorMessage?: string
  executionSteps: ExecutionStep[]
  toolResults: ToolResult[]
  evidenceItems?: EvidenceItem[]
  finalAssessment?: EvidenceAssessment
}

export interface Message {
  id: string
  prompt: string
  response?: QueryResponse
  loading: boolean
  timestamp: Date
  // incremental streaming state
  streamingOutput?: string
  streamingSteps?: ExecutionStep[]
  streamingToolResults?: ToolResult[]
}

// SSE stream event types
export type StreamEvent =
  | { type: 'step'; description: string; success: boolean }
  | { type: 'tool_result'; toolName: string; success: boolean; errorMessage?: string }
  | { type: 'answer'; text: string }
  | { type: 'done' } & QueryResponse
  | { type: 'error'; message: string }
