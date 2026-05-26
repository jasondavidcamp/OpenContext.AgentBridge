# ADR 0001: Start with a local .NET orchestrator and optional Docker execution

## Status

Accepted

## Context

AgentBridge is meant to provide a Codex-like development experience in constrained AI environments where the model API exposes limited tooling. The first users are a small infrastructure team, so the highest-value proof is a daily-driver workflow that can inspect a workspace, run commands, persist context, and eventually modify code.

The target environment is Windows-heavy and enterprise/government constrained. Docker Desktop is available on issued laptops, which makes it a useful way to package development tools without installing every dependency directly on the host.

## Decision

Start with a .NET CLI and core library running on the host. Model integration, command execution, storage, and skills are explicit boundaries:

- .NET CLI for Windows-friendly distribution and enterprise fit
- Workspace-scoped guardrails in the core library
- Host command execution first
- Docker command execution as a pluggable executor
- Gemini behind a provider interface
- SQLite for local conversation persistence
- Markdown-based skills as the initial skill format

## Consequences

This gives the project a usable vertical slice quickly while leaving room to add a richer UI, stronger skill manifests, tool-calling protocols, and additional model providers later.

Docker is not mandatory for the first loop, but the execution boundary is present from the beginning so a locked-down PC can use a prebuilt tool image when that is helpful.
