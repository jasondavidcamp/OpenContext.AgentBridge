# AgentBridge Server

`OpenContext.AgentBridge.Server` exposes a local OpenAI-compatible HTTP surface for clients that can call `/v1/models` and `/v1/chat/completions`.

The server has two modes:

- `agentbridge-agent`: runs the AgentBridge workspace-aware agent loop with skills and tools.
- Any other model name: raw proxy mode to the configured OpenAI-compatible upstream endpoint.

This keeps AgentBridge client-agnostic. Open WebUI, LangGraph, Aider, scripts, or other tools can point at the same local endpoint while AgentBridge handles constrained upstream auth and the agent workspace.

Agent mode responses include an `agentbridge` metadata object with the conversation id and tool-call counts. Clients can use that conversation id to fetch tool activity from `/v1/agentbridge/conversations/{conversation_id}`.

For bridge-side conversation continuation, send the returned conversation id back on the next agent request:

```text
X-AgentBridge-Conversation: <conversation_id>
```

When this header is present, AgentBridge appends the new request messages to that existing persisted conversation. Clients that already manage their own chat history can ignore the header and keep sending normal OpenAI-compatible requests.

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

To start Open WebUI against AgentBridge Server:

```powershell
.\scripts\Start-OpenWebUiAgentBridge.ps1
```

The script starts AgentBridge Server, runs Open WebUI in Docker, points Open WebUI at `http://host.docker.internal:<port>/v1`, and defaults the visible model to `agentbridge-agent`. In no-auth mode Open WebUI creates a disposable local admin session with:

```text
admin@localhost / admin
```

Use `-Recreate` to delete the previous Open WebUI container and volume before starting, or `-UseExistingProviderConfig` when you already configured AgentBridge provider environment variables for a constrained gateway.

To run an end-to-end Open WebUI compatibility smoke:

```powershell
.\scripts\Invoke-OpenWebUiSmoke.ps1
```

The smoke can start the bridge setup, sign into the disposable local Open WebUI session, send one small streaming `agentbridge-agent` request, validate AgentBridge metadata, and fetch the tool-call details endpoint. Use `-SkipStart` when Open WebUI and AgentBridge Server are already running.

For a no-cost Open WebUI compatibility smoke, use the local simulator upstream:

```powershell
.\scripts\Invoke-OpenWebUiSmoke.ps1 -UseSimulator
```

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

Fetch the AgentBridge activity details for an agent response:

```powershell
$response = Invoke-WebRequest `
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

$conversationId = $response.Headers["X-AgentBridge-Conversation"]
Invoke-RestMethod -Uri "http://127.0.0.1:5320/v1/agentbridge/conversations/$conversationId"
```

Continue that persisted AgentBridge conversation:

```powershell
Invoke-RestMethod `
  -Uri "http://127.0.0.1:5320/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Headers @{ "X-AgentBridge-Conversation" = $conversationId } `
  -Body (@{
    model = "agentbridge-agent"
    messages = @(
      @{ role = "user"; content = "Continue from the previous bridge conversation and summarize the next thing to inspect." }
    )
    stream = $false
  } | ConvertTo-Json -Depth 10)
```

Use agent mode with streaming:

```powershell
Invoke-WebRequest `
  -Uri "http://127.0.0.1:5320/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Body (@{
    model = "agentbridge-agent"
    messages = @(
      @{ role = "user"; content = "Inspect this workspace and summarize the PowerShell scripts. Do not edit files." }
    )
    stream = $true
  } | ConvertTo-Json -Depth 10) |
  Select-Object -ExpandProperty Content
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

See [client-compatibility.md](client-compatibility.md) for the fuller client compatibility packet.

- Open WebUI can use `agentbridge-agent` as a chat model for workspace-aware agent runs.
- LangGraph can call the local OpenAI-compatible endpoint as a node.
- Aider can use raw proxy mode when it needs a simple OpenAI-compatible auth/API shim.
- `agentbridge-agent` supports OpenAI-style server-sent event streaming. It currently streams the final answer in chunks after the agent run completes.
- Raw proxy mode forwards upstream streaming responses when clients send `stream=true`.
- Agent mode adds AgentBridge metadata to non-streaming responses and the final streaming chunk. Use `/v1/agentbridge/conversations/{conversation_id}` to inspect tool calls without changing the chat text.
- Wrappers can continue bridge-side agent history by sending `X-AgentBridge-Conversation` on later `agentbridge-agent` requests. Raw proxy mode does not use this header.
