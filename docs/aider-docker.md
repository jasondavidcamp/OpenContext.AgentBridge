# Aider Docker Bridge Spike

Aider is the first external coding agent to test before AgentBridge grows more fallback-agent behavior. Aider already provides repo mapping, diff-based edits, validation command loops, git awareness, and an OpenAI-compatible model path. AgentBridge should only wrap or shim the pieces that are hard in a constrained workstation or gateway environment.

References:

- Aider Docker docs: https://aider.chat/docs/install/docker.html
- Aider OpenAI-compatible docs: https://aider.chat/docs/llms/openai-compat.html

## What This Proves

The official `paulgauthier/aider` Docker image can run on the personal workstation path and can call Google's OpenAI-compatible Gemini endpoint with a public sandbox repo mounted into the container.

The first dry-run test on June 1, 2026:

- pulled `paulgauthier/aider`
- ran `aider 0.86.2`
- mounted this repository into `/app`
- built an Aider repo map over the public sandbox files
- received a valid Gemini response through the OpenAI-compatible endpoint
- then hit the free Gemini daily request quota during follow-up traffic

That means the immediate risk is not API shape. The immediate risks are quota, model quality, gateway auth details, and whether the restricted workstation can pull or receive a trusted Aider image.

The first edit test on June 2, 2026:

- used free Gemini `gemini-2.5-flash`
- disabled the repo map with `--map-tokens 0`
- made only `examples/sandbox-project/SandboxApp/Program.cs` editable
- kept `examples/sandbox-project/README.md` read-only
- changed `Greeter.CreateGreeting` from `Hello, {name}!` to `Hello, {name} from AgentBridge!`
- used about 1.0k input tokens and 98 output tokens
- validated on the host with `dotnet run --project examples\sandbox-project\SandboxApp -- AgentBridge`

The sandbox file was restored after the test so the fixture remains reusable.

Validation command check:

- the official `paulgauthier/aider` image does not include `dotnet`
- it can edit the public sandbox but cannot run the .NET validation command internally
- `docker/aider-dotnet.Dockerfile` provides a tested path for Aider plus the .NET 10 SDK
- the Aider plus .NET image ran the same edit and executed `dotnet run --project examples/sandbox-project/SandboxApp -- AgentBridge` inside Docker successfully
- the wrapper sets .NET, NuGet, home, and XDG paths inside the container so validation state does not get written into the mounted repository

## Launcher

Use the wrapper script instead of typing a long `docker run` command:

```powershell
.\scripts\Start-AiderDocker.ps1 -ShowVersion
```

For public Gemini rehearsal:

```powershell
$env:AGENTBRIDGE_GEMINI_API_KEY = [Environment]::GetEnvironmentVariable("AGENTBRIDGE_GEMINI_API_KEY", "User")

.\scripts\Start-AiderDocker.ps1 `
  -Pull `
  -Workspace . `
  -Model gemini-2.5-flash `
  -OpenAiApiBase "https://generativelanguage.googleapis.com/v1beta/openai/" `
  -DryRun `
  -NoTty `
  -Read @("examples/sandbox-project/README.md", "examples/sandbox-project/SandboxApp/Program.cs") `
  -Message "Inspect examples/sandbox-project/README.md and examples/sandbox-project/SandboxApp/Program.cs. Do not edit files. Say whether this is enough context for a greeting-change test."
```

To pass extra Aider flags, place them after `--`. This is useful for low-quota smoke tests:

```powershell
.\scripts\Start-AiderDocker.ps1 `
  -Workspace . `
  -Model gemini-2.5-flash `
  -OpenAiApiBase "https://generativelanguage.googleapis.com/v1beta/openai/" `
  -DryRun `
  -NoTty `
  -Read @("examples/sandbox-project/README.md", "examples/sandbox-project/SandboxApp/Program.cs") `
  -Message "Do not edit files. Say whether this sandbox is suitable for testing a greeting text change." `
  -- --map-tokens 0 --no-stream --no-pretty
```

For a constrained OpenAI-compatible gateway that accepts bearer API keys:

```powershell
$env:AGENTBRIDGE_GATEWAY_ENDPOINT = "https://gateway.example/v1"
$env:AGENTBRIDGE_GATEWAY_MODEL = "<model-id-from-v1-models>"
$env:AGENTBRIDGE_GATEWAY_API_KEY = "<key>"

.\scripts\Start-AiderDocker.ps1 `
  -Workspace . `
  -Message "Inspect this repository and identify the safest first PowerShell sandbox edit. Do not edit files yet."
```

To allow edits, omit `-DryRun`. Keep `--no-auto-commits` as the default until the workflow is trusted.

Low-token public Gemini edit test:

```powershell
.\scripts\Start-AiderDocker.ps1 `
  -Workspace . `
  -Model gemini-2.5-flash `
  -OpenAiApiBase "https://generativelanguage.googleapis.com/v1beta/openai/" `
  -NoTty `
  -File @("examples/sandbox-project/SandboxApp/Program.cs") `
  -Read @("examples/sandbox-project/README.md") `
  -Message "Modify examples/sandbox-project/SandboxApp/Program.cs so Greeter.CreateGreeting includes the phrase 'from AgentBridge' while preserving the supplied name. Do not edit any other files. Do not run tests." `
  -- --map-tokens 0 --no-stream --no-pretty --yes-always
```

Then validate from the host:

```powershell
dotnet run --project examples\sandbox-project\SandboxApp -- AgentBridge
```

## Aider Plus .NET Image

Build the local image when Aider needs to run .NET validation commands inside Docker:

```powershell
docker build -f docker\aider-dotnet.Dockerfile -t opencontext-agentbridge-aider-dotnet:latest .
```

Verify the image:

```powershell
.\scripts\Start-AiderDocker.ps1 -Image opencontext-agentbridge-aider-dotnet:latest -ShowVersion
```

The image keeps the same Aider entrypoint as the official image, but adds the .NET 10 SDK and common command-line tools. Use it with the wrapper's `-Image` parameter:

```powershell
.\scripts\Start-AiderDocker.ps1 `
  -Image opencontext-agentbridge-aider-dotnet:latest `
  -Workspace . `
  -Model gemini-2.5-flash `
  -OpenAiApiBase "https://generativelanguage.googleapis.com/v1beta/openai/" `
  -NoTty `
  -File @("examples/sandbox-project/SandboxApp/Program.cs") `
  -Read @("examples/sandbox-project/README.md") `
  -TestCommand "dotnet run --project examples/sandbox-project/SandboxApp -- AgentBridge" `
  -AutoTest `
  -Message "Modify examples/sandbox-project/SandboxApp/Program.cs so Greeter.CreateGreeting includes the phrase 'from AgentBridge' while preserving the supplied name. Do not edit any other files." `
  -- --map-tokens 0 --no-stream --no-pretty --yes-always
```

## Boundary Decision

If Aider can run acceptably against the target gateway, AgentBridge should become:

- configuration and launch wrapper
- endpoint/auth compatibility shim when needed
- diagnostics bundle generator
- simulator and contract-test harness
- policy wrapper for workspace boundaries

AgentBridge should not duplicate Aider's repo map, edit formats, interactive coding loop, or git workflow unless Aider cannot be made to work within the target constraints.

## Watch Items

- Aider direct mode assumes an OpenAI-compatible bearer-auth endpoint. If the gateway requires a different auth header, AgentBridge may need to provide a tiny local proxy.
- The public Gemini free tier is enough to prove connectivity, but it is too tight for many iterative coding tests.
- Docker image availability may be different on restricted machines. If the image cannot be pulled there, export/import the image or rebuild from an approved base image.
- Commands run from inside the Aider container. Validation commands must exist in the container image or be delegated back to the host by a separate wrapper.
