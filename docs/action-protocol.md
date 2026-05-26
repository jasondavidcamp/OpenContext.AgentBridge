# Action Protocol

AgentBridge uses a simple JSON protocol so models with limited chat APIs can still request tool execution.

The model must return exactly one JSON object per turn.

## Tool Action

```json
{
  "type": "tool",
  "tool": "read_file",
  "arguments": {
    "path": "README.md"
  }
}
```

AgentBridge executes the tool, stores the tool call, adds a `TOOL_RESULT` message to the conversation, and calls the model again.

## Final Answer

```json
{
  "type": "final",
  "message": "Summary of the completed work."
}
```

## Initial Tools

- `apply_patch`: apply a unified diff patch after validating patch paths
- `list_files`: list files and directories under a workspace path
- `read_file`: read a text file
- `search`: search text with ripgrep
- `write_file`: create or replace a text file
- `run_command`: run a shell command from the workspace root
- `git_status`: show concise git status
- `git_diff`: show the current git diff

File tools enforce the workspace boundary. `apply_patch` should be preferred over `write_file` for targeted edits to existing code. `run_command` starts in the workspace root using the selected executor. Use the Docker executor when command isolation is more important than direct host access.
