# ProjectLens - AI-Powered Developer Workspace Agent

ProjectLens is a **.NET 8 AI agent** that analyzes your local codebase using **tool-based orchestration** and **LLM-driven reasoning**.

**ProjectLens lets you ask questions about your codebase—and answers them by actually reading your code.**

> Built with a clean separation between reasoning (LLM), execution (tools), and control (orchestrator).

Instead of hardcoded workflows, ProjectLens exposes capabilities through tools and lets the model decide:

- what to inspect
- which files to read
- how to answer your query

---

## What Problem Does It Solve?

Developers often ask:

- "What does this repo do?"
- "Where is this feature implemented?"
- "Which files are important?"
- "What changed recently?"

ProjectLens answers these questions by:

- exploring your workspace
- reading relevant files
- reasoning over actual data instead of guessing

---

## How It Works

```text
User Prompt
   |
   v
Agent Orchestrator
   |
   v
Model (LLM)
   |
   v
Tool Calls (if needed)
   |
   v
Filesystem Tools (list_files, read_file)
   |
   v
Back to Model
   |
   v
Final Answer

```
## 🧪 Example

**Prompt:**
Summarize this project

**Output:**
- .NET 8 application using clean architecture

- Implements tool-based AI agent orchestration

- Uses filesystem tools (list_files, read_file)

- Supports OpenAI-based reasoning with fallback mode

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

✅ Model-driven orchestration loop  
✅ Tool-based architecture (extensible)  
✅ Safe filesystem access (workspace-bound)  
✅ Clean architecture separation  
✅ Rule-based fallback (no AI required)  
✅ Testable components  

### 🧠 Intelligent Code Exploration

✅ `list_files` – Discover workspace structure  
✅ `read_file` – Read file contents safely  
✅ `search_files` – 🔍 Search across codebase with:

- keyword search across files
- recursive directory traversal
- file pattern filtering (e.g., *.cs, *.json)
- case-sensitive / insensitive search
- snippet extraction for context
- binary file skipping
- result limiting for performance

---

## ❓ Why ProjectLens?

Traditional scripts:
- follow fixed steps

ProjectLens:
- dynamically decides what to inspect
- reasons over real data
- adapts to different repositories
- 👉 It behaves more like a developer than a script.
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

- "Summarize this project"
- "Explain the architecture"
- "Which files are important?"
- "What does this repository do?"
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
## 🧪 Real Example (From Live Execution) with using OpenAI API key

### 💬 Prompt:
"Search for unzip, extract, or archive logic and explain which file contains the main extraction flow."
### 🤖 Agent Behavior
- Iteration 1 → search_files  
- Iteration 2 → refine search_files  
- Iteration 3 → read_file  
- Iteration 4 → final answer

### ✅ Final Answer
The file "Program.cs" contains the main extraction flow for unzip and archive logic.
It correctly identified:
- archive discovery logic
- disk space validation
- extraction workflow
- SharpCompress usage
- helper utilities

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

## 🔮 Future Enhancements

🧬 Git history analysis (commit insights)  
📊 Code dependency mapping  
🧠 Semantic code understanding  
🌐 Web UI  
⚡ Performance optimization for large repositories  

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

## Final Thought

ProjectLens is not just a tool - it is a pattern for building intelligent, safe, and extensible AI agents.

Each tool represents a capability boundary.

Adding intelligence = adding new tools.

No change required in orchestrator.
