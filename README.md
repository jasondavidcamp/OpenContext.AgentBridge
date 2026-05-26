# OpenContext.AgentBridge

Codex-like workspace agents for constrained AI environments, with code editing, shell access, skills, and persistent project context.

## Purpose

OpenContext.AgentBridge is intended to bridge the gap between limited chat-only AI APIs and the richer agentic development workflows teams need in real project work.

The initial goal is to provide a workspace-scoped assistant that can:

- Understand project files and repository context
- Modify code directly within an approved workspace
- Run shell commands and development tooling
- Load skills for systems such as Azure DevOps Server, Splunk, Oracle, and other internal services
- Persist project-aware chats and working context over time

## Status

This repository is starting fresh as the next iteration of the OpenContext orchestrator idea. The first implementation is a thin .NET CLI that establishes the workspace boundary, command execution, skill loading, Gemini provider boundary, and SQLite conversation storage.

## Getting Started

Build and test:

```powershell
dotnet build OpenContext.AgentBridge.sln
dotnet test OpenContext.AgentBridge.sln
```

Initialize a workspace:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- init .
```

Run a command in the workspace:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- run . -- git status --short
```

Check the workspace:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- doctor .
```

## Current Shape

- `OpenContext.AgentBridge.Cli`: command-line entry point
- `OpenContext.AgentBridge.Core`: workspace, execution, model, and skill abstractions
- `OpenContext.AgentBridge.Providers.Gemini`: Gemini provider adapter boundary
- `OpenContext.AgentBridge.Storage`: SQLite conversation persistence
- `docker/`: optional tool container assets
- `skills/`: repo-level starter documentation for skills

## Docker Tool Executor

AgentBridge can run commands on the host first, then move selected tool execution into Docker when a locked-down machine needs a bundled toolchain.

Build the starter tool image:

```powershell
docker build -f docker/agentbridge-tools.Dockerfile -t opencontext-agentbridge-tools:latest .
```

Run a command through Docker:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- run . --executor docker -- dotnet --info
```

## Gemini Configuration

The Gemini provider currently uses environment variables so it can adapt to a public or internal Gemini-compatible endpoint:

```powershell
$env:AGENTBRIDGE_GEMINI_API_KEY = "<key>"
$env:AGENTBRIDGE_GEMINI_MODEL = "gemini-1.5-pro"
$env:AGENTBRIDGE_GEMINI_ENDPOINT = "<optional custom endpoint>"
```

Ask stores the conversation in `.agentbridge/agentbridge.db`:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- ask . "Summarize this repository."
```
