# PowerShell Sandbox

This fixture is safe for public endpoint dogfooding. It contains a tiny PowerShell script with no internal data, secrets, network calls, or environment-specific dependencies.

## Validate

```powershell
pwsh -NoProfile -File .\Get-Greeting.ps1 -Name AgentBridge
```

Expected output:

```text
Hello, AgentBridge!
```

If `pwsh` is unavailable, use Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Get-Greeting.ps1 -Name AgentBridge
```

## Starter Dogfood Prompt

```text
Use the PowerShell skill. Inspect this PowerShell sandbox, improve the script help text without changing runtime behavior, run the validation command, then show the git diff.
```
