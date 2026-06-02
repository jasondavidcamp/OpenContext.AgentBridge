# Gemini OpenAI-Compatible Provider Profile

Google documents an OpenAI-compatible Gemini endpoint with this base URL:

```text
https://generativelanguage.googleapis.com/v1beta/openai/
```

Reference: https://ai.google.dev/gemini-api/docs/openai

AgentBridge can use that endpoint through the generic `openai-compatible` provider. This is useful for rehearsing the same chat-completions contract that a constrained gateway exposes, while still using the free Gemini API key you generated in Google AI Studio.

## Environment Config

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
$env:AGENTBRIDGE_OPENAI_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_OPENAI_API_KEY = $env:AGENTBRIDGE_GEMINI_API_KEY
```

The `gemini-openai` provider name is an AgentBridge alias for `openai-compatible`; it exists so config and diagnostics make it obvious which compatibility surface you are testing.

## Diagnostics

For the cheapest live server canary, run one tiny chat completion through AgentBridge Server to Gemini's OpenAI-compatible endpoint:

```powershell
.\scripts\Invoke-GeminiServerCanary.ps1
```

This starts the local server, sends one low-token raw-proxy request, validates the exact response text, and stops the server. Add `-IncludeAgentMode` when you intentionally want to spend a few more requests to test the full workspace-aware agent loop against live Gemini.

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models list . --provider gemini-openai --endpoint "https://generativelanguage.googleapis.com/v1beta/openai/" --api-key "$env:AGENTBRIDGE_GEMINI_API_KEY"
```

```powershell
dotnet run --project src/OpenContext.AgentBridge.Cli -- models test . --provider gemini-openai --endpoint "https://generativelanguage.googleapis.com/v1beta/openai/" --model gemini-2.5-flash --api-key "$env:AGENTBRIDGE_GEMINI_API_KEY"
```

This path should be treated as a contract rehearsal, not a security rehearsal. Do not send restricted code, secrets, hostnames, logs, or internal data to the public Gemini endpoint.
