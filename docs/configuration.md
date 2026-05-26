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
  }
}
```

Prefer environment variables for secrets:

```powershell
$env:AGENTBRIDGE_GEMINI_API_KEY = "<key>"
$env:AGENTBRIDGE_GEMINI_ENDPOINT = "<endpoint>"
$env:AGENTBRIDGE_GEMINI_MODEL = "<model>"
```

## Model Diagnostics

Test the configured model endpoint:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test .
```

Override endpoint or model for one test:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --endpoint "<endpoint>" --model "<model>"
```

Enable request and response logging for one test:

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --log-traffic
```

Traffic logs are written to `.agentbridge/logs/`. Endpoint query secrets are redacted, but request and response bodies may contain workspace context or prompt text, so leave logging disabled unless you are debugging provider behavior.
