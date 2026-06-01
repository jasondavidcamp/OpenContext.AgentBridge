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

AgentBridge executes the tool, stores the full tool call record for audit/debugging, adds a compact `TOOL_RESULT` message to the conversation, and calls the model again. Large tool results are shown to the model as a head-and-tail preview with the middle omitted so command output and file reads do not consume the whole chat budget.

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
- `replace_text`: replace exact text in one file
- `search`: search text with ripgrep
- `write_file`: create or replace a text file
- `run_command`: run a shell command from the workspace root
- `git_status`: show concise git status
- `git_diff`: show the current git diff

File tools enforce the workspace boundary. `replace_text` is safest for small exact substitutions after reading the target file. Use `apply_patch` for broader targeted edits to existing code, not for simple one-line substitutions. Patch paths should be workspace-relative; AgentBridge also normalizes safe paths that include the active workspace folder prefix, such as `examples/sandbox-project/SandboxApp/Program.cs` when the workspace is already `examples/sandbox-project`. `git_status` and `git_diff` are scoped to the workspace path, even when the workspace is a subdirectory of a larger repository. `run_command` starts in the workspace root using the selected executor. Use the Docker executor when command isolation is more important than direct host access.

AgentBridge accepts a small set of recovery aliases for common model mistakes, such as `execute_command` -> `run_command` and `list_directory` -> `list_files`, but prompts still instruct models to use canonical tool names.

For smoke tests or high-confidence workflows, `ask --require-tool-calls <n>` rejects final answers until the current run has completed at least that many successful tool calls. This is useful when a model is prone to claiming it inspected, edited, validated, or diffed files without actually using tools.

## Parser Recovery

AgentBridge accepts a JSON object directly, inside a fenced code block, or embedded in otherwise harmless prose. If no valid directive can be parsed, the response is stored and AgentBridge adds a `MODEL_RESPONSE_PARSE_ERROR` observation asking the model to retry with exactly one JSON object.
