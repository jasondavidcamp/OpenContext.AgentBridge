# Local Gateway Simulation

AgentBridge includes a local OpenAI-compatible simulator so most agent-loop work can happen before testing a restricted gateway. The simulator exposes the same two endpoint shapes used by constrained gateways:

- `GET /v1/models`
- `POST /v1/chat/completions`

It intentionally returns deterministic tool-call sequences. This makes it useful for regression testing AgentBridge behavior such as model request formatting, action parsing, required tool use, file reads, text replacement, command validation, diffs, and final-answer handling.
The full smoke run also includes a symbol-aware C# edit where the simulator finds `Greeter.CreateGreeting` from the workspace map instead of being handed the file path in the user prompt.

## One-Command Smoke Run

From the repository root:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1
```

The wrapper script:

- builds the solution
- starts `OpenContext.AgentBridge.SimulatedGateway` on `http://127.0.0.1:5198`
- configures a temporary local API key
- runs `scripts/Invoke-GatewaySmoke.ps1` against the local simulator
- stops the simulator when the run finishes

Use `-ConnectivityOnly` when you only want to exercise model listing, model testing, and the PowerShell baseline:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1 -ConnectivityOnly
```

Use `-KeepChanges` to inspect the sample edit after the run:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1 -KeepChanges
```

Use `-OutputDirectory` and `-ZipDiagnostics` to test the diagnostics bundle locally:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1 -OutputDirectory ".agentbridge\diagnostics\local-smoke" -ZipDiagnostics
```

Simulator logs are written under `.agentbridge/simulator-logs/`.

## Local Server Edit Canary

To test the local OpenAI-compatible server path with an actual edit loop, run:

```powershell
.\scripts\Invoke-LocalServerEditCanary.ps1
```

The script creates a scratch git workspace under `.agentbridge`, copies only the safe PowerShell fixture, starts the local simulator and AgentBridge Server, asks `agentbridge-agent` to improve the fixture help text, verifies the saved tool-call details include `read_file`, `replace_text`, `run_command`, and `git_diff`, validates the script output is unchanged, and removes the scratch workspace unless `-KeepWorkspace` is set.

The cheap regression runs this canary by default. Use `.\scripts\Invoke-CheapRegression.ps1 -SkipEditCanary` only when the machine cannot run the PowerShell fixture validation.

## Manual Simulator Run

To run the simulator by hand:

```powershell
dotnet run --project src/OpenContext.AgentBridge.SimulatedGateway --urls http://127.0.0.1:5198
```

Then point AgentBridge at it:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
$env:AGENTBRIDGE_GATEWAY_ENDPOINT = "http://127.0.0.1:5198/v1"
$env:AGENTBRIDGE_GATEWAY_MODEL = "simulated-gemini-flash"
$env:AGENTBRIDGE_GATEWAY_API_KEY = "local-simulator-key"

dotnet run --project src/OpenContext.AgentBridge.Cli -- models test .
```

## When To Test The Real Gateway

Use the local simulator for rapid implementation loops. Test the real gateway only when a change touches:

- endpoint/auth assumptions
- HTTP headers, timeouts, retries, or rate-limit behavior
- prompts that may depend on real model behavior
- locked-down workstation assumptions
- a release milestone that needs real-environment confidence
