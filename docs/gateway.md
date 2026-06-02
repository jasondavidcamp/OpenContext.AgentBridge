# Gateway Provider Profile

A constrained gateway exposes an OpenAI-compatible chat API:

- `GET /v1/models`
- `POST /v1/chat/completions`

AgentBridge uses the generic `openai-compatible` provider for this shape. The provider sends a system message with the AgentBridge action protocol, then sends persisted conversation messages as standard chat messages.

## Workspace Config

```json
{
  "modelProvider": "gateway",
  "openAiCompatible": {
    "model": "<model-id-from-v1-models>",
    "endpoint": "https://gateway.example/v1",
    "apiKey": null,
    "apiKeyHeader": "Authorization",
    "apiKeyPrefix": "Bearer"
  }
}
```

`endpoint` can be either a base URL ending in `/v1` or the full `/v1/chat/completions` URL.

## Environment Config

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gateway"
$env:AGENTBRIDGE_GATEWAY_ENDPOINT = "https://gateway.example/v1"
$env:AGENTBRIDGE_GATEWAY_MODEL = "<model-id-from-v1-models>"
$env:AGENTBRIDGE_GATEWAY_API_KEY = "<key>"
```

The gateway OpenAPI document may say the API uses scoped API keys without specifying the exact header. AgentBridge defaults to `Authorization: Bearer <key>`. If the gateway expects a different header, set:

```powershell
$env:AGENTBRIDGE_GATEWAY_API_KEY_HEADER = "X-Gateway-Key"
$env:AGENTBRIDGE_GATEWAY_API_KEY_PREFIX = ""
```

## Diagnostics

For rapid local iteration before testing a real gateway, use the built-in simulator:

```powershell
.\scripts\Invoke-LocalGatewaySmoke.ps1
```

See [local-simulation.md](local-simulation.md) for details.

Run the Gateway smoke script:

```powershell
.\scripts\Invoke-GatewaySmoke.ps1 -Endpoint "https://gateway.example/v1" -Model "gemini-2.5-flash"
```

The script prompts for the API key without displaying it. By default it builds the repo, lists models, tests one model, validates the included PowerShell and .NET sandboxes, runs a no-edit agent inspection, runs a small edit/validate/diff loop against `examples/powershell-sandbox`, runs a symbol-aware C# edit/validate/diff loop against `examples/sandbox-project`, then resets the sample files. It also sets a 300-second model timeout for slow gateway responses. Use `-ConnectivityOnly` to stop after the model and sandbox checks, or `-KeepChanges` to leave the sample edits in place.

Use `-OutputDirectory` to write a diagnostics bundle with a redacted environment summary, pass/fail JSON, and one log per command step:

```powershell
.\scripts\Invoke-GatewaySmoke.ps1 -Endpoint "https://gateway.example/v1" -Model "gemini-2.5-flash" -OutputDirectory ".agentbridge\diagnostics\gateway-smoke" -ZipDiagnostics
```

The zip file is written next to the diagnostics directory when `-ZipDiagnostics` is supplied. API keys are not written to the bundle.

List available model IDs:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models list . --provider gateway --endpoint "<endpoint>" --api-key "<key>"
```

Test one model:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --provider gateway --endpoint "<endpoint>" --model "<model>" --api-key "<key>"
```

For troubleshooting only:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --provider gateway --log-traffic
```

Traffic logs are written to `.agentbridge/logs/`. They may contain prompt text, workspace context, model output, or tool instructions, so leave logging disabled for normal use.
