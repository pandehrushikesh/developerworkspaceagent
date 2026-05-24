# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**ProjectLens** is an AI-powered developer workspace agent built on .NET 8 + ASP.NET Core. It explores and reasons over local codebases using a bounded tool-execution loop driven by an LLM. It exposes a REST API and a React chat UI.

## Commands

### Backend (.NET)

```bash
# Build entire solution
dotnet build ProjectLens.sln

# Run the API (http://localhost:5000)
dotnet run --project ProjectLens.Api

# Run the console host
dotnet run --project ProjectLens.Host

# Run all tests
dotnet test ProjectLens.Tests

# Run a specific test by name pattern
dotnet test ProjectLens.Tests --filter "FullyQualifiedName~<TestName>"
```

### Frontend (Vite + React)

```bash
cd projectlens-ui
npm install
npm run dev    # dev server at http://localhost:3000 (proxies /api → backend)
npm run build  # production build
```

## Architecture

The solution follows Clean Architecture with strict dependency direction:

```
Host / Api → Infrastructure → Application → Domain
```

**Domain** (`ProjectLens.Domain`) — pure contracts only: `ITool`, `IAgentOrchestrator`, `AgentRequest`, `AgentResponse`, `EvidenceItem`, `EvidenceAssessment`.

**Application** (`ProjectLens.Application`) — orchestration logic: `AgentOrchestrator`, convergence policies, evidence evaluators, session context service. No infrastructure references allowed.

**Infrastructure** (`ProjectLens.Infrastructure`) — tool implementations (`ListFilesTool`, `ReadFileTool`, `WriteFileTool`, `EditFileTool`, `SearchFilesTool`, `GitLogTool`, `GitBlameTool`, `GitDiffTool`), model clients (`OpenAiModelClient`, `AnthropicModelClient`), session stores, file compressor, session summarizer, `LocalSemanticSearchService`.

**Api** (`ProjectLens.Api`) — ASP.NET Core entry point: `POST /api/query`, `POST /api/query/stream` (SSE), `GET /api/health`. Handles workspace path validation via `AllowedWorkspaceRoots`.

**Host** (`ProjectLens.Host`) — console entry point wiring the same DI setup as the API.

**UI** (`projectlens-ui`) — Vite + React 18 + TypeScript, no external UI library. `App.tsx` owns chat state; `api.ts` handles fetch and SSE parsing.

### Agent Loop (AgentOrchestrator)

```
User prompt
  → Session context load
  → Model call (OpenAI Responses API or Anthropic Messages API)
  → Tool execution (bounded per-iteration fetch budget)
  → Evidence collection (EvidenceItem with Kind, Confidence, IsPartial)
  → Convergence policy decision (broaden / deepen / finalize)
  → repeat or return AgentResponse
```

The LLM decides which tools to call; tools are deterministic and return structured results. The AI never has direct filesystem access — all reads and writes go through tools.

### Model Clients

- **OpenAI**: Uses Responses API with stateful chaining via `previous_response_id`.
- **Anthropic**: Uses Messages API with alternating-turn construction.
- Provider is selected via `appsettings.json` `Model:Provider` (`OpenAI` / `Anthropic` / `None`).

### Configuration (`ProjectLens.Api/appsettings.json`)

```json
{
  "Model": {
    "Provider": "OpenAI",
    "ApiKey": "<key>",
    "Model": "gpt-4o",
    "MaxIterations": 10
  },
  "AllowedOrigins": ["http://localhost:3000"],
  "AllowedWorkspaceRoots": []
}
```

## Architecture Rules

- **Domain stays pure**: no logic, no infrastructure references, only interfaces and models.
- **Application layer orchestrates**: no direct tool implementations, no HTTP/IO.
- **Tools must be deterministic**: no AI logic inside tool classes.
- **All filesystem access via tools**: never let the model touch the filesystem directly.
- **Do not bypass the orchestrator**: all agent actions flow through `AgentOrchestrator`.
- **Do not change public contracts** (`ITool`, `IAgentOrchestrator`, request/response DTOs) without explicit need.

## Key Files

| File | Role |
|------|------|
| `ProjectLens.Application/AgentOrchestrator.cs` | Core agent loop — convergence, tool execution, evidence tracking |
| `ProjectLens.Api/Program.cs` | DI setup, endpoint definitions, workspace security |
| `ProjectLens.Infrastructure/ProjectLensSettings.cs` | Config model and provider resolution |
| `projectlens-ui/src/App.tsx` | React root, chat/session state |
| `projectlens-ui/src/api.ts` | API client with SSE streaming support |
| `ProjectLens.Tests/Program.cs` | Full test suite (single file, custom assertion helpers) |
