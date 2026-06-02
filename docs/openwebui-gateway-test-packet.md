# Open WebUI Gateway Test Packet

Use this packet to prove that Open WebUI can talk to AgentBridge Server, and that AgentBridge Server can use a constrained OpenAI-compatible gateway behind it.

## Goal

Prove this chain:

```text
Open WebUI -> AgentBridge Server -> OpenAI-compatible gateway -> model
```

The test uses `agentbridge-agent`, so success means Open WebUI is not only chatting through the gateway; it is reaching AgentBridge's bridge-owned tool loop and receiving tool-call metadata.

## Prerequisites

- .NET 10 SDK
- Git
- Docker Desktop
- A gateway endpoint ending in `/v1`
- A model id from `/v1/models`
- A gateway API key
- An Open WebUI container image available to the workstation

If the default Open WebUI image is not available, use the `-Image` parameter with an approved image reference.

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

## 2. Baseline Check

```powershell
dotnet --version
```

```powershell
docker version
```

```powershell
.\scripts\Invoke-CheapRegression.ps1 -SkipOpenWebUi
```

Expected:

- `Cheap regression passed.`

## 3. Gateway Connectivity

Use your real endpoint and model id. Do not paste the API key into notes, chat, or issue text.

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

```powershell
dotnet run --project src\OpenContext.AgentBridge.Cli -- models list . --provider gateway --endpoint $endpoint --api-key $plainKey
```

```powershell
dotnet run --project src\OpenContext.AgentBridge.Cli -- models test . --provider gateway --endpoint $endpoint --model $model --api-key $plainKey
```

Expected:

- Model list succeeds.
- Model test succeeds.

## 4. Open WebUI Gateway Smoke

Run:

```powershell
.\scripts\Invoke-OpenWebUiGatewaySmoke.ps1 -Endpoint $endpoint -Model $model -ApiKey $plainKey -Recreate
```

If the default Open WebUI image is unavailable, run:

```powershell
.\scripts\Invoke-OpenWebUiGatewaySmoke.ps1 -Endpoint $endpoint -Model $model -ApiKey $plainKey -Recreate -Image "<approved-openwebui-image>"
```

Expected:

- AgentBridge Server starts.
- Open WebUI starts.
- Open WebUI exposes `agentbridge-agent`.
- Open WebUI streaming chat succeeds.
- The result includes a `conversation_id`.
- The conversation details show at least one successful tool call.
- Final output includes `Open WebUI gateway smoke passed.`

The script prints log paths under `.agentbridge\openwebui-smoke-logs`.

## 5. Manual Browser Check

Open:

```text
http://127.0.0.1:3100
```

If prompted:

```text
admin@localhost / admin
```

Select:

```text
agentbridge-agent
```

Send:

```text
Inspect only README.md. Do not edit files. Return one sentence and mention whether you used tools.
```

Expected:

- The model responds through Open WebUI.
- The response is about the repository.
- AgentBridge logs show the tool call details.

## 6. Stop Services

```powershell
docker rm -f agentbridge-openwebui
```

If the script printed a server process id:

```powershell
Stop-Process -Id <server-process-id> -Force
```

If needed, stop any remaining matching local bridge processes:

```powershell
.\scripts\Stop-LocalSimulatorBridge.ps1 -All
```

## Feedback To Send Back

Send:

- Commit tested
- `dotnet --version`
- Whether Docker worked
- Open WebUI image used
- Gateway model id used
- Whether `models list` passed
- Whether `models test` passed
- Whether `Invoke-OpenWebUiGatewaySmoke.ps1` ended with `Open WebUI gateway smoke passed.`
- The result JSON from the final section, redacted if needed
- Any error text, redacted for secrets

Do not send API keys, bearer tokens, cookies, internal hostnames, source files, or logs containing sensitive workspace content.
