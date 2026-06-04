# Cline Docker

This packet tests Cline as the coding agent while AgentBridge stays a thin OpenAI-compatible endpoint adapter.

## Split Image Flow

```text
Cline-only container -> constrained proxy container -> upstream OpenAI-compatible endpoint -> model
```

Images:

- `cline-client:latest`: Cline client only.
- `opencontext-agentbridge-constrained-proxy:latest`: AgentBridge Server running as a constrained OpenAI-compatible proxy.

Run the split canary:

```powershell
.\scripts\Invoke-ClineSplitProxyCanary.ps1
```

The script:

- builds both images when needed
- creates a scratch text workspace
- starts a proxy container on a private Docker network
- points the Cline container at the proxy container by name
- asks Cline to inspect files, edit a file, and run a Node-based validation command

Use a different endpoint and model:

```powershell
.\scripts\Invoke-ClineSplitProxyCanary.ps1 -Endpoint "https://gateway.example/v1" -Model "gemini-2.5-flash"
```

If the images already exist:

```powershell
.\scripts\Invoke-ClineSplitProxyCanary.ps1 -SkipClineImageBuild -SkipProxyImageBuild
```

## Runtime Image Flow

Use this when you intentionally want the Cline container to include extra local tooling for a language-specific canary.

```text
Cline plus .NET container -> host AgentBridge Server raw proxy -> upstream OpenAI-compatible endpoint -> model
```

Run the .NET-backed canary:

```powershell
.\scripts\Invoke-ClineGatewayCanary.ps1
```

The script:

- builds `opencontext-agentbridge-cline-dotnet:latest` when needed
- starts AgentBridge Server on a local port
- points Cline at `http://host.docker.internal:<port>/v1`
- creates a scratch .NET workspace
- asks Cline to inspect files, edit code, and run validation
- validates the result from the host

Use a different endpoint and model:

```powershell
.\scripts\Invoke-ClineGatewayCanary.ps1 -Endpoint "https://gateway.example/v1" -Model "gemini-2.5-flash"
```

If the image already exists:

```powershell
.\scripts\Invoke-ClineGatewayCanary.ps1 -SkipImageBuild
```

## Notes

Cline sends a rich OpenAI-compatible request that includes native tool definitions. AgentBridge strips a few client-specific reasoning fields before forwarding raw proxy requests, but it preserves `tools` so the model can request Cline tool calls.

This means the upstream endpoint still needs to preserve tool-call request and response fields for Cline to be fully agentic. If an endpoint only accepts plain chat messages and drops tool definitions, use `agentbridge-agent` mode instead of Cline raw proxy mode.
