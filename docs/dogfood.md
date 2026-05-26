# Dogfood Workflow

Use the sandbox project when testing public model endpoints. It has no internal data and can be safely inspected or edited by a public Gemini key.

## Gemini OpenAI-Compatible Setup

```powershell
$env:AGENTBRIDGE_MODEL_PROVIDER = "gemini-openai"
$env:AGENTBRIDGE_OPENAI_ENDPOINT = "https://generativelanguage.googleapis.com/v1beta/openai/"
$env:AGENTBRIDGE_OPENAI_MODEL = "gemini-2.5-flash"
$env:AGENTBRIDGE_OPENAI_API_KEY = [Environment]::GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY", "User")
```

## No-Edit Smoke

```powershell
dotnet run --project src\OpenContext.AgentBridge.Cli -- ask examples\sandbox-project --new --max-iterations 6 "Inspect this sandbox project. Read the README and the app source, then return a final summary. Do not edit files."
```

## Edit Smoke

```powershell
dotnet run --project src\OpenContext.AgentBridge.Cli -- ask examples\sandbox-project --new --max-iterations 8 "Modify the app so the greeting includes the phrase 'from AgentBridge', run the validation command, then show the git diff."
```

Review the diff before committing sandbox changes. Reset or keep the sandbox change depending on what you meant to test.

## What To Watch

- The model should return JSON action objects, not prose.
- If the model drifts into prose, AgentBridge should emit an invalid-response message and ask for a corrected JSON action.
- Tool calls should stay inside `examples/sandbox-project`.
- The final answer should mention what was inspected or changed and any validation command result.
