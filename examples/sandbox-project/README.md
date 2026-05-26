# AgentBridge Sandbox Project

This is a safe fixture for dogfooding AgentBridge against public model endpoints.

It intentionally contains a tiny .NET console app with no internal data, secrets, or dependencies beyond the .NET SDK. Use it when testing provider behavior, the JSON action loop, tool execution, and simple code edits.

## Validate

```powershell
dotnet run --project SandboxApp -- AgentBridge
```

Expected output:

```text
Hello, AgentBridge!
```

## Starter Dogfood Prompts

No-edit inspection:

```text
Inspect this sandbox project. Read the README and the app source, then return a final summary. Do not edit files.
```

Small edit:

```text
Modify the app so the greeting includes the phrase "from AgentBridge", run the validation command, then show the git diff.
```
