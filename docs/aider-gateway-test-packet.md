# Aider Gateway Test Packet

Use this packet to test whether Aider can perform agentic coding against a constrained OpenAI-compatible gateway.

## Goal

Prove this chain:

```text
Aider in Docker -> AgentBridge raw proxy -> OpenAI-compatible gateway -> model
```

In this mode, Aider owns the coding loop. AgentBridge only provides the local OpenAI-compatible proxy and upstream configuration. This is intentionally different from `agentbridge-agent`.

## Prerequisites

- .NET 10 SDK
- Git
- Docker Desktop
- Gateway endpoint ending in `/v1`
- Gateway model id
- Gateway API key
- Aider Docker image with the tools needed for validation

The default canary image is:

```text
opencontext-agentbridge-aider-dotnet:latest
```

If it does not exist locally, build it:

```powershell
docker build -f docker\aider-dotnet.Dockerfile -t opencontext-agentbridge-aider-dotnet:latest .
```

If the workstation requires an approved image, pass it with `-Image`.

## 1. Pull Latest

Run each command separately:

```powershell
cd C:\Git\public\OpenContext.AgentBridge
```

```powershell
git pull
```

```powershell
git rev-parse --short HEAD
```

## 2. Confirm Docker And Image

```powershell
docker version
```

```powershell
docker images --format "{{.Repository}}:{{.Tag}}" | Select-String -Pattern "aider"
```

If `opencontext-agentbridge-aider-dotnet:latest` is missing and the base image is available:

```powershell
docker build -f docker\aider-dotnet.Dockerfile -t opencontext-agentbridge-aider-dotnet:latest .
```

## 3. Configure Gateway Values

Use the real endpoint and model id. Do not paste the API key into notes, chat, or issue text.

```powershell
$endpoint = "https://gateway.example/v1"
```

```powershell
$model = "<model-id>"
```

```powershell
$apiKey = Read-Host "Gateway API key" -AsSecureString
```

```powershell
$plainKey = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($apiKey))
```

## 4. Run Aider Gateway Canary

```powershell
.\scripts\Invoke-AiderGatewayCanary.ps1 -Endpoint $endpoint -Model $model -ApiKey $plainKey
```

If using a different image:

```powershell
.\scripts\Invoke-AiderGatewayCanary.ps1 -Endpoint $endpoint -Model $model -ApiKey $plainKey -Image "<approved-aider-image>"
```

Expected:

- AgentBridge Server starts as a raw proxy.
- Aider runs inside Docker.
- Aider edits only `examples/sandbox-project/SandboxApp/Program.cs` in a scratch workspace.
- Aider runs the validation command inside Docker.
- Validation returns `Hello, AgentBridge from AgentBridge!`.
- Final result JSON includes `"status": "passed"`.

The script creates and removes a scratch workspace under `.agentbridge\aider-canary-workspaces` unless `-KeepWorkspace` is set.

## Feedback To Send Back

Send:

- Commit tested
- Whether Docker worked
- Aider image used
- Gateway model id used
- Whether `Invoke-AiderGatewayCanary.ps1` ended with `"status": "passed"`
- The final result JSON, redacted if needed
- Any error text, redacted for secrets

Do not send API keys, bearer tokens, cookies, internal hostnames, source files, or logs containing sensitive workspace content.
