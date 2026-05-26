# Configuration

AgentBridge reads workspace configuration from:

```text
.agentbridge/config.json
```

Create a starter config:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- config init .
```

Show the effective config with secrets redacted:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- config show .
```

## Precedence

Effective configuration is resolved in this order:

1. CLI flags
2. Environment variables
3. `.agentbridge/config.json`
4. Defaults

## Starter Config

```json
{
  "modelProvider": "gemini",
  "defaultExecutor": "host",
  "maxIterations": 8,
  "logModelTraffic": false,
  "gemini": {
    "model": "gemini-1.5-pro",
    "endpoint": null,
    "apiKey": null
  },
  "openAiCompatible": {
    "model": "gpt-4",
    "endpoint": null,
    "apiKey": null,
    "apiKeyHeader": "Authorization",
    "apiKeyPrefix": "Bearer",
    "temperature": null,
    "maxTokens": null
  }
}
```

Prefer environment variables for secrets:

```powershell
$env:AGENTBRIDGE_GEMINI_API_KEY = "<key>"
$env:AGENTBRIDGE_GEMINI_ENDPOINT = "<endpoint>"
$env:AGENTBRIDGE_GEMINI_MODEL = "<model>"
```

For STARK or another OpenAI-compatible gateway:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "stark"
$env:AGENTBRIDGE_STARK_ENDPOINT = "https://stark.example.mil/v1"
$env:AGENTBRIDGE_STARK_MODEL = "<model-id-from-v1-models>"
$env:AGENTBRIDGE_STARK_API_KEY = "<key>"
```

If the gateway expects a custom API key header instead of `Authorization: Bearer <key>`:

```powershell
$env:AGENTBRIDGE_STARK_API_KEY_HEADER = "X-STARK-Key"
$env:AGENTBRIDGE_STARK_API_KEY_PREFIX = ""
```

The generic names also work:

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "openai-compatible"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "<base-url-or-full-chat-completions-url>"
$env:AGENTBRIDGE_OPENAI_MODEL = "<model>"
$env:AGENTBRIDGE_OPENAI_API_KEY = "<key>"
```

## Model Diagnostics

List STARK/OpenAI-compatible models:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models list . --provider stark --endpoint "<endpoint>" --api-key "<key>"
```

Test the configured model endpoint:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test .
```

Override endpoint or model for one test:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --endpoint "<endpoint>" --model "<model>"
```

Test STARK without changing workspace config:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --provider stark --endpoint "<endpoint>" --model "<model>" --api-key "<key>"
```

Enable request and response logging for one test:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --log-traffic
```

Traffic logs are written to `.agentbridge/logs/`. Endpoint query secrets are redacted, but request and response bodies may contain workspace context or prompt text, so leave logging disabled unless you are debugging provider behavior.
