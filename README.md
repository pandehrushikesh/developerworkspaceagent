# 🚀 ProjectLens – AI-Powered Developer Workspace Agent

ProjectLens is a **.NET 8 AI agent** that analyzes your local codebase using **tool-based orchestration** and **LLM-driven reasoning**.

Instead of hardcoded workflows, ProjectLens exposes capabilities (tools) and lets the model decide:
- what to inspect
- which files to read
- how to answer your query

---

## 🧠 What Problem Does It Solve?

Developers often ask:
- "What does this repo do?"
- "Where is this feature implemented?"
- "Which files are important?"
- "What changed recently?"

ProjectLens answers these questions by:
- exploring your workspace
- reading relevant files
- reasoning over actual data (not guessing)

---

## ⚙️ How It Works

```text
User Prompt
   ↓
Agent Orchestrator
   ↓
Model (LLM)
   ↓
Tool Calls (if needed)
   ↓
Filesystem Tools (list_files, read_file)
   ↓
Back to Model
   ↓
Final Answer

## Project Structure

- `ProjectLens.Host`: console entry point and composition root for running the agent application.
- `ProjectLens.Application`: application-layer coordination and use-case logic.
- `ProjectLens.Domain`: core agent contracts and domain models, kept free of infrastructure concerns.
- `ProjectLens.Infrastructure`: external system implementations and adapters.
- `ProjectLens.Tests`: lightweight automated coverage for tools and orchestration flows.

## Notes

- Dependency flow is `Host -> Application -> Domain` and `Host -> Infrastructure -> Application -> Domain`.
- The `Domain` project contains the core agent request/response models plus tool and orchestrator interfaces.
- `ProjectLens.Application` contains the agent orchestrator plus the `IModelClient` seam used for model-driven tool orchestration.
- `ProjectLens.Infrastructure` contains filesystem tools and the OpenAI `IModelClient` implementation.

## Model Configuration

Set model settings in [ProjectLens.Host/appsettings.json](C:/Users/admin/source/repos/developerworkspaceagent/ProjectLens.Host/appsettings.json):

- `OpenAI:ApiKey`: required to enable model-driven orchestration.
- `OpenAI:Model`: required model name.
- `OpenAI:BaseUrl`: optional override for the API base URL.
- `OpenAI:MaxIterations`: maximum model/tool loop iterations before the agent stops.

If `ApiKey` or `Model` is not configured, ProjectLens automatically falls back to the existing rule-based repository summary flow.
