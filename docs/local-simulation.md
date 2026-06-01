# Local STARK Simulation

AgentBridge includes a local OpenAI-compatible simulator so most agent-loop work can happen before testing a restricted gateway. The simulator exposes the same two endpoint shapes used by STARK-style gateways:

- `GET /v1/models`
- `POST /v1/chat/completions`

It intentionally returns deterministic tool-call sequences. This makes it useful for regression testing AgentBridge behavior such as model request formatting, action parsing, required tool use, file reads, text replacement, command validation, diffs, and final-answer handling.

## One-Command Smoke Run

From the repository root:

```powershell
.\scripts\Invoke-LocalStarkSmoke.ps1
```

The wrapper script:

- builds the solution
- starts `OpenContext.AgentBridge.SimulatedStark` on `http://127.0.0.1:5198`
- configures a temporary local API key
- runs `scripts/Invoke-StarkSmoke.ps1` against the local simulator
- stops the simulator when the run finishes

Use `-ConnectivityOnly` when you only want to exercise model listing, model testing, and the PowerShell baseline:

```powershell
.\scripts\Invoke-LocalStarkSmoke.ps1 -ConnectivityOnly
```

Use `-KeepChanges` to inspect the sample edit after the run:

```powershell
.\scripts\Invoke-LocalStarkSmoke.ps1 -KeepChanges
```

Simulator logs are written under `.agentbridge/simulator-logs/`.

## Manual Simulator Run

To run the simulator by hand:

```powershell
dotnet run --project src/OpenContext.AgentBridge.SimulatedStark --urls http://127.0.0.1:5198
```

Then point AgentBridge at it:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "stark"
$env:AGENTBRIDGE_STARK_ENDPOINT = "http://127.0.0.1:5198/v1"
$env:AGENTBRIDGE_STARK_MODEL = "simulated-gemini-flash"
$env:AGENTBRIDGE_STARK_API_KEY = "local-simulator-key"

dotnet run --project src/OpenContext.AgentBridge.Cli -- models test .
```

## When To Test The Real Gateway

Use the local simulator for rapid implementation loops. Test the real gateway only when a change touches:

- endpoint/auth assumptions
- HTTP headers, timeouts, retries, or rate-limit behavior
- prompts that may depend on real model behavior
- locked-down workstation assumptions
- a release milestone that needs real-environment confidence
