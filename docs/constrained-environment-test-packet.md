# Constrained Environment Test Packet

Use this packet when testing AgentBridge on a separate workstation where the feedback loop is slower. It is intentionally generic and avoids environment-specific names.

## Goal

Prove three things before deeper testing:

- The repo builds on the workstation.
- The no-key simulator bridge works locally.
- The real OpenAI-compatible gateway can list models and complete a tiny chat request.

## Prerequisites

- .NET 10 SDK
- Git
- PowerShell 7 preferred, Windows PowerShell acceptable for most scripts
- Docker Desktop only if running the Open WebUI smoke
- Gateway endpoint, model id, and API key for live gateway testing

## 1. Clone And Build

```powershell
git clone https://github.com/jasondavidcamp/OpenContext.AgentBridge.git
cd OpenContext.AgentBridge
dotnet --version
dotnet build .\OpenContext.AgentBridge.sln
dotnet test .\OpenContext.AgentBridge.sln
```

Capture:

- .NET SDK version
- Build result
- Test result

## 2. No-Key Local Simulator Check

```powershell
.\scripts\Start-LocalSimulatorBridge.ps1
```

In a second PowerShell window:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5320/v1/models" |
  ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "http://127.0.0.1:5320/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Body (@{
    model = "agentbridge-agent"
    messages = @(
      @{ role = "user"; content = "Inspect only README.md. Do not edit files. Return one sentence." }
    )
    stream = $false
  } | ConvertTo-Json -Depth 10) |
  ConvertTo-Json -Depth 10
```

Stop the local simulator bridge:

```powershell
.\scripts\Stop-LocalSimulatorBridge.ps1
```

Expected:

- `/v1/models` includes `agentbridge-agent` and `simulated-gemini-flash`.
- The chat response includes an `agentbridge` metadata object with a `conversation_id`.
- The stop script shuts down the local simulator and AgentBridge Server.

Capture:

- Any errors
- The model ids returned
- Whether the chat response included `agentbridge.conversation_id`

## 3. Cheap Regression

Run the no-cost regression path:

```powershell
.\scripts\Invoke-CheapRegression.ps1 -SkipOpenWebUi
```

If Docker Desktop is available and allowed:

```powershell
.\scripts\Invoke-CheapRegression.ps1
```

Expected:

- Build passes.
- Local server smoke passes.
- Tests pass.
- Format verification passes.
- Reserved-term scan passes.
- Optional Open WebUI simulator smoke passes when Docker is available.

Capture:

- Final `Cheap regression passed.` line, or the failing section and error.

## 4. Live Gateway Connectivity

Use placeholders for the endpoint, model, and key. Do not paste secrets into chat or issue text.

```powershell
$endpoint = "https://gateway.example/v1"
$model = "<model-id>"
$apiKey = Read-Host "Gateway API key" -AsSecureString
$plainKey = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
  [Runtime.InteropServices.Marshal]::SecureStringToBSTR($apiKey)
)

dotnet run --project src\OpenContext.AgentBridge.Cli -- `
  models list . `
  --provider gateway `
  --endpoint $endpoint `
  --api-key $plainKey

dotnet run --project src\OpenContext.AgentBridge.Cli -- `
  models test . `
  --provider gateway `
  --endpoint $endpoint `
  --model $model `
  --api-key $plainKey
```

Expected:

- Model list succeeds.
- Model test returns `model test ok` or an equivalent successful response.

Capture:

- Endpoint shape only, not the key
- Model ids available
- HTTP status or error text if it fails

## 5. Live Gateway Smoke

Run the gateway smoke only after the connectivity check passes:

```powershell
.\scripts\Invoke-GatewaySmoke.ps1 `
  -Endpoint "https://gateway.example/v1" `
  -Model "<model-id>" `
  -ConnectivityOnly `
  -OutputDirectory ".agentbridge\diagnostics\gateway-smoke" `
  -ZipDiagnostics
```

For deeper testing, remove `-ConnectivityOnly` after reviewing local policy and sample-file safety.

Expected:

- Connectivity-only smoke completes.
- Diagnostics are written under `.agentbridge\diagnostics\gateway-smoke`.
- Optional zip file is created when `-ZipDiagnostics` is used.

## Feedback To Send Back

Send a short summary with:

- Commit tested
- OS and PowerShell version
- .NET SDK version
- Whether Docker was available
- Simulator check result
- Cheap regression result
- Gateway model ids
- Gateway connectivity result
- Any error messages, redacted for secrets

Do not send API keys, bearer tokens, cookies, internal hostnames, source files, or logs that include sensitive workspace content.
