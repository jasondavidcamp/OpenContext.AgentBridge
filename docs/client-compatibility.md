# Client Compatibility Packet

AgentBridge Server is the client-facing bridge surface. It exposes a local OpenAI-compatible API so different clients can use the same endpoint while AgentBridge handles upstream configuration, local workspace boundaries, and optional bridge-owned tool execution.

## Endpoint Contract

Start the local simulator-backed bridge:

```powershell
.\scripts\Start-LocalSimulatorBridge.ps1
```

Default local API:

```text
http://127.0.0.1:5320/v1
```

Docker-based clients running on the same host usually need:

```text
http://host.docker.internal:5320/v1
```

Core endpoints:

- `GET /v1/models`
- `POST /v1/chat/completions`
- `GET /v1/agentbridge/conversations/{conversation_id}`

Model behavior:

- `agentbridge-agent`: AgentBridge owns the workspace-aware tool loop.
- Any upstream model id: raw proxy mode; the request is forwarded to the configured OpenAI-compatible upstream.

Optional local server auth:

```powershell
$env:AGENTBRIDGE_SERVER_API_KEY = "<local-server-key>"
```

When set, clients send:

```text
Authorization: Bearer <local-server-key>
```

The local server key protects AgentBridge Server. Upstream API keys stay in AgentBridge configuration and do not need to be shared with each client.

## Mode Selection

Use `agentbridge-agent` when the client is mostly a chat surface and AgentBridge should own workspace inspection, edits, command execution, skill loading, conversation persistence, and tool-call audit details.

Use raw proxy mode when the client is already the agent and only needs an OpenAI-compatible endpoint, authentication adapter, or upstream configuration shim.

That boundary keeps AgentBridge thin:

- Open WebUI: usually `agentbridge-agent`
- Aider: usually raw proxy mode
- LangGraph: either mode, depending on whether the graph or AgentBridge owns tools
- scripts and smoke tests: either mode

## Streaming And Metadata

Both modes accept `stream=true`.

Raw proxy mode forwards upstream streaming responses.

`agentbridge-agent` runs the agent loop first, then streams the final answer as OpenAI-compatible server-sent events. The final streaming chunk includes the `agentbridge` metadata object.

Non-streaming `agentbridge-agent` responses include:

```json
{
  "agentbridge": {
    "conversation_id": "<id>",
    "tool_call_count": 2,
    "successful_tool_call_count": 2,
    "failed_tool_call_count": 0
  }
}
```

Fetch tool-call details:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5320/v1/agentbridge/conversations/$conversationId"
```

Continue a persisted AgentBridge conversation by sending:

```text
X-AgentBridge-Conversation: <conversation_id>
```

Clients that already own full chat history can ignore this header.

## Open WebUI

Recommended mode: `agentbridge-agent`.

Use Open WebUI when you want a browser chat surface for the bridge-owned agent loop.

Start the local simulator-backed Open WebUI path:

```powershell
.\scripts\Start-OpenWebUiAgentBridge.ps1 -UseSimulator
```

Compatibility smoke:

```powershell
.\scripts\Invoke-OpenWebUiSmoke.ps1 -UseSimulator
```

Manual settings:

- Base URL: `http://host.docker.internal:5320/v1`
- Model: `agentbridge-agent`
- API key: any placeholder when local server auth is disabled; otherwise use `AGENTBRIDGE_SERVER_API_KEY`
- Streaming: supported

Use the conversation details endpoint when you need to inspect exactly which tools ran after a UI turn.

## Aider

Recommended mode: raw proxy.

Aider already has its own repo map, editing loop, validation loop, and git workflow. Running Aider against `agentbridge-agent` would usually stack one coding agent on top of another. Prefer raw proxy mode so AgentBridge only adapts endpoint and auth details.

Point Aider at AgentBridge Server when the upstream gateway shape or workstation configuration makes direct Aider setup awkward:

```text
OPENAI_API_BASE=http://127.0.0.1:5320/v1
OPENAI_API_KEY=<local-server-key-or-placeholder>
```

Use the real upstream model id, not `agentbridge-agent`, for raw proxy mode.

For Docker-based Aider, use:

```text
OPENAI_API_BASE=http://host.docker.internal:5320/v1
```

See [aider-docker.md](aider-docker.md) for the tested Docker wrapper and image notes.

## LangGraph

Recommended mode: depends on graph ownership.

Use raw proxy mode when LangGraph owns tools, state, retries, and memory.

Use `agentbridge-agent` when LangGraph should treat AgentBridge as one workspace-aware node that can inspect files, edit files, run commands, and return a final summary.

For an AgentBridge-owned node:

- Send chat completions to `http://127.0.0.1:5320/v1/chat/completions`
- Use model `agentbridge-agent`
- Capture `agentbridge.conversation_id`
- Send `X-AgentBridge-Conversation` on later node calls when you want bridge-side continuity
- Fetch `/v1/agentbridge/conversations/{conversation_id}` for audit details

For a LangGraph-owned tool graph:

- Use the upstream model id
- Let AgentBridge raw proxy mode adapt endpoint/auth
- Keep graph state and tool audit in LangGraph

## Plain Scripts

List models:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5320/v1/models"
```

Call agent mode:

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

Call raw proxy mode:

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

## Test Ladder

Use this order to reduce slow handoff loops:

1. Local simulator bridge: `.\scripts\Start-LocalSimulatorBridge.ps1`
2. Local server smoke: `.\scripts\Invoke-LocalServerSmoke.ps1`
3. Local edit canary: `.\scripts\Invoke-LocalServerEditCanary.ps1`
4. Open WebUI simulator smoke: `.\scripts\Invoke-OpenWebUiSmoke.ps1 -UseSimulator`
5. Live Gemini canary: `.\scripts\Invoke-GeminiServerCanary.ps1`
6. Live Gemini agent canary, only when needed: `.\scripts\Invoke-GeminiServerCanary.ps1 -IncludeAgentMode`
7. Restricted-environment packet: [constrained-environment-test-packet.md](constrained-environment-test-packet.md)

## Current Boundaries

AgentBridge should not become the primary UI, a full coding-agent replacement, or a custom workflow engine when a mature client already works.

AgentBridge should provide:

- a local OpenAI-compatible endpoint
- endpoint and auth adaptation
- simulator-backed contract tests
- conservative diagnostics
- bridge-owned fallback tool execution when other clients cannot provide it
- conversation and tool-call audit details for bridge-owned runs
