# AgentBridge Server

`OpenContext.AgentBridge.Server` exposes a local OpenAI-compatible HTTP surface for clients that can call `/v1/models` and `/v1/chat/completions`.

The server has two modes:

- `agentbridge-agent`: runs the AgentBridge workspace-aware agent loop with skills and tools.
- Any other model name: raw proxy mode to the configured OpenAI-compatible upstream endpoint.

This keeps AgentBridge client-agnostic. Open WebUI, LangGraph, Aider, scripts, or other tools can point at the same local endpoint while AgentBridge handles constrained upstream auth and the agent workspace.

## Configuration

Set the workspace that the agent is allowed to use:

```powershell
$env:AGENTBRIDGE_SERVER_WORKSPACE = "C:\path\to\workspace"
```

Configure the upstream OpenAI-compatible endpoint for raw proxy mode and for agent mode when using the generic provider:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
$env:AGENTBRIDGE_GATEWAY_ENDPOINT = "https://gateway.example/v1"
$env:AGENTBRIDGE_GATEWAY_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_GATEWAY_API_KEY = "<key>"
```

For Gemini's public OpenAI-compatible endpoint:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
$env:AGENTBRIDGE_OPENAI_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_GEMINI_API_KEY
```

Optionally require callers to authenticate to the local server:

```powershell
$env:AGENTBRIDGE_SERVER_API_KEY = "<local-server-key>"
```

When `AGENTBRIDGE_SERVER_API_KEY` is set, callers must send:

```text
Authorization: Bearer <local-server-key>
```

## Run

```powershell
dotnet run --project src\OpenContext.AgentBridge.Server --urls http://127.0.0.1:5320
```

For a no-cost local regression that starts the simulator upstream and tests both server modes:

```powershell
.\scripts\Invoke-LocalServerSmoke.ps1
```

For a live Gemini regression through Gemini's OpenAI-compatible endpoint:

```powershell
.\scripts\Invoke-GeminiServerSmoke.ps1
```

The Gemini smoke keeps prompts small, disables AgentBridge traffic logging, and supports `-SkipRawProxy` or `-SkipAgentMode` when you only want to test one path.

List models:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5320/v1/models"
```

Use agent mode:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5320/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Body (@{
    model = "agentbridge-agent"
    messages = @(
      @{ role = "user"; content = "Inspect this workspace and summarize the PowerShell scripts. Do not edit files." }
    )
    stream = $false
  } | ConvertTo-Json -Depth 10)
```

Use raw proxy mode:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5320/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Body (@{
    model = "gemini-2.5-flash"
    messages = @(
      @{ role = "user"; content = "Return exactly: proxy ok" }
    )
    stream = $false
  } | ConvertTo-Json -Depth 10)
```

## Client Notes

- Open WebUI can use `agentbridge-agent` as a chat model for workspace-aware agent runs.
- LangGraph can call the local OpenAI-compatible endpoint as a node.
- Aider can use raw proxy mode when it needs a simple OpenAI-compatible auth/API shim.
- `agentbridge-agent` does not support streaming yet. Use `stream=false`.
- Raw proxy mode buffers the upstream response in this first version.
