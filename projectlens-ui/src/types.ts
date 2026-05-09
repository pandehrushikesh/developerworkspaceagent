export interface ExecutionStep {
  description: string
  success: boolean
}

export interface ToolResult {
  toolName: string
  success: boolean
  errorMessage?: string
}

export interface QueryResponse {
  success: boolean
  output?: string
  errorMessage?: string
  executionSteps: ExecutionStep[]
  toolResults: ToolResult[]
}

export interface Message {
  id: string
  prompt: string
  response?: QueryResponse
  loading: boolean
  timestamp: Date
}
