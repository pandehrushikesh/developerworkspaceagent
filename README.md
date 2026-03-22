# ProjectLens - AI-Powered Developer Workspace Agent

> ProjectLens is a host-agnostic, evidence-driven code analysis agent that retrieves and evaluates code context through bounded tool execution.
ProjectLens is a **.NET 8 AI-assisted code analysis agent** that explores and analyzes a local codebase using:
* 🧠 LLM-guided reasoning
* 🧩 tool-based orchestration
* 💾 persistent session memory
* 🔍 hybrid search (keyword + semantic)
* ❓ prompt clarification before retrieval
* ⚖️ evidence-aware decision making

**ProjectLens lets you ask questions about your codebase and answers them by retrieving and analyzing relevant code.**

> Built with a clean separation between reasoning (LLM), execution (tools), and control (orchestrator).
> The system relies on **retrieved evidence**, not assumptions.

Instead of hardcoded workflows, ProjectLens exposes capabilities through tools and lets the model decide:

- what to inspect
- which files to read
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
  
👉 It goes beyond returning matches by helping navigate related parts of the codebase.

---

## ❓ What Problem Does It Solve?

When working in an unfamiliar repository, developers often ask:

* "What does this repo do?"
* "Where is this feature implemented?"
* "Which files are relevant to this flow?"
* "How does this behavior span across files?"

Answering these usually requires:

* manual search
* opening multiple files
* building mental context

ProjectLens assists by:

* exploring the workspace
* retrieving relevant files
* presenting grounded evidence
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
Filesystem + Search Tools
     |
     v
Evidence Evaluation + Aggregation
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
## 🧠 Evolution of ProjectLens
<details>
<summary><b>Click to see the Evolution of ProjectLens (v0.2 - v0.8)</b></summary>
     
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
- distinguishes:
  - main flow file
  - supporting files
- enables architecture and feature-level understanding

#### 🎯 Feature-intent tracing
- understands prompts like:
  - "Trace how blog creation works"
- biases toward:
  - controllers
  - services
  - models
  - frontend files
- avoids drifting into setup/auth code

#### ⚖️ Confidence-gated reasoning
- distinguishes:
  - provisional feature hypotheses
  - strong evidence-backed conclusions
- prevents:
  - early wrong guesses becoming “truth”

#### 🔗 Follow-up anchoring
- resolves prompts like:
  - "that feature"
  - "that flow"
- keeps context anchored to the correct feature
- avoids drift into unrelated parts (e.g., Program.cs)

---

### v0.7 — Clarifying Question Engine

* detects ambiguous or underspecified prompts
* requests clarification before retrieval
* avoids premature or irrelevant exploration
* improves precision of tool usage

---

### v0.8 — Hybrid Semantic Search

* introduces semantic retrieval alongside keyword search
* uses semantic search selectively:

  * when keyword evidence is weak
  * when queries are conceptual
* maintains keyword-first, bounded retrieval strategy
* improves discovery of relevant code beyond exact matches

---
</details>

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

Trace how blog creation works across the codebase

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
| `ProjectLens.Host` | Console entry point and composition root |
| `ProjectLens.Application` | Orchestration logic and abstractions |
| `ProjectLens.Domain` | Core contracts for agents, tools, and models |
| `ProjectLens.Infrastructure` | Tools and OpenAI integration |
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

- Filesystem tools: `list_files`, `read_file`
- OpenAI model client

#### Host

- App configuration
- Dependency wiring
- Entry point

---

## 🔧 Features

### 🧠 Core Agent Capabilities

* ✅ Model-driven orchestration with bounded execution loop
* ✅ Tool-based architecture (extensible capability model)
* ✅ Follow-up prompt support (multi-step interactions)
* ✅ Prompt clarification before retrieval (ambiguity handling)
* ✅ Deterministic fallback mode (rule-based execution without AI)

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

### 🧠 Evidence-Aware Reasoning

* ✅ Grounded reasoning (observed vs inferred separation)
* ✅ Evidence quality evaluation and ranking
* ✅ Multi-file evidence aggregation (bounded scope)
* ✅ Feature-intent aware exploration (guided file selection)
* ✅ Confidence-aware conclusions (provisional vs strong understanding)

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

---

### ✅ 2. Reduced overlap

Before:

* “reasoning”, “exploration”, “search” mixed together

Now:

* clearly separated into:

  * retrieval
  * reasoning
  * memory
  * control

👉 This reflects your **actual architecture layers**

---

### ✅ 3. More credible language

Removed:

* vague phrases like “understands deeply”

Added:

* “bounded”, “evidence-aware”, “controlled”

👉 This signals **engineering maturity**

---

### ✅ 4. Aligns with your refactor

Now matches what you actually built:

* orchestrator loop ✔️
* hybrid search ✔️
* summarizer memory ✔️
* evidence evaluator ✔️

---
## ❓ Why ProjectLens?

Traditional scripts:
- follow fixed steps
- return raw matches
- leave interpretation to the user

ProjectLens:
- selects relevant evidence
- reasons across files
- adapts to the repository and prompt context

👉 It behaves more like a developer than a script.
---
## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/pandehrushikesh/developerworkspaceagent.git
cd developerworkspaceagent
```

### 2. Configure OpenAI (Optional but Recommended)

Update `ProjectLens.Host/appsettings.json`:

```json
{
  "OpenAI": {
    "ApiKey": "YOUR_API_KEY",
    "Model": "gpt-4.1-mini",
    "BaseUrl": "https://api.openai.com/v1/",
    "MaxIterations": 8
  }
}
```

### 3. Run the application

```bash
dotnet run --project ProjectLens.Host
```

---

## Example Prompts

Try asking:

- "Explain the architecture of this repository"
- "Trace how blog creation works across the codebase"
- "Which files drive that feature?"
- "Now refactor that flow"
  
## 🔍 Example: Intelligent Search

### Prompt:

"Find where AgentOrchestrator is implemented"

### What happens internally:

1. Model decides to call `search_files`
2. Tool scans workspace
3. Returns matching files + snippets
4. Model refines answer using actual code
   
👉 No guessing. Fully grounded in your codebase.

---
## 🛠️ Available Tools

| Tool          | Description |
|---------------|------------|
| list_files    | Lists files and directories in the workspace |
| read_file     | Reads text-based files safely |
| search_files  | Searches codebase for keywords with filtering and snippets |

---

## Model Configuration

Configuration is controlled via `ProjectLens.Host/appsettings.json`.

| Setting | Description |
| --- | --- |
| `OpenAI:ApiKey` | Required for AI mode |
| `OpenAI:Model` | Model name |
| `OpenAI:BaseUrl` | Optional API endpoint override |
| `OpenAI:MaxIterations` | Maximum reasoning loop iterations |

### Fallback Mode

If `ApiKey` or `Model` is not configured, ProjectLens automatically switches to **rule-based analysis mode**.

---

## Design Principles

- AI is not trusted blindly
- All data access happens via tools
- No direct filesystem or system access from the model
- Deterministic and AI-hybrid approach
---

## ⚠️ Current Limitations

- Semantic search is selective (not full indexing)
- Bounded multi-file aggregation (2–3 files)
- Refactor suggestions may be high-level if evidence is partial

---

## 🔮 Future Enhancements

- 🧠 Semantic code understanding
- 🧬 Git history analysis
- 📊 Dependency graphs
- 🌐 Web UI
- ⚡ Iteration efficiency optimization
- 🧠 Smarter convergence control
  
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
> From Stateless Exploration → To Stateful Understanding → To Feature-Aware Reasoning
## Final Thought

ProjectLens is not just a tool — it is a pattern for building intelligent, safe, and extensible AI agents.

Each tool represents a capability boundary.

Adding intelligence means adding new capabilities, not rewriting the orchestrator.

It doesn’t just explore and remember — it reasons with awareness of its own knowledge boundaries.

---

## 📌 Version

**v0.8 — Hybrid Retrieval + Clarification-Aware Agent**
