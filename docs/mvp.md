# MVP

The first useful version of AgentBridge should prove this workflow:

```text
agentbridge <workspace>
> inspect the repository
> make a targeted code change
> run the relevant command or test
> summarize what changed and persist the conversation
```

## First Slice

- Workspace-scoped file and command access
- Host executor
- Docker executor
- Gemini provider boundary
- SQLite-backed chats
- Markdown skills loaded from the workspace
- Git status/diff visibility
- JSON action loop for tool execution
- Patch-based code editing

## Next Decisions

- Skill manifest format
- Long-running task model
- Web or desktop UI after the CLI is useful
