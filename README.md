# OpenContext.AgentBridge

Thin bridge for agentic developer workflows on constrained OpenAI-compatible chat APIs.

## Purpose

OpenContext.AgentBridge is intended to bridge the gap between limited chat-only AI APIs and the richer agentic development workflows teams need in real project work. It is a control plane, simulator, diagnostics harness, and adapter layer rather than a full agent platform.

The initial goal is to help existing and future agent tools work against constrained gateways by providing:

- OpenAI-compatible provider configuration for constrained gateways
- Local simulation for fast personal-side testing
- Repeatable diagnostics bundles for slower restricted-environment testing
- Safe workspace boundaries for fallback tool execution
- Lightweight workspace context for code-aware prompts
- Skills and MCP adapter hooks where existing integrations can be reused

## Status

This repository is starting fresh as the next iteration of the OpenContext orchestrator idea. The first implementation is a thin .NET CLI that establishes the workspace boundary, command execution, skill loading, model provider boundaries, local simulation, diagnostics, and SQLite conversation storage.

## Project Boundary

AgentBridge should stay a bridge. It should use tools such as Aider, Continue, Cline, LiteLLM, Open WebUI, MCP servers, CLIs, and SDKs when they fit the constrained API and workstation environment.

AgentBridge should build only the pieces that are missing because the target API or environment is constrained:

- OpenAI-compatible gateway API adapters and configuration
- Local simulator and contract tests
- Diagnostics and smoke-test packaging
- Workspace policy and execution boundaries
- Lightweight workspace maps and skill routing
- Minimal fallback tool loop when an existing agent cannot run cleanly

AgentBridge should not become a custom IDE, chat UI, full MCP platform, advanced repo indexer, or replacement for mature coding agents.

## Getting Started

Prerequisite: .NET 10 SDK.

Build and test:

```powershell
dotnet build OpenContext.AgentBridge.sln
dotnet test OpenContext.AgentBridge.sln
```

Start a no-key local simulator bridge:

```powershell
.\scripts\Start-LocalSimulatorBridge.ps1
```

This starts the local simulator and AgentBridge Server, then prints the local OpenAI-compatible URL and a copyable chat request. It is the fastest fresh-clone path because it does not need Docker or an API key.

Stop it when finished:

```powershell
.\scripts\Stop-LocalSimulatorBridge.ps1
```

Run the cheap pre-push regression set:

```powershell
.\scripts\Invoke-CheapRegression.ps1
```

Use `-SkipOpenWebUi` when Docker is unavailable. The default path uses the local simulator for the Open WebUI smoke, so it does not spend live model calls.
Use `-SkipEditCanary` only when the machine cannot run the PowerShell fixture validation.

Run a focused local server edit canary:

```powershell
.\scripts\Invoke-LocalServerEditCanary.ps1
```

For slower workstation handoff testing, use the [constrained environment test packet](docs/constrained-environment-test-packet.md).

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

Inspect the compact workspace map used to orient the agent:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- workspace inspect .
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
- `OpenContext.AgentBridge.Providers.OpenAiCompatible`: OpenAI-compatible chat completions adapter for constrained gateways
- `OpenContext.AgentBridge.Server`: local OpenAI-compatible server for client-agnostic bridge testing
- `OpenContext.AgentBridge.SimulatedGateway`: local OpenAI-compatible gateway simulator for personal-side regression runs
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

Constrained OpenAI-compatible gateways use `/v1/chat/completions`:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
$env:AGENTBRIDGE_GATEWAY_ENDPOINT = "https://gateway.example/v1"
$env:AGENTBRIDGE_GATEWAY_MODEL = "<model-id-from-v1-models>"
$env:AGENTBRIDGE_GATEWAY_API_KEY = "<key>"
```

For a one-command Gateway smoke test, run:

```powershell
.\scripts\Invoke-GatewaySmoke.ps1 -Endpoint "https://gateway.example/v1"
```

The script prompts for the API key without echoing it, builds the repo, lists/tests gateway models, runs a no-edit agent inspection, runs a tiny PowerShell edit/validate/diff loop, runs a symbol-aware C# edit/validate/diff loop, and then resets the sample files. It sets a longer model timeout for slow gateway responses.

For rapid local iteration without a remote gateway, run the simulator smoke test:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1
```

To collect a diagnostics bundle from any smoke run:

```powershell
.\scripts\Invoke-GatewaySmoke.ps1 -Endpoint "https://gateway.example/v1" -OutputDirectory ".agentbridge\diagnostics\gateway-smoke" -ZipDiagnostics
```

Gemini can also be tested through Google's OpenAI-compatible endpoint:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
$env:AGENTBRIDGE_OPENAI_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_GEMINI_API_KEY
```

For one tiny live server canary through that endpoint:

```powershell
.\scripts\Invoke-GeminiServerCanary.ps1
```

Workspace configuration is stored at `.agentbridge/config.json`. Effective configuration is resolved in this order: CLI flags, environment variables, workspace config, defaults.

Ask stores the conversation in `.agentbridge/agentbridge.db`:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- ask . --executor host "Summarize this repository."
```

`ask` runs a structured fallback action loop for constrained APIs. The model must return either a tool request or a final answer as JSON, and AgentBridge validates and executes tool requests inside the selected workspace.
Each model turn includes a compact workspace map with detected solutions, projects, scripts, skills, docs, likely entry points, C#/PowerShell symbols, and git status so the model has repository orientation before it starts calling tools.
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
See [docs/aider-docker.md](docs/aider-docker.md) for the Aider-first Docker bridge spike.
See [docs/gemini-openai.md](docs/gemini-openai.md) for the Gemini OpenAI-compatible rehearsal path.
See [docs/server.md](docs/server.md) for the local OpenAI-compatible AgentBridge server.
See [docs/local-simulation.md](docs/local-simulation.md) for the local OpenAI-compatible gateway simulator workflow.
See [docs/gateway.md](docs/gateway.md) for the OpenAI-compatible gateway provider profile.
