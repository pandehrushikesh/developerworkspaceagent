# ProjectLens - AI-Powered Developer Workspace Agent

> ProjectLens is a host-agnostic, evidence-driven, convergence-controlled code analysis agent with explicit structured evidence extraction and bounded execution that retrieves and evaluates code context through controlled tool execution.

ProjectLens is a **.NET 8 AI-assisted code intelligence agent** that explores, analyzes, and modifies a local codebase using:

* 🧠 LLM-guided reasoning
* 🧩 Tool-based orchestration
* 💾 Persistent session memory
* 🔍 Hybrid search (keyword + semantic)
* ❓ Prompt clarification before retrieval
* ⚖️ Evidence-aware decision making
* ✏️ Code editing and file writing

**ProjectLens lets you ask questions about your codebase — and now modify it — through a chat UI backed by a REST API.**

> Built with a clean separation between reasoning (LLM), execution (tools), and control (orchestrator).
> The system relies on **retrieved evidence**, not assumptions.

Instead of hardcoded workflows, ProjectLens exposes capabilities through tools and lets the model decide:

- what to inspect
- which files to read
- what changes to make
- how to answer your query

---

## 🧠 How It Differs from Traditional Tools

Traditional tools:
- search files
- return matches

ProjectLens:
- decides what to inspect
- selects relevant files
- combines evidence across files
- tracks uncertainty
- refines answers across steps
- **edits code based on your intent**

👉 It goes beyond returning matches — it helps you navigate and modify your codebase.

---

## ❓ What Problem Does It Solve?

When working in an unfamiliar repository, developers often ask:

* "What does this repo do?"
* "Where is this feature implemented?"
* "Which files are relevant to this flow?"
* "How does this behavior span across files?"
* "Can you add a new endpoint for this resource?"

Answering these usually requires:

* manual search
* opening multiple files
* building mental context
* making careful edits

ProjectLens assists by:

* exploring the workspace
* retrieving relevant files
* presenting grounded evidence
* making targeted code changes
* maintaining session context across steps

---

## How It Works

```text
User Prompt
     |
     v
Prompt Clarifier (optional)
     |
     v
Agent Orchestrator
     |
     v
Session Memory
     |
     v
Model (LLM)
     |
     v
Tool Calls (if needed)
     |
     v
Filesystem + Search + Edit Tools
     |
     v
Evidence Extraction + Evaluation + Convergence + Controlled Execution
     |
     v
Compressed Context
     |
     v
Back to Model
     |
     v
Final Answer
```

> The system iteratively retrieves and evaluates evidence before producing a response.

---

## 🧾 Structured Evidence Model

ProjectLens treats evidence as a **first-class concept**, not just text.

Each tool execution produces:

- 📄 Prompt-facing text (for LLM reasoning)
- 📊 Structured evidence items (for internal evaluation)

### EvidenceItem

Each piece of evidence includes:

- **ToolName** – which tool produced it
- **SourceId** – file path or logical source
- **Content** – snippet or summary
- **Kind** – type of evidence:
  - `SearchHit`
  - `DirectSnippet`
  - `FileSummary`
  - `ToolObservation`
- **IsPartial** – whether the evidence is incomplete
- **Confidence** – deterministic confidence signal (not model-generated)

### Why this matters

This enables:

- evidence sufficiency scoring
- coverage-aware reasoning
- convergence decisions
- reduced redundant retrieval

👉 The system knows *what it knows*, not just *what it says*.

---

## 🎯 Convergence-Controlled Reasoning

ProjectLens includes an explicit **convergence control layer** that guides how the agent progresses through a problem.

Instead of blindly continuing tool execution, the system evaluates:

- how sufficient the evidence is
- how broad the coverage is
- whether progress is being made
- whether further retrieval is meaningful
- explicitly tracks and communicates uncertainty when evidence is incomplete

### Convergence Decisions

Each iteration produces a deterministic decision:

- `ContinueWithBroaderSearch` – explore different areas of the codebase
- `ContinueWithDeeperRead` – inspect relevant files more deeply
- `FinalizePartialAnswer` – stop and provide a bounded answer with limitations
- `FinalizeConfidentAnswer` – stop and provide a fully grounded answer

### Why this matters

This prevents:

- 🔁 Infinite or redundant loops
- 📂 Excessive file reads ("over-fetching")
- 🧠 Premature or overconfident answers

👉 The agent knows **when to continue, when to change strategy, and when to stop**.

---

### 🧭 Guided Execution Loop

During each iteration, ProjectLens:

1. Collects structured evidence
2. Evaluates evidence sufficiency and coverage
3. Decides the next action via convergence policy
4. Injects guidance into the model
5. Enforces bounded execution (fetch budgets + duplicate prevention)

👉 This creates a **closed-loop reasoning system**, not just a tool-calling agent.

---

## 🧠 Evolution of ProjectLens

<details>
<summary><b>Click to see the Evolution of ProjectLens (v0.2 – v0.9)</b></summary>

### v0.2 — Stateful Agent

- remembers visited files
- retains working summary
- supports follow-up prompts

---

### v0.3 — Grounded Reasoning

- separates:
  - ✅ observed facts
  - 💡 inferred recommendations
- reduces hallucination
- improves trust

---

### v0.4 — Persistent Memory

- session memory stored on disk
- survives process restarts
- enables long-running analysis

---

### 🚀 v0.5 — Evidence Quality Engine

- filters low-value files (bin/, obj/, etc.)
- prioritizes meaningful source files
- improves signal-to-noise ratio
- prevents noisy artifacts from polluting reasoning

---

### v0.6 — Multi-File Reasoning + Feature Awareness

#### 🔍 Multi-file evidence aggregation
- combines 2–3 relevant files
- distinguishes main flow file from supporting files
- enables architecture and feature-level understanding

#### 🎯 Feature-intent tracing
- understands prompts like "Trace how blog creation works"
- biases toward controllers, services, models, frontend files
- avoids drifting into setup/auth code

#### ⚖️ Confidence-gated reasoning
- distinguishes provisional feature hypotheses from strong evidence-backed conclusions
- prevents early wrong guesses becoming "truth"

#### 🔗 Follow-up anchoring
- resolves prompts like "that feature" or "that flow"
- keeps context anchored to the correct feature
- avoids drift into unrelated parts

---

### v0.7 — Clarifying Question Engine

- detects ambiguous or underspecified prompts
- requests clarification before retrieval
- avoids premature or irrelevant exploration
- improves precision of tool usage

---

### v0.8 — Hybrid Semantic Search

- introduces semantic retrieval alongside keyword search
- uses semantic search selectively when keyword evidence is weak or queries are conceptual
- maintains keyword-first, bounded retrieval strategy
- improves discovery of relevant code beyond exact matches

---

### 🚀 v0.9 — Convergence-Controlled Reasoning

#### 🧠 Evidence evaluation + convergence decisions
- introduces explicit evidence assessment: sufficiency, coverage, confidence
- enables deterministic convergence decisions

#### 🎯 Convergence-guided execution
- injects convergence guidance into each iteration
- steers model behavior without hardcoding tool usage
- reduces redundant or misdirected tool calls

#### ⚙️ Controlled execution loop
- aligns convergence decisions with orchestrator behavior
- allows partial finalization when progress stalls

#### 🔒 Bounded tool execution
- per-iteration fetch budgets (limits excessive search/read calls)
- uses synthetic tool results to guide the model when limits are hit

#### ⚖️ Stability and trust improvements
- avoids infinite or redundant loops
- prevents overconfident or premature answers
- explicitly communicates uncertainty when evidence is incomplete

👉 ProjectLens evolves from an evidence-aware agent into a **convergence-controlled reasoning system**.

---

</details>

### 🚀 v1.0 — Multi-Provider API + Chat UI + Code Editing

#### 🌐 REST API (ASP.NET Core)
- new `ProjectLens.Api` project exposes a clean HTTP interface
- `POST /api/query` — submit a prompt and workspace path, receive a structured response
- `GET /api/health` — liveness check
- configurable CORS origins (defaults to `http://localhost:3000`)
- configurable workspace root restrictions for deployment security

#### 💬 React Chat UI
- new `projectlens-ui` project — Vite + React 18 + TypeScript, zero external UI libraries
- dark-themed chat interface with streaming-style responses
- sidebar for workspace path configuration and session management
- real-time API health indicator
- Vite dev server proxy (`/api` → API backend)

#### ✏️ Code Editing Tools
- `write_file` — create or overwrite files; auto-creates parent directories; refuses binary files
- `edit_file` — exact-string find-and-replace; fails on ambiguous or missing matches

#### 🤖 Multi-Provider Model Support
- new `Model` settings block supports any provider via `Provider` field
- built-in providers: `OpenAI`, `Anthropic`, `None`
- `Anthropic` provider: native Messages API with proper alternating turn construction
- `OpenAI` provider: Responses API with `previous_response_id` stateful chaining
- fallback to rule-based mode when no provider is configured

👉 ProjectLens evolves from a convergence-controlled reasoning engine into a **full-stack AI coding assistant**.

> From Stateless Exploration → Stateful Understanding → Feature-Aware Reasoning → Convergence-Controlled Intelligence → **Multi-Provider API + Chat UI**

---

## 🧩 Context Compression

ProjectLens compresses file content into:
- file previews
- key symbols (classes, methods)
- relevant snippets

This ensures:
- efficient token usage
- faster reasoning
- better grounding

---

## Example

### 🧪 Real Example

**Prompt**

> Trace how blog creation works across the codebase

**Result**

ProjectLens:

- identifies `BlogsController.cs` as entry point
- finds `CreateBlogRequest` in models
- connects controller → model → DbContext flow
- avoids unrelated setup/auth files like `Program.cs`

👉 Multi-file reasoning. Fully grounded.

---

## Project Structure

| Project | Responsibility |
| --- | --- |
| `ProjectLens.Api` | ASP.NET Core Web API — HTTP entry point and composition root |
| `ProjectLens.Host` | Console entry point (alternative host) |
| `projectlens-ui` | React 18 + TypeScript chat UI (Vite) |
| `ProjectLens.Application` | Orchestration logic and abstractions |
| `ProjectLens.Domain` | Core contracts for agents, tools, and models |
| `ProjectLens.Infrastructure` | Tools, model clients (OpenAI, Anthropic), and settings |
| `ProjectLens.Tests` | Test coverage |

### Key Responsibilities

#### Domain
- Agent request/response models
- `ITool`
- `IAgentOrchestrator`

#### Application
- `AgentOrchestrator`
- `IModelClient` abstraction for AI integration

#### Infrastructure
- Filesystem tools: `list_files`, `read_file`, `write_file`, `edit_file`, `search_files`
- OpenAI model client (Responses API)
- Anthropic model client (Messages API)
- Shared settings (`ProjectLensSettings`)

#### Api
- Minimal API with `/api/query` and `/api/health`
- CORS and workspace path security

#### Host
- Console-based alternative entry point

---

## 🔧 Features

### 🧠 Core Agent Capabilities

* ✅ Model-driven orchestration with bounded execution loop
* ✅ Tool-based architecture (extensible capability model)
* ✅ Follow-up prompt support (multi-step interactions)
* ✅ Prompt clarification before retrieval (ambiguity handling)
* ✅ Deterministic fallback mode (rule-based execution without AI)
* ✅ Multi-provider model support (OpenAI, Anthropic, extensible)

---

### 🔍 Retrieval & Code Exploration

* ✅ Hybrid search:
  * keyword-first retrieval
  * semantic search fallback (for weak or conceptual queries)
* ✅ Workspace-scoped exploration (safe and bounded)
* ✅ Snippet-based evidence extraction
* ✅ File pattern filtering and recursive traversal
* ✅ Binary file detection and skipping
* ✅ Result limiting for controlled execution

---

### ✏️ Code Editing

* ✅ Create or overwrite files (`write_file`)
* ✅ Exact-string find-and-replace (`edit_file`)
* ✅ Auto-creates parent directories
* ✅ Ambiguity detection (fails if match is non-unique)
* ✅ Binary file protection (refuses to overwrite non-text files)

---

### 🧠 Evidence-Aware Reasoning

* ✅ Grounded reasoning (observed vs inferred separation)
* ✅ Evidence quality evaluation and ranking
* ✅ Multi-file evidence aggregation (bounded scope)
* ✅ Feature-intent aware exploration (guided file selection)
* ✅ Confidence-aware conclusions (provisional vs strong understanding)
* ✅ Structured evidence extraction (EvidenceItem model)
* ✅ Tool outputs produce both prompt text and machine-readable evidence

---

### 💾 Memory & Context Management

* ✅ Session memory (stateful interactions)
* ✅ Persistent memory across runs
* ✅ Context compression for efficient token usage
* ✅ Bounded summary construction (controlled memory growth)

---

### ⚙️ Reliability & Control

* ✅ Bounded execution (iteration limits, result limits)
* ✅ Duplicate tool-call prevention
* ✅ Weak-evidence detection and recovery strategies
* ✅ Safe filesystem access (workspace-bound only)
* ✅ Clean architecture separation (host-agnostic design)
* ✅ Convergence-aware execution (evidence-driven stopping decisions)
* ✅ Per-iteration fetch budgets (prevents over-fetching)
* ✅ Partial-answer finalization under no-progress conditions
* ✅ Convergence-guided model steering (reduces redundant tool calls)

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/pandehrushikesh/developerworkspaceagent.git
cd developerworkspaceagent
```

### 2. Configure a model provider

Update `ProjectLens.Api/appsettings.json`:

**OpenAI**
```json
{
  "Model": {
    "Provider": "OpenAI",
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "Model": "gpt-4.1-mini",
    "BaseUrl": "https://api.openai.com/v1/",
    "MaxIterations": 10
  }
}
```

**Anthropic**
```json
{
  "Model": {
    "Provider": "Anthropic",
    "ApiKey": "YOUR_ANTHROPIC_API_KEY",
    "Model": "claude-sonnet-4-6",
    "BaseUrl": "",
    "MaxIterations": 10
  }
}
```

> If no provider is configured, ProjectLens automatically switches to **rule-based analysis mode**.

### 3. Start the API

```bash
dotnet run --project ProjectLens.Api
# Listening on http://localhost:5000
```

### 4. Start the UI

```bash
cd projectlens-ui
npm install
npm run dev
# Open http://localhost:3000
```

### 5. Query via the UI

1. Open `http://localhost:3000`
2. Enter your workspace path in the sidebar (e.g. `C:\path\to\your\project`)
3. Click **Set Workspace**
4. Ask anything about the codebase

### Alternative: query via the API directly

```bash
curl -X POST http://localhost:5000/api/query \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Give me an overview of this codebase", "workspacePath": "C:\\path\\to\\project"}'
```

---

## Example Prompts

Try asking:

- "Explain the architecture of this repository"
- "Trace how blog creation works across the codebase"
- "Which files drive that feature?"
- "Add a GET /api/tags endpoint that returns all unique tags"
- "Refactor BlogService to use an interface"

---

## 🛠️ Available Tools

| Tool | Description |
|------|-------------|
| `list_files` | Lists files and directories in the workspace |
| `read_file` | Reads text-based files safely |
| `write_file` | Creates or overwrites a file; auto-creates parent directories |
| `edit_file` | Exact-string find-and-replace within a file |
| `search_files` | Searches codebase for keywords with filtering and snippets |

---

## Model Configuration

Configuration is controlled via `ProjectLens.Api/appsettings.json` (API) or `ProjectLens.Host/appsettings.json` (console).

### `Model` block (primary — supports any provider)

| Setting | Description |
| --- | --- |
| `Model:Provider` | `OpenAI`, `Anthropic`, or `None` |
| `Model:ApiKey` | API key for the selected provider |
| `Model:Model` | Model name (e.g. `gpt-4.1-mini`, `claude-sonnet-4-6`) |
| `Model:BaseUrl` | Optional API endpoint override |
| `Model:MaxIterations` | Maximum reasoning loop iterations |

### `OpenAI` block (legacy — OpenAI only)

| Setting | Description |
| --- | --- |
| `OpenAI:ApiKey` | OpenAI API key |
| `OpenAI:Model` | Model name |
| `OpenAI:BaseUrl` | Optional endpoint override |
| `OpenAI:MaxIterations` | Maximum iterations |

> The `Model` block takes precedence. If `Model:Provider` is set to `None` or left empty, the `OpenAI` block is used as a fallback.

### Security settings (API only)

| Setting | Description |
| --- | --- |
| `AllowedOrigins` | CORS origins allowed to call the API (default: `["http://localhost:3000"]`) |
| `AllowedWorkspaceRoots` | If non-empty, workspace paths must be within one of these roots |

---

## Design Principles

- AI is not trusted blindly
- All data access happens via tools
- No direct filesystem or system access from the model
- Deterministic and AI-hybrid approach
- Evidence-first: the agent reasons on what it retrieved, not what it assumes

---

## ⚠️ Current Limitations

- Semantic search is selective (not full indexing)
- Bounded multi-file aggregation (2–3 files)
- Refactor suggestions may be high-level if evidence is partial
- No streaming responses (full answer returned at once)

---

## 🔮 Future Enhancements

- 🧠 Semantic code understanding
- 🧬 Git history analysis
- 📊 Dependency graphs
- ⚡ Streaming response support
- 🤝 Interactive exploration (user-guided follow-up actions)
- 🧠 Adaptive convergence tuning based on query type
- 📊 Evidence visualization and traceability
- 🔌 MCP server mode

---

## Core Idea

> Tools define capability.
> AI provides reasoning.
> Orchestrator controls execution.

---

## Author

**Hrushikesh Pande**
Senior Consultant | AI Explorer

---

## Support

If you find this useful:

- Star the repo
- Fork it
- Share feedback

---

> From Stateless Exploration → Stateful Understanding → Feature-Aware Reasoning → Convergence-Controlled Intelligence → Multi-Provider API + Chat UI

## Final Thought

ProjectLens is not just a tool — it is a pattern for building intelligent, safe, and extensible AI agents.

Each tool represents a capability boundary.

Adding intelligence means adding new capabilities, not rewriting the orchestrator.

It doesn't just explore and remember — it reasons with awareness of its own observed evidence boundaries, controls its execution based on convergence, and can now act on your codebase directly.

---

## 📌 Version

**v1.0 — Multi-Provider API + Chat UI + Code Editing**
