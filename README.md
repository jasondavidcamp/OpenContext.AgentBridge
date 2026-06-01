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

This repository is starting fresh as the next iteration of the OpenContext orchestrator idea. The first implementation is a thin .NET CLI that establishes the workspace boundary, command execution, skill loading, model provider boundaries, and SQLite conversation storage.

## Getting Started

Prerequisite: .NET 10 SDK.

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

Create and inspect workspace configuration:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- config init .
dotnet run --project src/OpenContext.AgentBridge.Cli -- config show .
```

Test the configured model endpoint:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test .
```

## Current Shape

- `OpenContext.AgentBridge.Cli`: command-line entry point
- `OpenContext.AgentBridge.Core`: workspace, execution, model, and skill abstractions
- `OpenContext.AgentBridge.Providers.Gemini`: Gemini provider adapter boundary
- `OpenContext.AgentBridge.Providers.OpenAiCompatible`: OpenAI-compatible chat completions adapter for STARK-style gateways
- `OpenContext.AgentBridge.SimulatedStark`: local STARK/OpenAI-compatible simulator for personal-side regression runs
- `OpenContext.AgentBridge.Storage`: SQLite conversation persistence
- `docker/`: optional tool container assets
- `scripts/`: repeatable smoke tests for constrained environments
- `skills/`: repo-level starter documentation for skills
- `examples/`: safe sandbox fixtures for public-endpoint dogfooding

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

## Model Provider Configuration

The default provider is Gemini:

```powershell
$env:AGENTBRIDGE_GEMINI_API_KEY = "<key>"
$env:AGENTBRIDGE_GEMINI_MODEL = "gemini-1.5-pro"
$env:AGENTBRIDGE_GEMINI_ENDPOINT = "<optional custom endpoint>"
```

STARK and other OpenAI-compatible gateways use `/v1/chat/completions`:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "stark"
$env:AGENTBRIDGE_STARK_ENDPOINT = "https://stark.example.mil/v1"
$env:AGENTBRIDGE_STARK_MODEL = "<model-id-from-v1-models>"
$env:AGENTBRIDGE_STARK_API_KEY = "<key>"
```

For a one-command STARK smoke test, run:

```powershell
.\scripts\Invoke-StarkSmoke.ps1 -Endpoint "https://stark.example.mil/v1"
```

The script prompts for the API key without echoing it, builds the repo, lists/tests STARK models, runs a no-edit agent inspection, runs a tiny PowerShell edit/validate/diff loop, and then resets the sample file. It sets a longer model timeout for slow gateway responses.

For rapid local iteration without a remote gateway, run the simulator smoke test:

```powershell
.\scripts\Invoke-LocalStarkSmoke.ps1
```

Gemini can also be tested through Google's OpenAI-compatible endpoint:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
$env:AGENTBRIDGE_OPENAI_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_GEMINI_API_KEY
```

Workspace configuration is stored at `.agentbridge/config.json`. Effective configuration is resolved in this order: CLI flags, environment variables, workspace config, defaults.

Ask stores the conversation in `.agentbridge/agentbridge.db`:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- ask . --executor host "Summarize this repository."
```

`ask` now runs a structured action loop. The model must return either a tool request or a final answer as JSON, and AgentBridge validates and executes tool requests inside the selected workspace.
Tool requests are printed as they run, followed by a run summary with tool counts, commands run, and current git changes.
Use `--skill powershell` or `--skills powershell,splunk` to load only specific skills for a run. Without a skill filter, AgentBridge loads all available workspace and repository skills.
Use `--require-tool-calls <n>` when a run must prove that tools were actually used before accepting a final answer.

Start a new conversation:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- ask . --new "Inspect this repo."
```

Continue a specific conversation:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- ask . --conversation <conversation-id> "Continue."
```

List and inspect conversations:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- conversations list .
dotnet run --project src/OpenContext.AgentBridge.Cli -- conversations show . <conversation-id>
```

Starter tools:

- `apply_patch`
- `list_files`
- `read_file`
- `replace_text`
- `search`
- `write_file`
- `run_command`
- `git_status`
- `git_diff`

See [docs/action-protocol.md](docs/action-protocol.md) for the current protocol.
See [docs/configuration.md](docs/configuration.md) for configuration and provider diagnostics.
See [docs/dogfood.md](docs/dogfood.md) for safe public-endpoint dogfooding.
See [docs/gemini-openai.md](docs/gemini-openai.md) for the Gemini OpenAI-compatible rehearsal path.
See [docs/local-simulation.md](docs/local-simulation.md) for the local STARK-compatible simulator workflow.
See [docs/stark.md](docs/stark.md) for the STARK/OpenAI-compatible provider profile.
