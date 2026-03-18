# AGENTS.md

## 🧠 Project Overview

This repository implements **ProjectLens**, an AI-powered developer workspace agent.

The system is designed using **Clean Architecture** principles, separating:

- Reasoning (LLM)
- Capabilities (Tools)
- Orchestration (Agent Loop)

The goal is to build a system that can:
- Explore a codebase
- Reason over it
- Explain it

---

## 🏗️ Architecture Rules (STRICT)

Maintain this dependency flow:
Host → Application → Domain
Host → Infrastructure → Application → Domain


### ❌ DO NOT:
- Reference Infrastructure from Domain
- Add logic to Host layer
- Couple tools directly to LLM logic
- Bypass orchestrator

### ✅ ALWAYS:
- Keep Domain pure (interfaces + models)
- Keep Application as orchestration logic
- Keep Infrastructure as implementation of tools/services

---

## 🧩 Key Components

### 1. Agent Orchestrator (Application)
- Controls the agent loop
- Calls model
- Executes tools
- Feeds results back
- Decides termination

### 2. Tools (Domain + Infrastructure)
Examples:
- list_files
- read_file
- search_files

Rules:
- Tools must be deterministic
- No direct AI logic inside tools
- Tools return structured results

### 3. LLM (Reasoning Layer)
- Decides which tool to call
- Interprets results
- Produces final answer

---

## 🧠 Current Enhancements (In Progress)

We are evolving the system from:

**Stateless Agent → Stateful Workspace Agent**

### Features being added:
- Session memory
- Context compression
- Working summaries

---

## 🧠 Coding Guidelines

### General
- Prefer small, focused classes
- Avoid large monolithic methods
- Use clear naming (Agent, Tool, Session, Summary)

### Async
- Use async/await properly
- Avoid blocking calls

### Models
- Prefer immutable or controlled mutation
- Use clear DTOs for tool outputs

---

## 🔒 Safety Principles

- AI must NOT have direct filesystem access
- All actions must go through tools
- No arbitrary code execution
- All capabilities must be bounded and controlled

---

## 🧪 Testing Expectations

When adding features:
- Add or update unit tests
- Ensure no regression in:
  - agent loop
  - tool execution
  - fallback logic

---

## 🚫 What to Avoid

- Do NOT introduce heavy dependencies unnecessarily
- Do NOT add vector DB or embeddings unless explicitly requested
- Do NOT break existing orchestrator flow
- Do NOT change public contracts unless required

---

## 🎯 Goal of Contributions

Every change should move the system toward:

👉 A developer workspace agent that can:
- remember context
- reason across multiple steps
- assist like a real developer

---

## 🚀 Future Direction (Do NOT implement unless asked)

- Semantic search (embeddings + vector DB)
- Multi-agent coordination
- UI layer (web interface)

---

## 🧠 Final Principle

This is NOT a chatbot.

This is a **system that thinks through tools**.

Design accordingly.
